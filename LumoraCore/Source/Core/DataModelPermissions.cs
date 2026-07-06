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
/// How a user relates to the world when they join - used to pick a default role.
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

    // Owner of the world - full power, not assignable (you can't demote the host).
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

    private bool _enabled = true;

    /// <summary>
    /// Master switch for permission enforcement. Disabling it makes every Authorize() pass, so it is
    /// HOST-AUTHORITATIVE and FAIL-CLOSED: only the authority may turn enforcement off, and only while
    /// the world context is known. A guest (or any non-authority code path) that tries to clear it is
    /// ignored - enforcement can never be silently killed from a non-host path. Re-enabling is always
    /// allowed (it only tightens). -xlinka
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            // Re-enabling always allowed - it can only tighten enforcement.
            if (value)
            {
                _enabled = true;
                return;
            }

            // Disabling requires a known, authoritative context. Fail closed otherwise.
            if (_world == null || _world.IsDisposed || !_world.IsAuthority)
            {
                LumoraLogger.Warn("Refused to disable datamodel permission enforcement: not host-authoritative.");
                return;
            }

            _enabled = false;
        }
    }

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

        // Own-byte slot-REGISTRY adds: the flat world slot registry lives in the authority byte with a null
        // parent, so neither Target (the registry) nor Parent reads as owned - that wrongly denies a guest
        // REGISTERING its own slot (the per-peer hand tool / laser rig, or spawning its own content). The new
        // slot's KEY is its RefID, minted in the creator's own byte, so an add keyed in the actor's byte INTO
        // THE SLOT REGISTRY is an own-object create and must authorize (locally so the build doesn't throw, and
        // on the host so it accepts the guest's replicated add).
        // SCOPED TO THE REGISTRY ON PURPOSE: a per-slot collection (e.g. a slot's component list) has
        // Parent = its owning slot, so OwnsTarget(Parent) above already answers correctly - attaching a
        // component to a HOST slot is denied, and to the guest's OWN slot is allowed (the slot is in the guest's
        // byte). We must NOT relax those here, or a guest could bolt live components onto host geometry. The
        // registry is the only flat (null-parent) collection a guest legitimately writes; user streams go via
        // Parent=own-user and users are host-managed. -xlinka
        if (!ownsTarget &&
            (request.Action & (DataModelPermissionAction.CollectionAdd | DataModelPermissionAction.CollectionInsert)) != 0 &&
            request.Key is RefID keyId &&
            OwnsRefID(actor, keyId) &&
            ReferenceEquals(request.Target, world.SlotRegistryElement))
        {
            ownsTarget = true;
        }

        // Social/Event floor: the authored world is frozen for EVERYONE incl. the host. Only a user's
        // own runtime objects may be mutated; world content (authority-owned) is foreign to all and
        // denied regardless of role. This is the unbypassable lock - no role escapes it, no live toggle.
        if (SocialLock && !ownsTarget && (request.Action & DataModelPermissionAction.Mutation) != 0)
        {
            reason = "editing is disabled in this world (social)";
            return Deny(request, reason);
        }

        // Grab interactions: a grabbable object opts into being picked up + moved by ANY user - that's an
        // interaction, not an ownership edit. Allow the grab-state refs (who holds it / its restore-parent)
        // and the reparent+pose of an object the actor is CURRENTLY HOLDING. The host still owns the object
        // and arbitrates the authoritative holder (conflicting grabs resolve there), and SocialLock above
        // already froze this in event worlds. Ordinary edits to the object stay owner-gated by the role
        // check below. -xlinka
        if (IsGrabInteraction(in request, actor))
        {
            return true;
        }

        // Destroying / removing / clearing an object you only "own" because you are holding it is forbidden. A
        // grab parents the object under the grabber and flips ActiveUser / IsUnderUsersRoot, which OwnsTarget
        // reads as ownership - that would let a client destroy host content on the next batch.
        // Destructive ops therefore require REAL per-byte ownership, not the structural signal. This only ever
        // removes authority that came from the grab-flip: if the actor really owns it (own-byte equipment)
        // strong ownership holds and this is a no-op; if they don't own it at all the role check below already
        // denies. The grab interaction (move / reparent / pose / grab-state) was allowed above and never
        // reaches here. Holding conveys no destroy authority. -xlinka
        if ((request.Action & DestructiveActions) != 0 && ownsTarget)
        {
            bool ownsTargetStrong = OwnsRefIDStrong(actor, request.Target) ||
                                    OwnsRefIDStrong(actor, request.Parent);
            if (!ownsTargetStrong)
            {
                reason = "destroying a held object requires ownership, not just holding it";
                return Deny(request, reason);
            }
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

    // True when the request is a permitted GRAB interaction rather than an ownership edit: setting/clearing a
    // grabbable's grab-state, or reparenting/posing an object the actor is currently holding. Lets a user
    // grab a shared object it doesn't own; the host still arbitrates the authoritative holder. -xlinka
    private static bool IsGrabInteraction(in DataModelPermissionRequest request, User? actor)
    {
        var member = request.Member;
        if (member == null || actor == null)
            return false;

        // Grab / release: only the grab-state refs of a grabbable that allows grabbing.
        if (request.Parent is Components.Grabbable grabbable)
        {
            if (!grabbable.AllowGrab.Value)
                return false;
            if (!ReferenceEquals(member, grabbable.GrabberRef) && !ReferenceEquals(member, grabbable.LastParentRef))
                return false;

            // No-steal enforcement, host-side. If the object is currently held by SOMEONE ELSE and stealing is
            // off, refuse a holder-ref write from a different user - that's a force-steal the host doesn't allow
            // (a client-side steal check could be skipped). Releases (the current holder clearing the ref) are
            // not steals and stay allowed. The host runs Validate per delta BEFORE decoding the batch, so
            // GrabberRef.Target is still the PRE-batch holder here - exactly the holder we compare against. -xlinka
            if (ReferenceEquals(member, grabbable.GrabberRef) && !grabbable.AllowSteal.Value)
            {
                var currentHoldingUser = grabbable.GrabberRef.Target?.OwningUser;
                if (currentHoldingUser != null && !ReferenceEquals(currentHoldingUser, actor))
                    return false; // held by another user, not stealable -> fall through to the role check, which denies
            }
            return true;
        }

        // Reparent / pose of a grabbable object: the write targets a slot's parent or local transform, and the
        // slot carries a grabbable that allows grabbing. We do NOT require the actor to already be the recorded
        // holder - the host runs Validate for every delta record BEFORE it decodes any, so the in-batch
        // GrabberRef write isn't applied yet when the reparent is validated; a holder check would reject every
        // real grab. Bounding it to AllowGrab grabbables is the safe line: the host still owns the object and
        // arbitrates the authoritative holder, so this is transient interaction, moderated like any grab, not an
        // edit of host content. -xlinka
        if (request.Parent is Slot slot)
        {
            bool transformOrParent =
                ReferenceEquals(member, slot.ParentSlotRef) ||
                ReferenceEquals(member, slot.LocalPosition) ||
                ReferenceEquals(member, slot.LocalRotation) ||
                ReferenceEquals(member, slot.LocalScale);
            if (!transformOrParent)
                return false;

            var g = slot.GetComponent<Components.Grabbable>();
            return g != null && g.AllowGrab.Value;
        }

        return false;
    }

    // Actions that hand a user real control over an object's existence - destroying it, pulling it out of a
    // collection, or clearing a collection. For a HELD foreign object these must require REAL ownership (the
    // creator's allocation byte), not the structural "it's parked under my hand right now" signal a grab flips.
    // Move / reparent / grab-state writes are NOT here - those are the grab interaction, allowed earlier.
    // Holding an object confers no edit/destroy authority; that comes only from real per-byte ownership. -xlinka
    private const DataModelPermissionAction DestructiveActions =
        DataModelPermissionAction.Destroy |
        DataModelPermissionAction.CollectionRemove |
        DataModelPermissionAction.CollectionClear;

    // STRONG ownership: only the real per-byte signal (and self), never the grab-flippable structural one. A
    // grab reparents the object under the grabber's UserRoot, which flips ActiveUser / IsUnderUsersRoot to the
    // grabber - so those CANNOT gate destroying a foreign object, or a client could destroy a host prop
    // just by holding it. OwnsRefID is minted in the creator's byte and a grab never changes it, so a user's own
    // per-peer-spawned equipment still passes here. -xlinka
    private static bool OwnsRefIDStrong(User actor, IWorldElement? target)
    {
        if (target == null)
            return false;
        if (ReferenceEquals(actor, target))
            return true;
        if (OwnsRefID(actor, target.ReferenceID))
            return true;

        // Climb the same logical ownership chain OwnsTarget uses, but ONLY via the byte signal - never
        // ActiveUser / IsUnderUsersRoot. A component's real owner is its slot's byte; a sync element's or
        // worker's is its parent's.
        return target switch
        {
            Component component => OwnsRefIDStrong(actor, component.Slot),
            SyncElement syncElement => OwnsRefIDStrong(actor, syncElement.Parent),
            Worker worker => OwnsRefIDStrong(actor, worker.Parent),
            _ => false
        };
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
