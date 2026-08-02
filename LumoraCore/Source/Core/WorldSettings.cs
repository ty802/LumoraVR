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

    public override void OnInit()
    {
        base.OnInit();
        MaxUsers.Value = 32;
        AllowJoin.Value = true;
        IsPublic.Value = false;
        AccessLevel.Value = World.WorldAccessLevel.Private;
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
    }

    public override void OnStart()
    {
        base.OnStart();
        // Keep the permission preset in lockstep with the mode: if Mode ever changes (e.g. a world is
        // loaded with a different mode), the authority re-applies the matching Social/Builder/Event
        // lock. World.Mode itself can't be toggled live (see World.Mode setter); this just guarantees
        // the gate never drifts from the value.
        Mode.OnChanged += _ =>
        {
            if (World != null && World.IsAuthority)
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
                World.Session?.SetVisibility(ToVisibility(AccessLevel.Value));
        };
    }

    // Map the user-facing access level to the network session visibility that drives the LAN beacon / public
    // registration. Contacts tiers advertise to contacts, the open tiers advertise publicly. -xlinka
    internal static SessionVisibility ToVisibility(World.WorldAccessLevel level) => level switch
    {
        World.WorldAccessLevel.Private => SessionVisibility.Private,
        World.WorldAccessLevel.LAN => SessionVisibility.LAN,
        World.WorldAccessLevel.Contacts or World.WorldAccessLevel.ContactsPlus
            or World.WorldAccessLevel.GroupMembers or World.WorldAccessLevel.GroupPlus
            => SessionVisibility.Contacts,
        _ => SessionVisibility.Public, // RegisteredUsers / Anyone / GroupPublic
    };
}
