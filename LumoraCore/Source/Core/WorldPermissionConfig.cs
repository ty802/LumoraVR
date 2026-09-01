// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core;

// One user pinned to one role. Keyed by durable identity rather than by a SyncRef to the User, because a
// User worker only exists while that person is connected: a ref would be dangling the moment they left and
// would save as nothing. AccountId when they have one (it follows them across machines), MachineId always.
public sealed class PermissionRoleAssignment : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<string> MachineId = new();
    public readonly Sync<string> AccountId = new();
    public readonly Sync<string> Role = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
        WorldPermissionConfig.LockToHost(MachineId, AccountId, Role);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("MachineId", MachineId.Save(control));
        dictionary.Add("AccountId", AccountId.Save(control));
        dictionary.Add("Role", Role.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("MachineId") is { } machineNode)
            MachineId.Load(machineNode, control);
        if (dictionary.TryGetNode("AccountId") is { } accountNode)
            AccountId.Load(accountNode, control);
        if (dictionary.TryGetNode("Role") is { } roleNode)
            Role.Load(roleNode, control);
    }

    public override object? GetValueAsObject() => Role.Value;

    internal void OnAnyChange(Action handler)
        => WorldPermissionConfig.Subscribe(handler, MachineId, AccountId, Role);
}

// Per-role overrides of the compiled capability caps. Every toggle is tri-state: Inherit leaves the
// compiled answer alone, which is what an empty or half-filled entry reads as. Scale bounds are inactive at
// zero or less.
public sealed class PermissionRoleCapEntry : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly Sync<string> Role = new();
    public readonly Sync<DataModelPermissionToggle> Spawn = new();
    public readonly Sync<DataModelPermissionToggle> SaveCopy = new();
    public readonly Sync<DataModelPermissionToggle> Export = new();
    public readonly Sync<DataModelPermissionToggle> ToolUse = new();
    public readonly Sync<DataModelPermissionToggle> Touch = new();
    public readonly Sync<float> MinScale = new();
    public readonly Sync<float> MaxScale = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
        WorldPermissionConfig.LockToHost(Role, Spawn, SaveCopy, Export, ToolUse, Touch, MinScale, MaxScale);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Role", Role.Save(control));
        dictionary.Add("Spawn", Spawn.Save(control));
        dictionary.Add("SaveCopy", SaveCopy.Save(control));
        dictionary.Add("Export", Export.Save(control));
        dictionary.Add("ToolUse", ToolUse.Save(control));
        dictionary.Add("Touch", Touch.Save(control));
        dictionary.Add("MinScale", MinScale.Save(control));
        dictionary.Add("MaxScale", MaxScale.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        LoadMember(dictionary, "Role", Role, control);
        LoadMember(dictionary, "Spawn", Spawn, control);
        LoadMember(dictionary, "SaveCopy", SaveCopy, control);
        LoadMember(dictionary, "Export", Export, control);
        LoadMember(dictionary, "ToolUse", ToolUse, control);
        LoadMember(dictionary, "Touch", Touch, control);
        LoadMember(dictionary, "MinScale", MinScale, control);
        LoadMember(dictionary, "MaxScale", MaxScale, control);
    }

    private static void LoadMember(DataTreeDictionary dictionary, string name, SyncElement member, LoadControl control)
    {
        if (dictionary.TryGetNode(name) is { } node)
            member.Load(node, control);
    }

    public override object? GetValueAsObject() => Role.Value;

    internal void OnAnyChange(Action handler)
        => WorldPermissionConfig.Subscribe(handler, Role, Spawn, SaveCopy, Export, ToolUse, Touch, MinScale, MaxScale);
}

