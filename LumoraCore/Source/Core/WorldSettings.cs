// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Nexus.Cloud;

namespace Lumora.Core;

// Because it rides the normal component replication, settings replicate to clients, persist with the world
// (in the slot tree), and update live - reached via Configuration. The authority owns it; a client receives
// it through state sync. Edit/access policy (e.g. Mode) is still enforced host-authoritatively by the
// permission gate, not by trusting these synced values.
[ComponentCategory("World")]
public sealed class WorldSettings : Component
{
    [Group("Access")]
    public readonly Sync<int> MaxUsers = new();
    public readonly Sync<bool> AllowJoin = new();
    public readonly Sync<bool> IsPublic = new();
    public readonly Sync<World.WorldAccessLevel> AccessLevel = new();

    // The group this session is hosted for, or empty. The host writes it; a guest only reads it, which is
    // why it is host-only - a guest that could set it would hand itself the moderator marker on the
    // nametag of anyone in the same group. Nothing in this package SETS it: hosting a world for a group is
    // its own piece of work, and this is the field it will write. What already reads it is the nametag,
    // which only shows a group's moderators as moderators inside that group's own world. -xlinka
    public readonly Sync<string> HostGroupId = new();

    // Baked at host; drives the permission preset.
    [Group("Mode")]
    public readonly Sync<WorldMode> Mode = new();

    public readonly Sync<bool> MobileFriendly = new();
    public readonly Sync<bool> EditMode = new();
    [Group("Session")]
    public readonly Sync<bool> HideFromSessionLists = new();
    public readonly Sync<bool> AutoKickAFK = new();
    public readonly Sync<int> MaxAFKMinutes = new();
    [Group("Assets")]
    public readonly Sync<bool> CleanupUnusedAssets = new();
    public readonly Sync<float> AssetCleanupInterval = new();
    [Group("Info")]
    public readonly Sync<string> Description = new();
    [Group("Persistence")]
    public readonly Sync<bool> EnablePersistence = new();
    public readonly Sync<float> AutoSaveInterval = new();
    public readonly Sync<int> MaxWorldSizeMB = new();

    [Group("Discovery")]
    public readonly SyncFieldList<string> Tags = new();

    // Sync send/process rate in Hz for THIS session. Host-only: a guest turning it down would not make
    // the host send less, it would only starve that guest's own loop, so the host owns it and everyone
    // runs at the same rate. MarkHostOnly makes the authority refuse a guest's delta for it outright,
    // which is the actual enforcement - the Session screen only hides the control. -xlinka
    [Group("Network")]
    public readonly Sync<int> NetworkTickRate = new();

    public const int DefaultTickRate = 70;
    public const int MinTickRate = 10;
    public const int MaxTickRate = 120;

    public override void OnInit()
    {
        base.OnInit();
        MaxUsers.Value = 32;
        AllowJoin.Value = true;
        IsPublic.Value = false;
        AccessLevel.Value = World.WorldAccessLevel.Private;
        HostGroupId.Value = string.Empty;
        Mode.Value = WorldMode.Builder;
        MobileFriendly.Value = false;
        EditMode.Value = false;
        HideFromSessionLists.Value = false;
        AutoKickAFK.Value = false;
        MaxAFKMinutes.Value = 30;
        CleanupUnusedAssets.Value = true;
        AssetCleanupInterval.Value = 300f;
        Description.Value = string.Empty;
        EnablePersistence.Value = false;
        AutoSaveInterval.Value = 0f;
        MaxWorldSizeMB.Value = 512;
        NetworkTickRate.Value = DefaultTickRate;
    }

    public override void OnAwake()
    {
        base.OnAwake();
        NetworkTickRate.MarkHostOnly();
        HostGroupId.MarkHostOnly();
    }

    public override void OnStart()
    {
        base.OnStart();

        // A world saved before the tick rate moved here loads with a zero. Normalize it on the
        // authority so the Session screen shows a real number instead of an out-of-range 0.
        if (World != null && World.IsAuthority && NetworkTickRate.Value <= 0)
            NetworkTickRate.Value = DefaultTickRate;

        // Push the session rate into the sync manager on the MAIN thread. The sync loop reads its
        // cached copy off-thread every idle wait; reaching into the data model from there to resolve
        // a component would race the main thread's attaches. -xlinka
        PublishTickRate();
        NetworkTickRate.OnChanged += _ => PublishTickRate();
        // Keep the permission preset in lockstep with the mode, ON EVERY PEER. The host is still the only
        // gate that decides anything - it refuses a guest's delta and corrects it - but a guest whose local
        // gate never ran the preset keeps the constructor's Builder defaults, so its own client cheerfully
        // authorises edits the host is about to throw away. That is the first wall a guest hits in a social
        // world and it is completely invisible from the client side. The mode value is host-authored and
        // replicated, so applying it locally only ever makes a guest agree with the host sooner. -xlinka
        if (World != null)
            WorldModePermissions.Apply(World, Mode.Value);
        Mode.OnChanged += _ =>
        {
            if (World != null)
                WorldModePermissions.Apply(World, Mode.Value);
        };

        // Access level is host-authoritative LIVE state: when the host changes it at runtime, re-advertise
        // the session and start/stop the LAN beacon to match. (The beacon was otherwise only chosen once at
        // create time, which is why you used to have to "open as LAN" instead of switching to it.) Gated to
        // the host AND to a Running world so the create-time seed in StartSession doesn't fire this before the
        // session even exists (the initial beacon comes from the session metadata as before). A client just
        // receives the synced value and never touches the beacon. -xlinka
        AccessLevel.OnChanged += _ =>
        {
            if (World != null && World.IsAuthority && World.State == World.WorldState.Running)
                World.Session?.SetVisibility(ToVisibility(AccessLevel.Value, HostGroupId.Value.Length > 0));
        };
    }

    private void PublishTickRate()
    {
        int rate = NetworkTickRate.Value;
        if (rate <= 0)
            rate = DefaultTickRate;
        World?.Session?.Sync?.SetSyncRate(System.Math.Clamp(rate, MinTickRate, MaxTickRate));
    }

    // Map the user-facing access level to the network session visibility that drives the LAN beacon / public
    // registration. Contacts tiers advertise to contacts, the open tiers advertise publicly.
    //
    // A world actually hosted FOR a group advertises publicly whichever group tier it is on. Members have
    // to be able to FIND the thing before the door can refuse everybody else, and a world nobody can find
    // is not a group world. The flag matters: a world set to a group tier with no HostGroupId has no door
    // behind it, so it keeps the old silent answer rather than being quietly published to the directory.
    // -xlinka
    internal static SessionVisibility ToVisibility(World.WorldAccessLevel level, bool hostedForGroup = false)
    {
        bool groupTier = level == World.WorldAccessLevel.GroupMembers
            || level == World.WorldAccessLevel.GroupPlus;
        if (groupTier && hostedForGroup)
            return SessionVisibility.Public;

        return level switch
        {
            World.WorldAccessLevel.Private => SessionVisibility.Private,
            World.WorldAccessLevel.LAN => SessionVisibility.LAN,
            World.WorldAccessLevel.Contacts or World.WorldAccessLevel.ContactsPlus
                or World.WorldAccessLevel.GroupMembers or World.WorldAccessLevel.GroupPlus
                => SessionVisibility.Contacts,
            _ => SessionVisibility.Public, // RegisteredUsers / Anyone / GroupPublic
        };
    }
}
