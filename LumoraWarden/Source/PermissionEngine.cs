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

    private sealed class AuthoredContentScope : IDisposable
    {
        private readonly int _previousDepth;
        private readonly bool _armed;
        private bool _disposed;

        public AuthoredContentScope(bool armed)
        {
            _armed = armed;
            // An unarmed scope must not so much as READ the counter: the inert singleton is constructed
            // during this class's static init, before the ThreadLocal field it would touch exists. -xlinka
            _previousDepth = armed ? s_authoredContentDepth.Value : 0;
            if (armed)
                s_authoredContentDepth.Value = _previousDepth + 1;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            if (_armed)
                s_authoredContentDepth.Value = _previousDepth;
            _disposed = true;
        }
    }

    private static readonly AuthoredContentScope s_inertScope = new(armed: false);

    // The compiled capability caps for one role, plus whatever the host's config resolved on top. Built
    // once per policy change and read by reference afterwards, so the per-domain question costs a
    // dictionary probe and a field read.
    private readonly struct DomainCaps
    {
        public readonly bool Spawn;
        public readonly bool SaveCopy;
        public readonly bool Export;
        public readonly bool ToolUse;
        public readonly bool Touch;
        public readonly float MinScale;
        public readonly float MaxScale;

        public DomainCaps(bool spawn, bool saveCopy, bool export, bool toolUse, bool touch, float minScale = 0f, float maxScale = 0f)
        {
            Spawn = spawn;
            SaveCopy = saveCopy;
            Export = export;
            ToolUse = toolUse;
            Touch = touch;
            MinScale = minScale;
            MaxScale = maxScale;
        }

        public bool Allows(PermissionDomain domain) => domain switch
        {
            PermissionDomain.Spawn => Spawn,
            PermissionDomain.SaveCopy => SaveCopy,
            PermissionDomain.Export => Export,
            PermissionDomain.ToolUse => ToolUse,
            PermissionDomain.Touch => Touch,
            _ => false
        };

        public DomainCaps With(PermissionRoleCap cap) => new(
            Resolve(Spawn, cap.Spawn),
            Resolve(SaveCopy, cap.SaveCopy),
            Resolve(Export, cap.Export),
            Resolve(ToolUse, cap.ToolUse),
            Resolve(Touch, cap.Touch),
            cap.MinScale > 0f ? cap.MinScale : MinScale,
            cap.MaxScale > 0f ? cap.MaxScale : MaxScale);

        private static bool Resolve(bool compiled, PermissionToggle toggle) => toggle switch
        {
            PermissionToggle.Grant => true,
            PermissionToggle.Deny => false,
            _ => compiled
        };
    }

    private static readonly ThreadLocal<IPermissionActor?> s_currentActor = new();
    private static readonly ThreadLocal<int> s_systemBypassDepth = new();
    private static readonly ThreadLocal<int> s_authoredContentDepth = new();

    private readonly IPermissionWorldFacts _world;
    private readonly IPermissionIdSpace _ids;
    private readonly List<IPermissionRule> _rules = new();
    private readonly Dictionary<ulong, PermissionRole> _userRoles = new();

    // Distinct denials already logged, so a per-frame denial doesn't spam thousands of identical lines. -xlinka
    private readonly HashSet<string> _loggedDenials = new();

    // THE HOST'S CONFIG, RESOLVED
    // Nothing below is read from the data model at request time. ApplyPolicy takes a whole snapshot and
    // rebuilds these; between snapshots every lookup is a dictionary probe on state the engine owns. That
    // is what keeps a config that lives in a replicated component off the hot path entirely. -xlinka
    private readonly Dictionary<PermissionRole, DomainCaps> _compiledCaps = new();
    private readonly Dictionary<PermissionRole, DomainCaps> _resolvedCaps = new();
    private readonly Dictionary<string, PermissionRole> _rolesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PermissionRole> _assignedByMachine = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PermissionRole> _assignedByAccount = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PermissionToggle[]> _toggles = new(StringComparer.OrdinalIgnoreCase);
    private PermissionRole? _defaultJoinerRole;
    private WorldMode _mode = WorldMode.Builder;

    // Resolved assignment per live actor, so the common path does not hash identity strings on every
    // gated write. Dropped wholesale whenever the policy changes; never filled for an actor whose
    // identity the host has not authored yet, because that answer would be wrong the moment it arrives.
    private readonly Dictionary<ulong, PermissionRole?> _assignmentCache = new();

    // THE GROUP ROSTER, AS THE HOST FETCHED IT
    // Account id -> the group service's own rung name, plus the group's ban list. Only the authority
    // ever fills these (the roster component does the fetching) and nothing that arrives over the wire
    // can reach them, so a client claiming to be a group moderator claims it to nobody. Empty until the
    // first fetch lands, which is the fail-closed answer: no members-only door opens and nobody is a
    // group moderator while we have not heard back. -xlinka
    private readonly Dictionary<string, string> _groupRoster = new(StringComparer.Ordinal);
    private readonly HashSet<string> _groupBans = new(StringComparer.Ordinal);

    private readonly PermissionLedger _ledger = new();

    // Where denial and refusal messages go. The engine carries no logger of its own.
    public Action<string>? Warn { get; set; }

    // Where a peer that keeps tripping the gate gets dealt with. Warden knows nothing about connections;
    // the adapter wires this to the session.
    public IPermissionEnforcement? Enforcement { get; set; }

    // Seconds, monotonic. Only the denial ledger reads it. Settable so a test can drive decay without
    // sleeping.
    public Func<double>? Clock { get; set; }

    public PermissionLedger Ledger => _ledger;

    // The mode the world was hosted in. Set by ApplyMode; the per-domain ceilings read it.
    public WorldMode Mode => _mode;

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

        // Spectator is view-only on OTHER people's things, not on its own. It used to be view/view, which
        // read as "cannot write anything at all, including the objects it minted itself" - and since Event
        // worlds default every visitor to Spectator, that meant nobody in an event could stand up their own
        // per-peer rig: no hand tool, no laser, no touch relay, so no interacting with the authored world
        // that an event exists to show you. What makes a spectator a spectator is the FOREIGN mask (they
        // cannot touch the world) and the Spawn domain ceiling (an event forbids bringing items in, and
        // GrabSpawnerBase reads that ceiling directly), not being unable to author their own body. -xlinka
        SpectatorRole = new PermissionRole("Spectator", all, view);
        AssignableRoles = new[] { AdminRole, BuilderRole, ModeratorRole, GuestRole, SpectatorRole };

        _defaultRoles[PermissionAccessClass.Anonymous] = SpectatorRole;
        _defaultRoles[PermissionAccessClass.Visitor] = GuestRole;
        _defaultRoles[PermissionAccessClass.Contact] = BuilderRole;
        _defaultRoles[PermissionAccessClass.Host] = AdminRole;
        _defaultRoles[PermissionAccessClass.Group] = BuilderRole;

        foreach (var role in new[] { HostRole, AdminRole, BuilderRole, ModeratorRole, GuestRole, SpectatorRole })
            _rolesByName[role.Name] = role;

        // COMPILED DOMAIN CAPS. These are the floor a missing or nonsense config falls back to, so they
        // are written tight: a role gets a domain here only if it would be strange for it NOT to have it.
        // Export is the one that stays off by default even for people who can build, because taking
        // content off the machine is a different ask from editing it in place. -xlinka
        _compiledCaps[HostRole] = new DomainCaps(true, true, true, true, true);
        _compiledCaps[AdminRole] = new DomainCaps(true, true, true, true, true);
        _compiledCaps[BuilderRole] = new DomainCaps(true, true, true, true, true);
        _compiledCaps[ModeratorRole] = new DomainCaps(true, true, false, true, true);
        _compiledCaps[GuestRole] = new DomainCaps(true, true, false, false, true);
        _compiledCaps[SpectatorRole] = new DomainCaps(false, false, false, false, true);

        RebuildResolvedCaps();
    }

    // CONFIGURATION
    // One snapshot in, everything rebuilt. Called by the adapter whenever the host's config component
    // changes; there is no path that reads the data model from inside a decision. A null policy is the
    // same as an empty one: back to the compiled defaults, never wider. -xlinka
    public void ApplyPolicy(PermissionPolicy? policy)
    {
        _assignedByMachine.Clear();
        _assignedByAccount.Clear();
        _toggles.Clear();
        _assignmentCache.Clear();
        _defaultJoinerRole = null;

        if (policy != null)
        {
            foreach (var assignment in policy.Assignments)
            {
                if (assignment == null || !TryResolveAssignableRole(assignment.RoleName, out var role))
                    continue;

                if (!string.IsNullOrEmpty(assignment.AccountKey))
                    _assignedByAccount[assignment.AccountKey!] = role;
                if (!string.IsNullOrEmpty(assignment.MachineKey))
                    _assignedByMachine[assignment.MachineKey!] = role;
            }

            foreach (var cap in policy.Caps)
            {
                if (cap == null || string.IsNullOrEmpty(cap.RoleName) || !_rolesByName.ContainsKey(cap.RoleName!))
                    continue;
                _toggles[cap.RoleName!] = new[] { cap.Spawn, cap.SaveCopy, cap.Export, cap.ToolUse, cap.Touch };
            }

            // A default that names an unknown or non-assignable role is discarded rather than guessed at:
            // the per-access-class default that the mode baked in is the safe answer.
            if (TryResolveAssignableRole(policy.DefaultJoinerRole, out var joinerRole))
                _defaultJoinerRole = joinerRole;

            _ledger.Settings = policy.Escalation ?? new PermissionEscalationSettings();
        }
        else
        {
            _ledger.Settings = new PermissionEscalationSettings();
        }

        RebuildResolvedCaps(policy);
    }

    private bool TryResolveAssignableRole(string? name, out PermissionRole role)
    {
        role = GuestRole;
        if (string.IsNullOrWhiteSpace(name) || !_rolesByName.TryGetValue(name!, out var found))
            return false;

        // Host is not assignable. You cannot hand someone the world.
        if (ReferenceEquals(found, HostRole))
            return false;

        role = found;
        return true;
    }

    private void RebuildResolvedCaps(PermissionPolicy? policy = null)
    {
        _resolvedCaps.Clear();
        foreach (var pair in _compiledCaps)
            _resolvedCaps[pair.Key] = pair.Value;

        if (policy == null)
            return;

        foreach (var cap in policy.Caps)
        {
            if (cap == null || string.IsNullOrEmpty(cap.RoleName)
                || !_rolesByName.TryGetValue(cap.RoleName!, out var role)
                || !_resolvedCaps.TryGetValue(role, out var current))
                continue;

            _resolvedCaps[role] = current.With(cap);
        }
    }

    private DomainCaps CapsFor(PermissionRole role)
        => _resolvedCaps.TryGetValue(role, out var caps) ? caps : default;

    private PermissionToggle ToggleFor(PermissionRole role, PermissionDomain domain)
        => _toggles.TryGetValue(role.Name, out var set) && (int)domain < set.Length
            ? set[(int)domain]
            : PermissionToggle.Inherit;

    // The mode whose ceilings apply. SocialLock is settable on its own (the world-mode preset is not the
    // only thing that can freeze a world), so a hand-set lock still pulls the domain ceilings down with
    // it rather than leaving tools live in a frozen world. -xlinka
    private WorldMode EffectiveMode => SocialLock && _mode == WorldMode.Builder ? WorldMode.Social : _mode;

    // Whether a user may use a whole capability domain. THE one place the mode floor lands for domains:
    // the role's compiled cap and the host's toggle resolve first, and the ceiling is applied after, so a
    // toggle can only ever restrict below it. Binds every role including the host, by design. -xlinka
    public bool AllowsDomain(IPermissionActor? actor, PermissionDomain domain)
        => AllowsDomainFor(GetRole(actor), domain);

    public bool AllowsDomainFor(PermissionRole role, PermissionDomain domain)
    {
        if (!Enabled)
            return true;

        bool allowed = CapsFor(role).Allows(domain);
        return allowed && WorldModePolicy.ModeAllows(EffectiveMode, domain);
    }

    // Bounds a requested uniform scale to the actor's role cap. Bounds at or below zero are inactive,
    // which is what an unset config reads as.
    public float ClampScale(IPermissionActor? actor, float requested)
    {
        if (!Enabled)
            return requested;

        var caps = CapsFor(GetRole(actor));
        if (caps.MinScale > 0f && requested < caps.MinScale)
            return caps.MinScale;
        if (caps.MaxScale > 0f && requested > caps.MaxScale)
            return caps.MaxScale;
        return requested;
    }

    public bool TryGetScaleBounds(IPermissionActor? actor, out float min, out float max)
    {
        var caps = CapsFor(GetRole(actor));
        min = caps.MinScale;
        max = caps.MaxScale;
        return min > 0f || max > 0f;
    }

    // Role a freshly-joined user of the given access class gets (unless overridden).
    public PermissionRole GetDefaultRole(PermissionAccessClass accessClass)
        => _defaultRoles.TryGetValue(accessClass, out var role) ? role : GuestRole;

    public void SetDefaultRole(PermissionAccessClass accessClass, PermissionRole role)
    {
        if (role != null)
            _defaultRoles[accessClass] = role;
    }

    // GROUP ROSTER
    //
    // Replaces the whole roster in one call, same shape as ApplyPolicy: the caller hands over a complete
    // snapshot and this rebuilds from it, so there is no partial state and no path that merges a half
    // answer into a good one. A failed fetch must therefore NOT call this - the previous roster standing
    // is the point.
    public void SetGroupRoster(IReadOnlyDictionary<string, string>? members, IReadOnlyCollection<string>? bans)
    {
        _groupRoster.Clear();
        _groupBans.Clear();

        if (members != null)
        {
            foreach (var pair in members)
            {
                if (!string.IsNullOrEmpty(pair.Key))
                    _groupRoster[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        if (bans != null)
        {
            foreach (var account in bans)
            {
                if (!string.IsNullOrEmpty(account))
                    _groupBans.Add(account);
            }
        }
    }

    public bool IsGroupMember(string? accountKey)
        => !string.IsNullOrEmpty(accountKey) && _groupRoster.ContainsKey(accountKey!);

    public bool IsGroupBanned(string? accountKey)
        => !string.IsNullOrEmpty(accountKey) && _groupBans.Contains(accountKey!);

    // The rung the roster gives this account, or empty. These are the group service's words (Owner,
    // Admin, Moderator, Builder, Member), never a role of this engine's, and turning one into a world
    // role is done in exactly one place below.
    public string GroupRoleOf(string? accountKey)
        => !string.IsNullOrEmpty(accountKey) && _groupRoster.TryGetValue(accountKey!, out var role)
            ? role
            : string.Empty;

    public int GroupRosterCount => _groupRoster.Count;

    public int GroupBanCount => _groupBans.Count;

    // The top three rungs of the group service's five rung ladder. Compared by name because the words
    // are the service's; an unknown rung is not a moderator, which is the safe way for this to be wrong.
    public static bool IsGroupModeratorRank(string? groupRole)
        => string.Equals(groupRole, "Moderator", StringComparison.OrdinalIgnoreCase)
            || string.Equals(groupRole, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(groupRole, "Owner", StringComparison.OrdinalIgnoreCase);

    // Number of users with an explicit per-user role override.
    public int UserOverrideCount => _userRoles.Count;

    // Persisted assignments the current policy carries, counted by identity key.
    public int AssignmentCount => _assignedByAccount.Count + _assignedByMachine.Count;

    public void ClearUserOverrides()
    {
        _userRoles.Clear();
        _assignmentCache.Clear();
    }

    public IDisposable EnterActor(IPermissionActor? actor) => new Scope(actor, systemBypass: false);

    public IDisposable EnterSystemBypass() => new Scope(s_currentActor.Value, systemBypass: true);

    // A much narrower door than EnterSystemBypass, for the world's OWN authored logic running on the
    // authority: a button in the world flipping a light it was built alongside. Under the Social/Event
    // freeze that write is denied like any other edit of world content, which is right for a person and
    // wrong for the world's own wiring - the freeze exists to stop VISITORS rearranging the place, not to
    // stop the place from working.
    //
    // BOTH ENDS ARE PINNED TO AUTHORED CONTENT, and that is the whole design:
    //   `source` - the component asking. It must itself be authority-authored, so a visitor who spawns a
    //              control of their own and pokes it opens nothing. Without this the scope would be a
    //              door anybody could carry into a frozen world in their pocket.
    //   target   - what gets written. Must be authority-authored too, so the scope can never reach a
    //              user's belongings.
    // Plus: authority only, mutations and reads only, and only for the dynamic extent of the using block.
    //
    // Inside it the ACTOR's role is deliberately not the gate - the touching visitor is not the one
    // making the edit, the world's own wiring is, and it runs on the host either way. The full bypass
    // skips every one of the checks above, which is why authored controls should reach for this instead.
    // -xlinka
    public IDisposable EnterAuthoredContentScope(IPermissionTarget? source)
    {
        if (!_world.IsAuthority || _world.IsDisposed || !IsAuthorityAuthored(source))
            return s_inertScope;

        return new AuthoredContentScope(armed: true);
    }

    // Whether this element was minted by the authority, walking the ownership chain the way OwnsIdStrong
    // does. A user's object never satisfies it, no matter where it is currently parented.
    private bool IsAuthorityAuthored(IPermissionTarget? target)
    {
        for (int depth = 0; target != null && depth < 8; depth++)
        {
            if (_ids.IsAuthority(target.Id))
                return true;
            if (!_ids.IsNull(target.Id) && _ids.IsValidOwnerByte(_ids.OwnerByte(target.Id)))
                return false;
            target = target.OwnershipParent;
        }
        return false;
    }

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
        _assignmentCache.Remove(user.Id);
    }

    public void ClearUserRole(IPermissionActor user)
    {
        if (user != null)
        {
            _userRoles.Remove(user.Id);
            _assignmentCache.Remove(user.Id);
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

        // An explicit assignment - this session's host override, or the persisted one that matched this
        // user's identity - wins. Then the host's configured joiner default, then the per-access-class
        // default the mode baked in.
        if (TryGetAssignedRole(user, out var assigned))
            return assigned;

        if (TryGetGroupModeratorRole(user, out var groupModerator))
            return groupModerator;

        return _defaultJoinerRole ?? GetDefaultRole(GetAccessClass(user));
    }

    // A group's own moderators run that group's events. EVENT ONLY: being a moderator of a group is not
    // being a moderator of somebody's build session, so in Builder and Social worlds a group member gets
    // the Group class default and nothing more. The role handed out is this world's Moderator, which the
    // Event role set already offers, so the mode floor is never crossed - there is no path from here to
    // Builder or Admin. It sits under the assigned-role check on purpose: a host who pinned a role to
    // this person meant it. -xlinka
    private bool TryGetGroupModeratorRole(IPermissionActor user, out PermissionRole role)
    {
        role = ModeratorRole;
        if (_mode != WorldMode.Event || _groupRoster.Count == 0)
            return false;
        return IsGroupModeratorRank(GroupRoleOf(user.AccountKey));
    }

    // The role EXPLICITLY pinned to this user, if any. Deliberately does not fall back to any default,
    // because GetAccessClass is built on top of it and a default that consulted the class would loop.
    private bool TryGetAssignedRole(IPermissionActor user, out PermissionRole role)
    {
        // The session override is the host reaching in live; it outranks the persisted assignment on
        // purpose, so "make them a moderator right now" is not undone by what the file says.
        if (_userRoles.TryGetValue(user.Id, out role!))
            return true;

        if (_assignmentCache.TryGetValue(user.Id, out var cached))
        {
            role = cached!;
            return cached != null;
        }

        var account = user.AccountKey;
        var machine = user.MachineKey;

        PermissionRole? found = null;
        if (!string.IsNullOrEmpty(account) && _assignedByAccount.TryGetValue(account!, out var byAccount))
            found = byAccount;
        else if (!string.IsNullOrEmpty(machine) && _assignedByMachine.TryGetValue(machine!, out var byMachine))
            found = byMachine;

        // Only cache once the host has actually authored an identity for this user. Caching "no
        // assignment" for a user whose MachineID has not synced yet would pin the wrong answer for the
        // rest of the session. -xlinka
        if (!string.IsNullOrEmpty(machine))
            _assignmentCache[user.Id] = found;

        role = found!;
        return found != null;
    }

    // Classify a user for default-role purposes. The host is detected from the world's authority flag; an
    // ASSIGNED user is classified by the tier their role belongs to, which is what finally makes the
    // Contact/Anonymous defaults reachable instead of dead entries in a table. An unassigned user stays a
    // Visitor, the conservative answer, exactly as before.
    //
    // Group sits between the two: after the host check and the explicit assignment, before the Visitor
    // fallback. The only thing that puts anyone in it is the roster the HOST fetched, keyed on the account
    // id the host authored during the handshake. A client sends nothing that can reach either. Until the
    // first fetch lands the roster is empty and everyone is a Visitor, which is the answer that refuses
    // rather than the one that lets people in. -xlinka
    public PermissionAccessClass GetAccessClass(IPermissionActor? user)
    {
        if (user == null)
        {
            return PermissionAccessClass.Anonymous;
        }

        if (IsHostUser(user))
        {
            return PermissionAccessClass.Host;
        }

        if (TryGetAssignedRole(user, out var role))
        {
            return AccessClassOf(role);
        }

        if (IsGroupMember(user.AccountKey))
        {
            return PermissionAccessClass.Group;
        }

        return PermissionAccessClass.Visitor;
    }

    // Which tier a role speaks for. Assigning someone Builder is a statement that they are trusted like a
    // contact; assigning Spectator is a statement that they are not trusted at all.
    public PermissionAccessClass AccessClassOf(PermissionRole role)
    {
        if (ReferenceEquals(role, HostRole) || ReferenceEquals(role, AdminRole))
            return PermissionAccessClass.Host;
        if (ReferenceEquals(role, BuilderRole) || ReferenceEquals(role, ModeratorRole))
            return PermissionAccessClass.Contact;
        if (ReferenceEquals(role, SpectatorRole))
            return PermissionAccessClass.Anonymous;
        return PermissionAccessClass.Visitor;
    }

    public PermissionRole? FindRole(string? name)
        => !string.IsNullOrWhiteSpace(name) && _rolesByName.TryGetValue(name!, out var role) ? role : null;

    // Roles a host may assign to users in the given mode (the per-mode role set).
    public IReadOnlyList<PermissionRole> AssignableRolesFor(WorldMode mode)
    {
        // Social + Event are view/interact spaces: only moderation, normal user, and spectator make
        // sense - no Builder/Admin (there is nothing to build).
        if (mode == WorldMode.Social || mode == WorldMode.Event)
            return new[] { ModeratorRole, GuestRole, SpectatorRole };

        return AssignableRoles;
    }

    // The seed the world mode hands one access class, on its own, with no host config on top.
    //
    // Split out of ApplyMode because the host's config now overrides these PER CLASS: a row the host
    // cleared has to go back to exactly the role the mode would have given it, and the only way that
    // stays true is for both paths to read one table. Answer a new class here or it silently inherits
    // Visitor's answer. -xlinka
    public PermissionRole SeedRoleFor(WorldMode mode, PermissionAccessClass accessClass)
    {
        if (accessClass == PermissionAccessClass.Host)
            return AdminRole;
        if (accessClass == PermissionAccessClass.Anonymous)
            return SpectatorRole;

        return mode switch
        {
            // Frozen world; users may still bring/handle their own items (Guest = "User": own objects
            // fully editable, the world view-only). The SocialLock floor denies any edit of the authored
            // world for everyone, host included.
            WorldMode.Social => GuestRole,
            // Strictest: view + interact only, no spawning even of your own items.
            WorldMode.Event => SpectatorRole,
            _ => BuilderRole
        };
    }

    // Load a mode's preset: the lock floor plus the default role per access class. Baked at host time
    // from the world mode. The floor value comes from WorldModePolicy so it is defined in exactly one
    // place.
    public void ApplyMode(WorldMode mode)
    {
        _mode = mode;
        SocialLock = WorldModePolicy.SocialLockFloor(mode);

        SetDefaultRole(PermissionAccessClass.Anonymous, SeedRoleFor(mode, PermissionAccessClass.Anonymous));
        SetDefaultRole(PermissionAccessClass.Visitor, SeedRoleFor(mode, PermissionAccessClass.Visitor));
        SetDefaultRole(PermissionAccessClass.Contact, SeedRoleFor(mode, PermissionAccessClass.Contact));
        SetDefaultRole(PermissionAccessClass.Group, SeedRoleFor(mode, PermissionAccessClass.Group));
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
            return Deny(request, reason, PermissionDenialKind.Unclassified);
        }

        foreach (var rule in _rules)
        {
            var result = rule.Evaluate(request, actor, out var ruleReason);
            if (result == PermissionResult.Allow)
            {
                return true;
            }
            if (result == PermissionResult.Deny)
            {
                reason = ruleReason ?? "denied by datamodel permission rule";
                return Deny(request, reason, PermissionDenialKind.Unclassified);
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

        // SAVE-COPY / EXPORT. Neither one changes the world, so they are settled here, before the freeze
        // (which only speaks about mutations) and before the role check (whose foreign masks are only one
        // of the three things a copy has to satisfy). A request that carries nothing else is finished
        // either way; a request that carries copy bits alongside real mutations falls through so the rest
        // is still judged. -xlinka
        if ((request.Action & PermissionAction.Copy) != 0)
        {
            if (!AuthorizeCopy(in request, role, actor, out var copyReason))
            {
                reason = copyReason;
                return Deny(request, reason!, PermissionDenialKind.ForeignWrite);
            }

            if ((request.Action & ~PermissionAction.Copy) == 0)
            {
                return true;
            }
        }

        // The world's own authored logic, running on the authority against authority-authored content.
        // Narrow on purpose - see EnterAuthoredContentScope. Sits here, above the freeze, because the
        // freeze is the only gate it is meant to lift: everything before this point (system bypass, actor
        // resolution, registered rules) has already run, and nothing after it is skipped for a target this
        // scope does not cover.
        if (s_authoredContentDepth.Value > 0
            && world.IsAuthority
            && (request.Action & ~(PermissionAction.Mutation | PermissionAction.Read | PermissionAction.CollectionEnumerate)) == 0
            && IsAuthorityAuthored(request.Target ?? request.Parent))
        {
            return true;
        }

        // Social/Event floor: the authored world is frozen for EVERYONE incl. the host. Only a user's
        // own runtime objects may be mutated; world content (authority-owned) is foreign to all and
        // denied regardless of role. This is the unbypassable lock - no role escapes it, no live toggle.
        if (SocialLock && !ownsTarget && (request.Action & PermissionAction.Mutation) != 0)
        {
            reason = "editing is disabled in this world (social)";
            return Deny(request, reason, PermissionDenialKind.LockedWorld);
        }

        // Grab interactions: a grabbable object opts into being picked up + moved by ANY user - that's an
        // interaction, not an ownership edit. Allow the grab-state refs (who holds it / its restore-parent)
        // and the reparent+pose, but ONLY for the user who is actually holding the thing. The host still
        // owns the object and arbitrates the authoritative holder, and SocialLock above already froze this
        // in event worlds. Ordinary edits to the object stay owner-gated by the role check below. -xlinka
        if (IsAllowedGrabWrite(in request, actor, world, out bool grabTraffic))
        {
            return true;
        }

        // A held foreign object rides under the holder's root, and OwnsTarget reads that as ownership - so
        // without this, picking something up would hand you full edit rights over it for as long as you
        // held it (retune its throw limits, swap its material, rewrite its components). Everything a grab
        // legitimately writes was already allowed above, so anything still arriving here is an ordinary
        // edit of someone else's property and must answer the role's FOREIGN question. Strips only the
        // grab-flip: real per-byte ownership still reads as owned, so your own equipment is untouched, and
        // a role that may edit foreign objects anyway (host, admin, builder) still can. -xlinka
        if (ownsTarget && (request.Action & PermissionAction.Mutation) != 0
            && IsHeldByActor(in request, actor)
            && !OwnsIdStrong(actor, request.Target) && !OwnsIdStrong(actor, request.Parent))
        {
            ownsTarget = false;
            if (!role.Allows(request.Action, ownsTarget))
            {
                reason = "editing a held object requires ownership, not just holding it";
                return Deny(request, reason, PermissionDenialKind.ForeignWrite);
            }
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
                // Strip the grab-flip signal and ask the role the real question, as if the actor were not
                // holding it. Denying outright here took away authority the role grants: the host deleting a
                // component off something in its own hand was refused, then allowed the moment it let go. A
                // guest holding host content still fails, because its role cannot destroy foreign objects.
                ownsTarget = false;
                if (!role.Allows(request.Action, ownsTarget))
                {
                    reason = "destroying a held object requires ownership, not just holding it";
                    return Deny(request, reason, PermissionDenialKind.Ownership);
                }
            }
        }

        if (role.Allows(request.Action, ownsTarget))
        {
            return true;
        }

        reason = $"role '{role.Name}' cannot perform {request.Action} on {request.Surface}";
        return Deny(request, reason, grabTraffic
            ? PermissionDenialKind.GrabContention
            : ClassifyDenial(in request, world));
    }

    // What a refused write was reaching for, for the escalation ledger's weighting. Destroying or
    // unregistering something you do not own is a decision; a plain field write on someone else's object
    // is what a stale client does by accident. -xlinka
    private static PermissionDenialKind ClassifyDenial(in PermissionRequest request, IPermissionWorldFacts world)
    {
        if (ReferenceEquals(request.Target, world.SlotRegistry) || ReferenceEquals(request.Parent, world.SlotRegistry))
            return PermissionDenialKind.Ownership;

        if ((request.Action & DestructiveActions) != 0)
            return PermissionDenialKind.Ownership;

        return PermissionDenialKind.ForeignWrite;
    }

    public void Assert(in PermissionRequest request)
    {
        if (!Authorize(request, out var reason))
        {
            throw new UnauthorizedAccessException(reason ?? "datamodel mutation denied");
        }
    }

    private bool Deny(in PermissionRequest request, string reason, PermissionDenialKind kind)
    {
        var actor = request.IsNetwork
            ? request.Actor
            : request.Actor ?? s_currentActor.Value ?? request.World?.LocalActor;

        if (LogDeniedMutations)
        {
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

        // Only a refusal of something that arrived OVER THE WIRE counts against a peer. A local denial is
        // this machine refusing its own optimistic write, which every client does constantly and honestly.
        if (request.IsNetwork)
            ReportDenial(actor, kind, reason);

        return false;
    }

    // VIOLATION ESCALATION
    // Records one refused inbound write against the sender and acts if their score crosses the host's
    // threshold. Public because refusals that never reach Authorize - a host-only member forged on the
    // wire, a link claim the arbitration ledger rejected - are exactly the ones worth the most weight, and
    // the adapter reports them here. -xlinka
    public void ReportDenial(IPermissionActor? actor, PermissionDenialKind kind, string? reason = null)
    {
        if (actor == null || !_world.IsAuthority || _world.IsDisposed)
            return;

        // Never escalate against the host: it cannot kick itself, and its own denials are the floor doing
        // its job (a locked world refuses the host too).
        if (IsHostUser(actor))
            return;

        var identity = IdentityOf(actor);
        if (identity == null)
            return;

        var verdict = _ledger.Record(identity, kind, Now, out double score, out bool warn);

        if (warn)
        {
            Warn?.Invoke($"Permission violations from '{actor.DisplayName ?? identity}': score {score:0.0} "
                + $"({_ledger.CountFor(identity)} refusals, latest {kind}{(reason != null ? ": " + reason : string.Empty)})");
            Enforcement?.WarnHost(actor, score, $"{_ledger.CountFor(identity)} refused writes, latest {kind}");
        }

        switch (verdict)
        {
            case PermissionViolationResponse.Kick:
                Warn?.Invoke($"Kicking '{actor.DisplayName ?? identity}': permission violation score {score:0.0} (latest {kind}).");
                Enforcement?.Kick(actor, $"permission violation score {score:0.0}, latest {kind}");
                break;

            case PermissionViolationResponse.TempBan:
                Warn?.Invoke($"Temp-banning '{actor.DisplayName ?? identity}': permission violation score {score:0.0} (latest {kind}).");
                Enforcement?.TempBan(actor, $"permission violation score {score:0.0}, latest {kind}");
                break;
        }
    }

    // The key a peer's denial score hangs off. Account first so it survives a machine change, machine as
    // the fallback that always exists. A peer with neither is not tracked - the host authors both, so that
    // only happens before the handshake finished, and there is nothing to escalate against yet.
    public static string? IdentityOf(IPermissionActor actor)
    {
        var account = actor.AccountKey;
        if (!string.IsNullOrEmpty(account))
            return "a:" + account;

        var machine = actor.MachineKey;
        return string.IsNullOrEmpty(machine) ? null : "m:" + machine;
    }

    // Seconds on whatever clock the ledger is keeping. Exposed because anything asking the ledger a
    // question has to ask it on the SAME clock the entries were written on, or a decay computed against
    // wall time reads every score as zero. -xlinka
    public double Now => Clock?.Invoke() ?? (Environment.TickCount64 / 1000.0);

    public double DenialScoreOf(IPermissionActor? actor)
    {
        if (actor == null)
            return 0.0;
        var identity = IdentityOf(actor);
        return identity == null ? 0.0 : _ledger.ScoreFor(identity, Now);
    }

    // SAVE-COPY / EXPORT
    // Three separate questions, all of which have to say yes for someone else's object: the role may copy
    // at all (its cap plus the mode ceiling), the role may copy things it does not own (the compiled
    // foreign mask, which an explicit Grant is the only thing that widens), and the object is not marked
    // against it. Your own object skips all three. -xlinka
    private bool AuthorizeCopy(in PermissionRequest request, PermissionRole role, IPermissionActor actor, out string? reason)
    {
        reason = null;
        var action = request.Action & PermissionAction.Copy;

        // Ownership on the copy path is the allocation byte ONLY. The structural signal flips the moment
        // anything is parented under the actor's root (a grab, an equip), and "I am holding it" must
        // never mean "it is mine to copy". A mutation needs the structural signal so you can pose what
        // you carry; a copy needs nothing of the sort. -xlinka
        if (OwnsIdStrong(actor, request.Target) || OwnsIdStrong(actor, request.Parent))
            return true;

        // The authority is already holding every byte of this world in its own memory. A marker that
        // "stopped" the machine serving the world would be theatre, and we do not ship controls that only
        // look like controls. -xlinka
        if (IsHostUser(actor))
            return true;

        var protection = (request.Target ?? request.Parent)?.CopyProtection;
        if (protection != null)
        {
            if ((action & PermissionAction.SaveCopy) != 0 && protection.BlocksSaveCopy)
            {
                reason = "this object is marked as not copyable";
                return false;
            }
            if ((action & PermissionAction.Export) != 0 && protection.BlocksExport)
            {
                reason = "this object is marked as not exportable";
                return false;
            }
        }

        if ((action & PermissionAction.SaveCopy) != 0
            && !AllowsForeignCopy(role, PermissionDomain.SaveCopy, PermissionAction.SaveCopy))
        {
            reason = $"role '{role.Name}' cannot save a copy of another user's object";
            return false;
        }

        if ((action & PermissionAction.Export) != 0
            && !AllowsForeignCopy(role, PermissionDomain.Export, PermissionAction.Export))
        {
            reason = $"role '{role.Name}' cannot export another user's object";
            return false;
        }

        return true;
    }

    private bool AllowsForeignCopy(PermissionRole role, PermissionDomain domain, PermissionAction action)
    {
        if (!AllowsDomainFor(role, domain))
            return false;

        // Compiled answer first: only the host tier carries copy rights over other people's objects. An
        // explicit Grant is what widens it; Inherit leaves it exactly where the compiled cap put it, which
        // is the whole point of the tri-state.
        if (role.Allows(action, ownsTarget: false))
            return true;

        return ToggleFor(role, domain) == PermissionToggle.Grant;
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

    // Whether this write is a permitted move in the grab protocol. False does NOT mean refused: it means
    // the grab protocol has nothing to say, and the ordinary ownership/role rules decide. That fall-through
    // is what keeps host arbitration working - the host handing an object to someone else is not a grab it
    // is making, it is a decision its role entitles it to. `grabTraffic` reports whether the write was
    // grab-shaped at all, so a refusal that comes out of the role check downstream can be weighted as
    // contention rather than as an attack.
    //
    // WHO MAY WRITE WHAT:
    //   HolderRef - who is holding it. Free object: anyone may claim it. Held: only the holder (that is a
    //               release) unless the object opts into being stolen.
    //   Transform - the held object's parent and pose. The holder, or - and this is the case that needs
    //               the batch - the user whose SAME BATCH also claims the holder ref. The authority
    //               validates every record in a batch before it decodes any of them, so at the moment a
    //               pickup's reparent is judged, the holder ref still reads pre-grab; without the in-batch
    //               claim every legitimate pickup would be refused for not already holding the thing it is
    //               picking up. A claim does NOT buy the transform when the object is held and unstealable,
    //               or the holder-ref refusal would be worth nothing.
    //   GrabState - restore-parent and release velocities. Same rule as Transform: this is the hand-off,
    //               and it belongs to whoever is doing the handing.
    //
    // LOCAL WRITES are held to the weaker "held by nobody, or held by you" line. A client authors its own
    // grab optimistically and in pieces - the release clears the holder ref BEFORE it reparents, so at the
    // moment of the local reparent nobody holds the object - and the authority re-judges the replicated
    // records under the full rule anyway. Refusing locally would only break the honest client's own
    // animation while changing nothing a hostile one can do. -xlinka
    private static bool IsAllowedGrabWrite(in PermissionRequest request, IPermissionActor actor, IPermissionWorldFacts world, out bool grabTraffic)
    {
        grabTraffic = false;

        var member = request.Member;
        if (member == null)
            return false;

        if (request.Parent is not IPermissionGrabSurface surface)
            return false;

        var kind = surface.ClassifyGrabWrite(member);
        if (kind == GrabWriteKind.None || !surface.AllowsGrab)
            return false;

        grabTraffic = true;
        var holder = surface.CurrentHolder;

        if (kind == GrabWriteKind.HolderRef)
        {
            if (holder == null || ReferenceEquals(holder, actor))
                return true;
            return surface.AllowsSteal;
        }

        if (ReferenceEquals(holder, actor))
            return true;

        if (!request.IsNetwork)
            return holder == null;

        if (holder == null)
            return world.BatchClaimsGrab(surface, actor);

        return surface.AllowsSteal && world.BatchClaimsGrab(surface, actor);
    }

    // Whether the thing being written to is, or hangs off, a grabbable this actor is holding right now.
    // Walks the ownership chain because a write to some other component on a held prop has that component
    // as its parent, not the slot the grabbable sits on.
    private static bool IsHeldByActor(in PermissionRequest request, IPermissionActor actor)
    {
        var node = request.Parent ?? request.Target;
        for (int depth = 0; node != null && depth < 4; depth++)
        {
            if (node is IPermissionGrabSurface surface
                && surface.AllowsGrab
                && ReferenceEquals(surface.CurrentHolder, actor))
            {
                return true;
            }
            node = node.OwnershipParent;
        }
        return false;
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