// The host's permission configuration for this world, as replicated, persisted DATA. It decides nothing:
// every field here is marshalled into a policy snapshot and handed to the gate, which is the only thing
// that turns any of it into an answer.
//
// A COMPONENT OF ITS OWN, not more fields on WorldSettings, for one reason: every member on this surface
// has to be host-only, and the only way to guarantee that for members nobody has written yet is to blanket
// it. LockToHost runs over the whole component, so a field added here next year is host-only by
// construction. Bolted onto WorldSettings the same sweep would have locked down MaxUsers and Description
// too, and the alternative - remembering to mark each new field by hand - is exactly the kind of promise
// that gets broken once and then stays broken quietly. It also gives round 2's session UI a single object
// to bind to and the world save a single node to carry. -xlinka
//
// Guests RECEIVE this (their UI needs to show who holds which role) and can never write it: MarkHostOnly
// makes the authority refuse a guest's delta for any member outright, so the enforcement is in the sync
// layer, not in whether the UI drew a control.
[ComponentCategory("World")]
public sealed class WorldPermissionConfig : Component
{
    [Group("Roles")]
    public readonly SyncList<PermissionRoleAssignment> Assignments;

    // Role a joiner with no assignment lands on. Empty falls back to the per-access-class default the world
    // mode baked in, which is never wider.
    //
    // SUPERSEDED by the four per-class fields below and no longer offered anywhere in the UI: one role for
    // everybody who walks in cannot say "signed in gets User, contacts get Builder". The field stays because
    // saved worlds carry it and the gate still honours it, and the session screen clears it the moment the
    // host touches a per-class row so the two can never disagree about who gets what. -xlinka
    public readonly Sync<string> DefaultJoinerRole;

    // Who gets which role on arrival, one field per access class. Empty means "whatever the world mode
    // seeds that class with", which is what ApplyConfig pushes back through SetDefaultRole - so clearing a
    // row is a real answer and not just an absence. Group is configurable before anything can classify a
    // user into it; see PermissionEngine.GetAccessClass.
    public readonly Sync<string> AnonymousRole;
    public readonly Sync<string> VisitorRole;
    public readonly Sync<string> ContactRole;
    public readonly Sync<string> GroupRole;

    [Group("Capabilities")]
    public readonly SyncList<PermissionRoleCapEntry> RoleCaps;

    // ESCALATION. Conservative by default: warn the host, kick a peer that keeps grinding, and never
    // temp-ban unless the host asks for it. The scores are weighted counts, not raw counts - one refused
    // foreign destroy is worth ten lost grabs (see PermissionLedger).
    [Group("Escalation")]
    public readonly Sync<DataModelViolationResponse> ViolationResponse;
    public readonly Sync<float> DenialHalfLifeSeconds;
    public readonly Sync<float> WarnScore;
    public readonly Sync<float> KickScore;
    public readonly Sync<float> TempBanScore;

    public WorldPermissionConfig()
    {
        Assignments = new SyncList<PermissionRoleAssignment>();
        RoleCaps = new SyncList<PermissionRoleCapEntry>();
        DefaultJoinerRole = new Sync<string>(this, string.Empty);
        AnonymousRole = new Sync<string>(this, string.Empty);
        VisitorRole = new Sync<string>(this, string.Empty);
        ContactRole = new Sync<string>(this, string.Empty);
        GroupRole = new Sync<string>(this, string.Empty);
        ViolationResponse = new Sync<DataModelViolationResponse>(this, DataModelViolationResponse.Kick);
        DenialHalfLifeSeconds = new Sync<float>(this, 60f);
        WarnScore = new Sync<float>(this, 10f);
        KickScore = new Sync<float>(this, 50f);
        TempBanScore = new Sync<float>(this, 200f);
    }

    public override void OnInit()
    {
        base.OnInit();
        DefaultJoinerRole.Value = string.Empty;
        AnonymousRole.Value = string.Empty;
        VisitorRole.Value = string.Empty;
        ContactRole.Value = string.Empty;
        GroupRole.Value = string.Empty;
        ViolationResponse.Value = DataModelViolationResponse.Kick;
        DenialHalfLifeSeconds.Value = 60f;
        WarnScore.Value = 10f;
        KickScore.Value = 50f;
        TempBanScore.Value = 200f;
    }

