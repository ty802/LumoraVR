// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core;

/// <summary>
/// World-level settings as a synced datamodel component on the world's root slot. Because it rides the
/// normal component replication, settings replicate to clients, persist with the world (in the slot
/// tree), and update live - reached via <see cref="World.Configuration"/>. The authority owns it; a
/// client receives it through state sync. Edit/access policy (e.g. <see cref="Mode"/>) is still
/// enforced host-authoritatively by the permission gate, not by trusting these synced values.
/// </summary>
[ComponentCategory("World")]
public sealed class WorldSettings : Component
{
    public readonly Sync<int> MaxUsers = new();
    public readonly Sync<bool> AllowJoin = new();
    public readonly Sync<bool> IsPublic = new();
    public readonly Sync<World.WorldAccessLevel> AccessLevel = new();

    /// <summary>Edit mode (Builder / Social / Event). Baked at host; drives the permission preset.</summary>
    public readonly Sync<WorldMode> Mode = new();

    public readonly Sync<bool> MobileFriendly = new();
    public readonly Sync<bool> EditMode = new();
    public readonly Sync<bool> HideFromSessionLists = new();
    public readonly Sync<bool> AutoKickAFK = new();
    public readonly Sync<int> MaxAFKMinutes = new();
    public readonly Sync<bool> CleanupUnusedAssets = new();
    public readonly Sync<float> AssetCleanupInterval = new();
    public readonly Sync<string> Description = new();
    public readonly Sync<bool> EnablePersistence = new();
    public readonly Sync<float> AutoSaveInterval = new();
    public readonly Sync<int> MaxWorldSizeMB = new();

    /// <summary>World tags for discovery.</summary>
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
    }
}
