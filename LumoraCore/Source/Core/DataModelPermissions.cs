// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using Lumora.Core.Networking.Sync;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core;

[Flags]
public enum DataModelPermissionAction : ulong
{
    None = 0,
    Read = 1UL << 0,
    Write = 1UL << 1,
    Create = 1UL << 2,
    Destroy = 1UL << 3,
    ReferenceWrite = 1UL << 4,
    CollectionEnumerate = 1UL << 5,
    CollectionAdd = 1UL << 6,
    CollectionInsert = 1UL << 7,
    CollectionSet = 1UL << 8,
    CollectionRemove = 1UL << 9,
    CollectionClear = 1UL << 10,
    CollectionResize = 1UL << 11,
    Replicate = 1UL << 12,
    Serialize = 1UL << 13,
    ConfigurePermissions = 1UL << 14,

    CollectionMutation = CollectionAdd | CollectionInsert | CollectionSet | CollectionRemove | CollectionClear | CollectionResize,
    Mutation = Write | Create | Destroy | ReferenceWrite | CollectionMutation,
    All = ulong.MaxValue
}

public enum DataModelPermissionSurface
{
    Unknown,
    Field,
    SyncElement,
    Array,
    List,
    Dictionary,
    Bag,
    ReplicatedDictionary,
    Worker,
    Slot,
    Component,
    User
}

public enum DataModelPermissionResult
{
    Abstain,
    Allow,
    Deny
}

/// <summary>
/// How a user relates to the world when they join — used to pick a default role.
/// </summary>
public enum DataModelAccessClass
{
    Anonymous,
    Visitor,
    Contact,
    Host
}

public readonly struct DataModelPermissionRequest
{
    public readonly World? World;
    public readonly User? Actor;
    public readonly IWorldElement? Target;
    public readonly IWorldElement? Parent;
    public readonly ISyncMember? Member;
    public readonly DataModelPermissionSurface Surface;
    public readonly DataModelPermissionAction Action;
    public readonly bool IsNetwork;
    public readonly bool IsFullState;
    public readonly int? Index;
    public readonly object? Key;

    public DataModelPermissionRequest(
        World? world,
        User? actor,
        IWorldElement? target,
        IWorldElement? parent,
        ISyncMember? member,
        DataModelPermissionSurface surface,
        DataModelPermissionAction action,
        bool isNetwork,
        bool isFullState = false,
        int? index = null,
        object? key = null)
    {
        World = world;
        Actor = actor;
        Target = target;
        Parent = parent;
        Member = member;
        Surface = surface;
        Action = action;
        IsNetwork = isNetwork;
        IsFullState = isFullState;
        Index = index;
        Key = key;
    }
}

public interface IDataModelPermissionRule
{
    DataModelPermissionResult Evaluate(in DataModelPermissionRequest request, out string? reason);
}

public sealed class DataModelPermissionRole
{
    public string Name { get; }
    public DataModelPermissionAction AllowedOwnActions { get; set; }
    public DataModelPermissionAction AllowedForeignActions { get; set; }

    public DataModelPermissionRole(string name, DataModelPermissionAction allowedOwnActions, DataModelPermissionAction allowedForeignActions)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Unnamed" : name;
        AllowedOwnActions = allowedOwnActions;
        AllowedForeignActions = allowedForeignActions;
    }

    public bool Allows(DataModelPermissionAction action, bool ownsTarget)
    {
        var allowed = ownsTarget ? AllowedOwnActions : AllowedForeignActions;
        return (allowed & action) == action;
    }

    public override string ToString() => Name;
}

public sealed class DataModelPermissionController
{
    private sealed class Scope : IDisposable
    {
        private readonly User? _previousActor;
        private readonly int _previousBypassDepth;
        private bool _disposed;

        public Scope(User? actor, bool systemBypass)
        {
            _previousActor = s_currentActor.Value;
            _previousBypassDepth = s_systemBypassDepth.Value;
            s_currentActor.Value = actor;
            if (systemBypass)
            {
                s_systemBypassDepth.Value = _previousBypassDepth + 1;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            s_currentActor.Value = _previousActor;
            s_systemBypassDepth.Value = _previousBypassDepth;
            _disposed = true;
        }
    }

