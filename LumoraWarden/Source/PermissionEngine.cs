// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Lumora.Warden;

// The hard permission gate. One per world. It sits below the engine on purpose: the engine takes a
// dependency on this policy and cannot compile without it, so there is no path that "forgets" to
// consult the gate. Every decision here is host-authoritative - it reads the world's own authority
// flag and host-assigned allocation bytes, never a value a client sent. -xlinka
public sealed class PermissionEngine
{
    private sealed class Scope : IDisposable
    {
        private readonly IPermissionActor? _previousActor;
        private readonly int _previousBypassDepth;
        private bool _disposed;

        public Scope(IPermissionActor? actor, bool systemBypass)
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

    private static readonly ThreadLocal<IPermissionActor?> s_currentActor = new();
    private static readonly ThreadLocal<int> s_systemBypassDepth = new();

    private readonly IPermissionWorldFacts _world;
    private readonly IPermissionIdSpace _ids;
    private readonly List<IPermissionRule> _rules = new();
    private readonly Dictionary<ulong, PermissionRole> _userRoles = new();

    // Distinct denials already logged, so a per-frame denial doesn't spam thousands of identical lines. -xlinka
    private readonly HashSet<string> _loggedDenials = new();

    // Where denial and refusal messages go. The engine carries no logger of its own.
    public Action<string>? Warn { get; set; }

    // Owner of the world - full power, not assignable (you can't demote the host).
    public PermissionRole HostRole { get; }
    // Assignable roles, in descending power. Each has preset capabilities over OTHER users' objects
    // (own objects are always fully editable). The host assigns these per access-class or per-user.
    public PermissionRole AdminRole { get; }
    public PermissionRole BuilderRole { get; }
    public PermissionRole ModeratorRole { get; }
    public PermissionRole GuestRole { get; }
    public PermissionRole SpectatorRole { get; }
    public IReadOnlyList<PermissionRole> AssignableRoles { get; }

    private readonly Dictionary<PermissionAccessClass, PermissionRole> _defaultRoles = new();

    private bool _enabled = true;

    // Master switch for permission enforcement. Disabling it makes every Authorize() pass, so it is
    // HOST-AUTHORITATIVE and FAIL-CLOSED: only the authority may turn enforcement off, and only while
    // the world context is known. A guest (or any non-authority code path) that tries to clear it is
    // ignored - enforcement can never be silently killed from a non-host path. Re-enabling is always
    // allowed (it only tightens). -xlinka
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
                Warn?.Invoke("Refused to disable datamodel permission enforcement: not host-authoritative.");
                return;
            }