    public override void OnAwake()
    {
        base.OnAwake();

        // The blanket. Runs over every member this component has, including ones added later, so there is
        // no per-field promise to forget. A guest's delta for any of them is refused at the authority. The
        // lists are locked too: without that a guest could ADD an assignment naming itself. -xlinka
        for (int i = 0; i < SyncMemberCount; i++)
        {
            if (GetSyncMember(i) is ConflictingSyncElement element)
            {
                element.MarkHostOnly();
                element.MarkNonDrivable();
            }
        }

        // Live-apply: any config change rebuilds the gate's mirror once, right then. No polling, and no
        // reading this component from inside a decision. Wired in OnAwake rather than OnStart because
        // startup is deferred a tick, and a host that assigns a role in the same breath as attaching the
        // component would otherwise write into a surface nothing was listening to yet. -xlinka
        for (int i = 0; i < SyncMemberCount; i++)
        {
            if (GetSyncMember(i) is IChangeable changeable)
                changeable.Changed += _ => Republish();
        }

        // A list's own change event says nothing about the fields INSIDE its entries, so each entry gets
        // hooked as it arrives - whether the host added it or it decoded off the wire.
        Assignments.ElementsAdded += (list, index, count) => TrackAssignments(list, index, count);
        Assignments.ElementsRemoved += (_, _, _) => Republish();
        RoleCaps.ElementsAdded += (list, index, count) => TrackCaps(list, index, count);
        RoleCaps.ElementsRemoved += (_, _, _) => Republish();
    }

    private void TrackAssignments(SyncElementList<PermissionRoleAssignment> list, int index, int count)
    {
        for (int i = index; i < index + count && i < list.Count; i++)
            list[i]?.OnAnyChange(Republish);
        Republish();
    }

    private void TrackCaps(SyncElementList<PermissionRoleCapEntry> list, int index, int count)
    {
        for (int i = index; i < index + count && i < list.Count; i++)
            list[i]?.OnAnyChange(Republish);
        Republish();
    }

    public override void OnStart()
    {
        base.OnStart();
        Republish();
    }

    // Hand the gate a fresh snapshot. Cheap and rare: one rebuild per config edit, never per frame.
    public void Republish() => World?.DataModelPermissions?.ApplyConfig(this);

    // FINDERS / EDITORS, for round 2's session screen. All host-side: on a guest these write into members
    // the authority will refuse, so the UI must gate on World.IsAuthority before offering them.

    public PermissionRoleAssignment? FindAssignment(string? machineId, string? accountId)
    {
        foreach (var entry in Assignments)
        {
            if (!string.IsNullOrEmpty(accountId) && entry.AccountId.Value == accountId)
                return entry;
            if (!string.IsNullOrEmpty(machineId) && entry.MachineId.Value == machineId)
                return entry;
        }
        return null;
    }

    public PermissionRoleAssignment? FindAssignment(User? user)
        => user == null ? null : FindAssignment(user.MachineID.Value, user.AccountId.Value);

    // Pins a user to a role by their durable identity, replacing whatever they had. A null or unknown role
    // name clears the assignment instead, which drops them back to the joiner default.
    public void SetRole(User? user, string? roleName)
    {
        if (user == null || World == null || !World.IsAuthority)
            return;

        var existing = FindAssignment(user);
        if (string.IsNullOrWhiteSpace(roleName))
        {
            if (existing != null)
                Assignments.Remove(existing);
            Republish();
            return;
        }

        var entry = existing ?? Assignments.Add();
        entry.MachineId.Value = user.MachineID.Value ?? string.Empty;
        entry.AccountId.Value = user.AccountId.Value ?? string.Empty;
        entry.Role.Value = roleName!;
        Republish();
    }

    public PermissionRoleCapEntry GetOrAddCap(string roleName)
    {
        foreach (var entry in RoleCaps)
        {
            if (entry.Role.Value == roleName)
                return entry;
        }

        var added = RoleCaps.Add();
        added.Role.Value = roleName;
        return added;
    }

    // Shared by the entry types: a config member is host-authored, full stop, and driving one would let a
    // component in the world author the permission config from inside the world it is gated by. -xlinka
    internal static void LockToHost(params SyncElement[] members)
    {
        foreach (var member in members)
        {
            if (member is ConflictingSyncElement element)
            {
                element.MarkHostOnly();
                element.MarkNonDrivable();
            }
        }
    }

    internal static void Subscribe(Action handler, params SyncElement[] members)
    {
        foreach (var member in members)
        {
            if (member is IChangeable changeable)
                changeable.Changed += _ => handler();
        }
    }
}