    private static readonly ThreadLocal<User?> s_currentActor = new();
    private static readonly ThreadLocal<int> s_systemBypassDepth = new();

    private readonly World _world;
    private readonly List<IDataModelPermissionRule> _rules = new();
    private readonly Dictionary<RefID, DataModelPermissionRole> _userRoles = new();

    // Distinct denials already logged, so a per-frame denial doesn't spam thousands of identical lines. -xlinka
    private readonly HashSet<string> _loggedDenials = new();

    // Owner of the world — full power, not assignable (you can't demote the host).
    public DataModelPermissionRole HostRole { get; }
    // Assignable roles, in descending power. Each has preset capabilities over OTHER users' objects
    // (own objects are always fully editable). The host assigns these per access-class or per-user.
    public DataModelPermissionRole AdminRole { get; }
    public DataModelPermissionRole BuilderRole { get; }
    public DataModelPermissionRole ModeratorRole { get; }
    public DataModelPermissionRole GuestRole { get; }
    public DataModelPermissionRole SpectatorRole { get; }
    public IReadOnlyList<DataModelPermissionRole> AssignableRoles { get; }

    private readonly Dictionary<DataModelAccessClass, DataModelPermissionRole> _defaultRoles = new();

    public bool Enabled { get; set; } = true;
    public bool LogDeniedMutations { get; set; } = true;

    /// <summary>
    /// Social/Event lockdown. When set, the authored world is frozen for EVERYONE (including the host
    /// and admins): only a user's OWN runtime objects (avatar, spawned items) may be mutated. World
    /// content is created under the authority RefID, so no user "owns" it and all editing of it is
    /// denied - there's no role that escapes this and no live toggle. Set by <see cref="WorldModePermissions"/>.
    /// </summary>
    public bool SocialLock { get; set; }

    public DataModelPermissionController(World world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));

        var all = DataModelPermissionAction.All;
        var view = DataModelPermissionAction.Read | DataModelPermissionAction.CollectionEnumerate;
        var build = view | DataModelPermissionAction.Create | DataModelPermissionAction.Write
            | DataModelPermissionAction.ReferenceWrite | DataModelPermissionAction.CollectionAdd
            | DataModelPermissionAction.CollectionInsert | DataModelPermissionAction.CollectionSet;
        var moderate = view | DataModelPermissionAction.Destroy | DataModelPermissionAction.CollectionRemove
            | DataModelPermissionAction.CollectionClear;

        HostRole = new DataModelPermissionRole("Host", all, all);
        AdminRole = new DataModelPermissionRole("Admin", all, all);
        BuilderRole = new DataModelPermissionRole("Builder", all, build);
        ModeratorRole = new DataModelPermissionRole("Moderator", all, moderate);
        // "User": full control of your OWN objects, view-only on others'. Shown as the normal-member
        // role (the social/event role set is Moderator / User / Spectator).
        GuestRole = new DataModelPermissionRole("User", all, view);
        SpectatorRole = new DataModelPermissionRole("Spectator", view, view);
        AssignableRoles = new[] { AdminRole, BuilderRole, ModeratorRole, GuestRole, SpectatorRole };