            _enabled = false;
        }
    }

    public bool LogDeniedMutations { get; set; } = true;

    // Social/Event lockdown. When set, the authored world is frozen for EVERYONE (including the host
    // and admins): only a user's OWN runtime objects (avatar, spawned items) may be mutated. World
    // content is created under the authority id, so no user "owns" it and all editing of it is
    // denied - there's no role that escapes this and no live toggle. Set from the world mode by
    // ApplyMode.
    public bool SocialLock { get; set; }

    public PermissionEngine(IPermissionWorldFacts world, IPermissionIdSpace ids)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));

        var all = PermissionAction.All;
        var view = PermissionAction.Read | PermissionAction.CollectionEnumerate;
        var build = view | PermissionAction.Create | PermissionAction.Write
            | PermissionAction.ReferenceWrite | PermissionAction.CollectionAdd
            | PermissionAction.CollectionInsert | PermissionAction.CollectionSet;
        var moderate = view | PermissionAction.Destroy | PermissionAction.CollectionRemove
            | PermissionAction.CollectionClear;

        HostRole = new PermissionRole("Host", all, all);
        AdminRole = new PermissionRole("Admin", all, all);
        BuilderRole = new PermissionRole("Builder", all, build);
        ModeratorRole = new PermissionRole("Moderator", all, moderate);
        // "User": full control of your OWN objects, view-only on others'. Shown as the normal-member
        // role (the social/event role set is Moderator / User / Spectator).
        GuestRole = new PermissionRole("User", all, view);
        SpectatorRole = new PermissionRole("Spectator", view, view);
        AssignableRoles = new[] { AdminRole, BuilderRole, ModeratorRole, GuestRole, SpectatorRole };

        _defaultRoles[PermissionAccessClass.Anonymous] = SpectatorRole;
        _defaultRoles[PermissionAccessClass.Visitor] = GuestRole;
        _defaultRoles[PermissionAccessClass.Contact] = BuilderRole;
        _defaultRoles[PermissionAccessClass.Host] = AdminRole;
    }

    // Role a freshly-joined user of the given access class gets (unless overridden).
    public PermissionRole GetDefaultRole(PermissionAccessClass accessClass)
        => _defaultRoles.TryGetValue(accessClass, out var role) ? role : GuestRole;

    public void SetDefaultRole(PermissionAccessClass accessClass, PermissionRole role)
    {
        if (role != null)
            _defaultRoles[accessClass] = role;
    }

    // Number of users with an explicit per-user role override.
    public int UserOverrideCount => _userRoles.Count;

    public void ClearUserOverrides() => _userRoles.Clear();

    public IDisposable EnterActor(IPermissionActor? actor) => new Scope(actor, systemBypass: false);

    public IDisposable EnterSystemBypass() => new Scope(s_currentActor.Value, systemBypass: true);

    public void AddRule(IPermissionRule rule)
    {
        if (rule == null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        _rules.Add(rule);
    }

    public bool RemoveRule(IPermissionRule rule) => _rules.Remove(rule);

    public void ClearRules() => _rules.Clear();

    public void SetUserRole(IPermissionActor user, PermissionRole role)
    {
        if (user == null)
        {
            throw new ArgumentNullException(nameof(user));
        }
        if (role == null)
        {
            throw new ArgumentNullException(nameof(role));
        }

        _userRoles[user.Id] = role;
    }

    public void ClearUserRole(IPermissionActor user)
    {
        if (user != null)
        {
            _userRoles.Remove(user.Id);
        }
    }

    public PermissionRole GetRole(IPermissionActor? user)
    {
        if (user == null)
        {
            return GetDefaultRole(PermissionAccessClass.Anonymous);
        }

        if (IsHostUser(user))
        {
            return HostRole;
        }

        // An explicit per-user override wins; otherwise fall back to the default for their class.
        return _userRoles.TryGetValue(user.Id, out var role)
            ? role
            : GetDefaultRole(GetAccessClass(user));
    }

    // Classify a user for default-role purposes. Host is detected; Contact/Anonymous require the
    // social/account layer (not present yet), so everyone else is treated as a Visitor.
    public PermissionAccessClass GetAccessClass(IPermissionActor? user)
    {
        if (user != null && IsHostUser(user))
        {
            return PermissionAccessClass.Host;
        }
        return PermissionAccessClass.Visitor;
    }

    // Roles a host may assign to users in the given mode (the per-mode role set).
    public IReadOnlyList<PermissionRole> AssignableRolesFor(WorldMode mode)
    {
        // Social + Event are view/interact spaces: only moderation, normal user, and spectator make
        // sense - no Builder/Admin (there is nothing to build).
        if (mode == WorldMode.Social || mode == WorldMode.Event)
            return new[] { ModeratorRole, GuestRole, SpectatorRole };

        return AssignableRoles;
    }

    // Load a mode's preset: the lock floor plus the default role per access class. Baked at host time
    // from the world mode. The floor value comes from WorldModePolicy so it is defined in exactly one
    // place.
    public void ApplyMode(WorldMode mode)
    {
        SocialLock = WorldModePolicy.SocialLockFloor(mode);

        switch (mode)
        {
            case WorldMode.Builder:
                SetDefaultRole(PermissionAccessClass.Anonymous, SpectatorRole);
                SetDefaultRole(PermissionAccessClass.Visitor, BuilderRole);
                SetDefaultRole(PermissionAccessClass.Contact, BuilderRole);
                break;

            case WorldMode.Social:
                // Frozen world; users may still bring/handle their own items (Guest = "User": own
                // objects fully editable, the world view-only). The SocialLock floor denies any edit
                // of the authored world for everyone, host included.
                SetDefaultRole(PermissionAccessClass.Anonymous, SpectatorRole);
                SetDefaultRole(PermissionAccessClass.Visitor, GuestRole);
                SetDefaultRole(PermissionAccessClass.Contact, GuestRole);
                break;

            case WorldMode.Event:
                // Strictest: view + interact only, no spawning even of your own items.
                SetDefaultRole(PermissionAccessClass.Anonymous, SpectatorRole);
                SetDefaultRole(PermissionAccessClass.Visitor, SpectatorRole);
                SetDefaultRole(PermissionAccessClass.Contact, SpectatorRole);
                break;
        }
    }

    public bool Authorize(in PermissionRequest request, out string? reason)
    {
        reason = null;
        if (!Enabled)
        {
            return true;
        }

        var world = request.World ?? _world;
        if (world.IsDisposed || !world.IsRunning)
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
        const PermissionAction readOnlyActions =
            PermissionAction.Read | PermissionAction.CollectionEnumerate;
        if ((request.Action & ~readOnlyActions) == 0)
        {
            return true;
        }

        var actor = request.IsNetwork
            ? request.Actor
            : request.Actor ?? s_currentActor.Value ?? world.LocalActor;
        if (actor == null)
        {
            reason = "no actor for datamodel mutation";
            return Deny(request, reason);
        }

        foreach (var rule in _rules)
        {
            var result = rule.Evaluate(request, out var ruleReason);
            if (result == PermissionResult.Allow)
            {
                return true;
            }
            if (result == PermissionResult.Deny)
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
        // slot's KEY is its id, minted in the creator's own byte, so an add keyed in the actor's byte INTO
        // THE SLOT REGISTRY is an own-object create and must authorize (locally so the build doesn't throw, and
        // on the host so it accepts the guest's replicated add).
        // SCOPED TO THE REGISTRY ON PURPOSE: a per-slot collection (e.g. a slot's component list) has
        // Parent = its owning slot, so OwnsTarget(Parent) above already answers correctly - attaching a
        // component to a HOST slot is denied, and to the guest's OWN slot is allowed (the slot is in the guest's
        // byte). We must NOT relax those here, or a guest could bolt live components onto host geometry. The
        // registry is the only flat (null-parent) collection a guest legitimately writes; user streams go via
        // Parent=own-user and users are host-managed. -xlinka
        if (!ownsTarget &&
            (request.Action & (PermissionAction.CollectionAdd | PermissionAction.CollectionInsert)) != 0 &&
            _ids.TryGetElementId(request.Key, out var keyId) &&
            OwnsId(actor, keyId) &&
            ReferenceEquals(request.Target, world.SlotRegistry))
        {
            ownsTarget = true;
        }

        // Social/Event floor: the authored world is frozen for EVERYONE incl. the host. Only a user's
        // own runtime objects may be mutated; world content (authority-owned) is foreign to all and
        // denied regardless of role. This is the unbypassable lock - no role escapes it, no live toggle.
        if (SocialLock && !ownsTarget && (request.Action & PermissionAction.Mutation) != 0)
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
        // grab parents the object under the grabber and flips the structural owner, which OwnsTarget reads as
        // ownership - that would let a client destroy host content on the next batch.
        // Destructive ops therefore require REAL per-byte ownership, not the structural signal. This only ever
        // removes authority that came from the grab-flip: if the actor really owns it (own-byte equipment)
        // strong ownership holds and this is a no-op; if they don't own it at all the role check below already
        // denies. The grab interaction (move / reparent / pose / grab-state) was allowed above and never
        // reaches here. Holding conveys no destroy authority. -xlinka
        if ((request.Action & DestructiveActions) != 0 && ownsTarget)
        {
            bool ownsTargetStrong = OwnsIdStrong(actor, request.Target) ||
                                    OwnsIdStrong(actor, request.Parent);
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

    public void Assert(in PermissionRequest request)
    {
        if (!Authorize(request, out var reason))
        {
            throw new UnauthorizedAccessException(reason ?? "datamodel mutation denied");
        }
    }

    private bool Deny(in PermissionRequest request, string reason)
    {
        if (LogDeniedMutations)
        {
            var actor = request.IsNetwork
                ? request.Actor
                : request.Actor ?? s_currentActor.Value ?? request.World?.LocalActor;
            var actorName = actor?.DisplayName ?? "none";
            var target = request.Target?.HierarchyPath ?? request.Parent?.HierarchyPath ?? "(unknown)";

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
                Warn?.Invoke($"Datamodel permission denied: actor={actorName}, action={request.Action}, surface={request.Surface}, target={target}, reason={reason}");
        }

        return false;
    }

    private static bool IsSystemMutation(in PermissionRequest request)
    {
        if (!request.IsNetwork && request.Target is { IsLocalElement: true })
        {
            return true;
        }

        if (request.Member is { PermissionKind: PermissionTargetKind.SyncElement, IsInitializing: true })
        {
            return true;
        }

        return request.Target is { PermissionKind: PermissionTargetKind.SyncElement, IsInitializing: true };
    }

    private bool IsHostUser(IPermissionActor user)
    {
        return user.IsHost || (_world.IsAuthority && ReferenceEquals(_world.LocalActor, user));
    }

    // True when the request is a permitted GRAB interaction rather than an ownership edit: setting/clearing a
    // grabbable's grab-state, or reparenting/posing an object the actor is currently holding. Lets a user
    // grab a shared object it doesn't own; the host still arbitrates the authoritative holder. -xlinka
    private static bool IsGrabInteraction(in PermissionRequest request, IPermissionActor? actor)
    {
        var member = request.Member;
        if (member == null || actor == null)
            return false;

        if (request.Parent is not IPermissionGrabSurface surface)
            return false;

        switch (surface.ClassifyGrabWrite(member))
        {
            // Grab / release bookkeeping other than the holder itself (where to put the object back).
            case GrabWriteKind.GrabState:
                return surface.AllowsGrab;

            case GrabWriteKind.HolderRef:
                if (!surface.AllowsGrab)
                    return false;
                if (surface.AllowsSteal)
                    return true;

                // No-steal enforcement, host-side. If the object is currently held by SOMEONE ELSE and stealing is
                // off, refuse a holder write from a different user - that's a force-steal the host doesn't allow
                // (a client-side steal check could be skipped). Releases (the current holder clearing the ref) are
                // not steals and stay allowed. The host runs Validate per delta BEFORE decoding the batch, so the
                // reported holder is still the PRE-batch holder here - exactly the holder we compare against.
                // Returning false lands on the role check below, which denies. -xlinka
                var currentHoldingUser = surface.CurrentHolder;
                return currentHoldingUser == null || ReferenceEquals(currentHoldingUser, actor);

            // Reparent / pose of a grabbable object: the write targets a slot's parent or local transform, and the
            // slot carries a grabbable that allows grabbing. We do NOT require the actor to already be the recorded
            // holder - the host runs Validate for every delta record BEFORE it decodes any, so the in-batch holder
            // write isn't applied yet when the reparent is validated; a holder check would reject every real grab.
            // Bounding it to AllowGrab grabbables is the safe line: the host still owns the object and arbitrates
            // the authoritative holder, so this is transient interaction, moderated like any grab, not an edit of
            // host content. -xlinka
            case GrabWriteKind.Transform:
                return surface.AllowsGrab;

            default:
                return false;
        }
    }

    // Actions that hand a user real control over an object's existence - destroying it, pulling it out of a
    // collection, or clearing a collection. For a HELD foreign object these must require REAL ownership (the
    // creator's allocation byte), not the structural "it's parked under my hand right now" signal a grab flips.
    // Move / reparent / grab-state writes are NOT here - those are the grab interaction, allowed earlier.
    // Holding an object confers no edit/destroy authority; that comes only from real per-byte ownership. -xlinka
    private const PermissionAction DestructiveActions =
        PermissionAction.Destroy |
        PermissionAction.CollectionRemove |
        PermissionAction.CollectionClear;

    // STRONG ownership: only the real per-byte signal (and self), never the grab-flippable structural one. A
    // grab reparents the object under the grabber's root, which flips the structural owner to the grabber - so
    // that CANNOT gate destroying a foreign object, or a client could destroy a host prop just by holding it.
    // The id is minted in the creator's byte and a grab never changes it, so a user's own per-peer-spawned
    // equipment still passes here. -xlinka
    private bool OwnsIdStrong(IPermissionActor actor, IPermissionTarget? target)
    {
        if (target == null)
            return false;
        if (ReferenceEquals(actor, target))
            return true;
        if (OwnsId(actor, target.Id))
            return true;

        // Climb the same logical ownership chain OwnsTarget uses, but ONLY via the byte signal - never the
        // structural owner. A component's real owner is its slot's byte; a sync element's or worker's is its
        // parent's.
        return target.PermissionKind switch
        {
            PermissionTargetKind.Component or PermissionTargetKind.SyncElement or PermissionTargetKind.Worker
                => OwnsIdStrong(actor, target.OwnershipParent),
            _ => false
        };
    }

    private bool OwnsTarget(IPermissionActor actor, IPermissionTarget? target)
    {
        if (target == null)
        {
            return false;
        }

        if (ReferenceEquals(actor, target))
        {
            return true;
        }

        if (OwnsId(actor, target.Id))
        {
            return true;
        }

        // Slots and components fall back to the STRUCTURAL signals, and those are the reliable ones: a user's
        // allocation byte reads as 0 on their own client until the host-authored value syncs across, and the
        // cached root lags a beat behind the body being built - both of which otherwise (wrongly) deny a user
        // the right to drive their own head/hands/nameplate. The user -> root link is replicated and set on
        // both ends. -xlinka
        if (target.PermissionKind is PermissionTargetKind.Slot or PermissionTargetKind.Component)
        {
            return ReferenceEquals(target.StructuralOwner, actor) || target.IsUnderActorRoot(actor);
        }

        if (target.PermissionKind is PermissionTargetKind.SyncElement or PermissionTargetKind.Worker)
        {
            return OwnsTarget(actor, target.OwnershipParent);
        }

        return false;
    }

    private bool OwnsId(IPermissionActor actor, ulong id)
    {
        if (_ids.IsNull(id) || _ids.IsAuthority(id))
        {
            return false;
        }

        byte actorByte = actor.AllocationByte;
        if (!_ids.IsValidOwnerByte(actorByte))
        {
            actorByte = _ids.OwnerByte(actor.Id);
        }

        return _ids.IsValidOwnerByte(actorByte) && _ids.OwnerByte(id) == actorByte;
    }
}