        _defaultRoles[DataModelAccessClass.Anonymous] = SpectatorRole;
        _defaultRoles[DataModelAccessClass.Visitor] = GuestRole;
        _defaultRoles[DataModelAccessClass.Contact] = BuilderRole;
        _defaultRoles[DataModelAccessClass.Host] = AdminRole;
    }

    /// <summary>Role a freshly-joined user of the given access class gets (unless overridden).</summary>
    public DataModelPermissionRole GetDefaultRole(DataModelAccessClass accessClass)
        => _defaultRoles.TryGetValue(accessClass, out var role) ? role : GuestRole;

    public void SetDefaultRole(DataModelAccessClass accessClass, DataModelPermissionRole role)
    {
        if (role != null)
            _defaultRoles[accessClass] = role;
    }

    /// <summary>Number of users with an explicit per-user role override.</summary>
    public int UserOverrideCount => _userRoles.Count;

    public void ClearUserOverrides() => _userRoles.Clear();

    public IDisposable EnterActor(User? actor) => new Scope(actor, systemBypass: false);

    public IDisposable EnterSystemBypass() => new Scope(s_currentActor.Value, systemBypass: true);

    public void AddRule(IDataModelPermissionRule rule)
    {
        if (rule == null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        _rules.Add(rule);
    }

    public bool RemoveRule(IDataModelPermissionRule rule) => _rules.Remove(rule);

    public void ClearRules() => _rules.Clear();

    public void SetUserRole(User user, DataModelPermissionRole role)
    {
        if (user == null)
        {
            throw new ArgumentNullException(nameof(user));
        }
        if (role == null)
        {
            throw new ArgumentNullException(nameof(role));
        }

        _userRoles[user.ReferenceID] = role;
    }

    public void ClearUserRole(User user)
    {
        if (user != null)
        {
            _userRoles.Remove(user.ReferenceID);
        }
    }

    public DataModelPermissionRole GetRole(User? user)
    {
        if (user == null)
        {
            return GetDefaultRole(DataModelAccessClass.Anonymous);
        }

        if (IsHostUser(user))
        {
            return HostRole;
        }

        // An explicit per-user override wins; otherwise fall back to the default for their class.
        return _userRoles.TryGetValue(user.ReferenceID, out var role)
            ? role
            : GetDefaultRole(GetAccessClass(user));
    }

    /// <summary>
    /// Classify a user for default-role purposes. Host is detected; Contact/Anonymous require the
    /// social/account layer (not present yet), so everyone else is treated as a Visitor.
    /// </summary>
    public DataModelAccessClass GetAccessClass(User? user)
    {
        if (user != null && IsHostUser(user))
        {
            return DataModelAccessClass.Host;
        }
        return DataModelAccessClass.Visitor;
    }

    public bool Authorize(in DataModelPermissionRequest request, out string? reason)
    {
        reason = null;
        if (!Enabled)
        {
            return true;
        }

        var world = request.World ?? _world;
        if (world.IsDisposed || world.State != World.WorldState.Running)
        {
            return true;
        }

        if (s_systemBypassDepth.Value > 0 || IsSystemMutation(request))
        {
            return true;
        }

        if (request.IsNetwork && !world.IsAuthority)
        {
            return true;
        }

        // Pure reads/enumeration never mutate the data model, so they need neither an actor nor a role -
        // ANYTHING may read, including engine-side render hooks that read synced lists (e.g. a renderer
        // reading its Materials list) on a thread with no actor context. Without this a guest's render
        // hook throws "no actor for datamodel mutation" every frame. Scoped tightly to Read/Enumerate so
        // Replicate/Serialize (network send) and real mutations still go through ownership. -xlinka
        const DataModelPermissionAction readOnlyActions =
            DataModelPermissionAction.Read | DataModelPermissionAction.CollectionEnumerate;
        if ((request.Action & ~readOnlyActions) == 0)
        {
            return true;
        }

        var actor = request.IsNetwork
            ? request.Actor
            : request.Actor ?? s_currentActor.Value ?? world.LocalUser;
        if (actor == null)
        {
            reason = "no actor for datamodel mutation";
            return Deny(request, reason);
        }

        foreach (var rule in _rules)
        {
            var result = rule.Evaluate(request, out var ruleReason);
            if (result == DataModelPermissionResult.Allow)
            {
                return true;
            }
            if (result == DataModelPermissionResult.Deny)
            {
                reason = ruleReason ?? "denied by datamodel permission rule";
                return Deny(request, reason);
            }
        }

        var role = GetRole(actor);
        bool ownsTarget = OwnsTarget(actor, request.Target) ||
                          OwnsTarget(actor, request.Parent);

        // Social/Event floor: the authored world is frozen for EVERYONE incl. the host. Only a user's
        // own runtime objects may be mutated; world content (authority-owned) is foreign to all and
        // denied regardless of role. This is the unbypassable lock - no role escapes it, no live toggle.
        if (SocialLock && !ownsTarget && (request.Action & DataModelPermissionAction.Mutation) != 0)
        {
            reason = "editing is disabled in this world (social)";
            return Deny(request, reason);
        }

        if (role.Allows(request.Action, ownsTarget))
        {
            return true;
        }

        reason = $"role '{role.Name}' cannot perform {request.Action} on {request.Surface}";
        return Deny(request, reason);
    }

    public void Assert(in DataModelPermissionRequest request)
    {
        if (!Authorize(request, out var reason))
        {
            throw new UnauthorizedAccessException(reason ?? "datamodel mutation denied");
        }
    }

    private bool Deny(in DataModelPermissionRequest request, string reason)
    {
        if (LogDeniedMutations)
        {
            var actor = request.IsNetwork
                ? request.Actor
                : request.Actor ?? s_currentActor.Value ?? request.World?.LocalUser;
            var actorName = actor?.UserName?.Value ?? actor?.ReferenceID.ToString() ?? "none";
            var target = request.Target?.ParentHierarchyToString() ?? request.Parent?.ParentHierarchyToString() ?? "(unknown)";

            // Log each DISTINCT denial once, not every frame. A driven/own-body write that keeps getting denied
            // would otherwise spam thousands of identical lines and bury everything else in the console. -xlinka
            var key = $"{actorName}|{request.Action}|{request.Surface}|{target}|{reason}";
            bool firstTime;
            lock (_loggedDenials)
            {
                firstTime = _loggedDenials.Add(key);
                if (_loggedDenials.Count > 512)
                    _loggedDenials.Clear();
            }

            if (firstTime)
                LumoraLogger.Warn($"Datamodel permission denied: actor={actorName}, action={request.Action}, surface={request.Surface}, target={target}, reason={reason}");
        }

        return false;
    }

    private static bool IsSystemMutation(in DataModelPermissionRequest request)
    {
        if (!request.IsNetwork && request.Target is { IsLocalElement: true })
        {
            return true;
        }

        if (request.Member is SyncElement { IsInInitPhase: true } or SyncElement { IsLoading: true })
        {
            return true;
        }

        return request.Target is SyncElement { IsInInitPhase: true } or SyncElement { IsLoading: true };
    }

    private bool IsHostUser(User user)
    {
        return user.IsHost || (_world.IsAuthority && ReferenceEquals(_world.LocalUser, user));
    }

    private static bool OwnsTarget(User actor, IWorldElement? target)
    {
        if (target == null)
        {
            return false;
        }

        if (ReferenceEquals(actor, target))
        {
            return true;
        }

        if (OwnsRefID(actor, target.ReferenceID))
        {
            return true;
        }

        if (target is Slot slot)
        {
            return ReferenceEquals(slot.ActiveUser, actor) || IsUnderUsersRoot(actor, slot);
        }

        if (target is Component component)
        {
            var compSlot = component.Slot;
            return (compSlot != null && ReferenceEquals(compSlot.ActiveUser, actor)) || IsUnderUsersRoot(actor, compSlot);
        }

        if (target is SyncElement syncElement)
        {
            return OwnsTarget(actor, syncElement.Parent);
        }

        if (target is Worker worker)
        {
            return OwnsTarget(actor, worker.Parent);
        }

        return false;
    }

    /// <summary>
    /// A user owns everything parented under their own UserRoot - avatar, body nodes, tools, nameplate. This is
    /// the STRUCTURAL ownership signal, and the reliable one: a user's allocation byte reads as 0 on their own
    /// client until the host-authored AllocationID syncs across, and the cached ActiveUserRoot lags a beat behind
    /// the body being built - both of which otherwise (wrongly) deny a user the right to drive their own
    /// head/hands/nameplate. The User -> UserRoot link (UserRootRef) is replicated and set on both ends. -xlinka
    /// </summary>
    private static bool IsUnderUsersRoot(User actor, IWorldElement? slot)
    {
        var rootSlot = actor?.Root?.Slot;
        if (rootSlot == null || slot is not Slot s)
        {
            return false;
        }

        return ReferenceEquals(s, rootSlot) || s.IsDescendantOf(rootSlot);
    }

    private static bool OwnsRefID(User actor, RefID id)
    {
        if (id.IsNull || id.IsAuthorityID)
        {
            return false;
        }

        byte actorByte = actor.AllocationID.Value;
        if (!RefIDConstants.IsValidUserByte(actorByte))
        {
            actorByte = actor.ReferenceID.GetUserByte();
        }

        return RefIDConstants.IsValidUserByte(actorByte) && id.GetUserByte() == actorByte;
    }
}
