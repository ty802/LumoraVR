// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core;

/// <summary>
/// Configures a world's <see cref="DataModelPermissionController"/> for a <see cref="WorldMode"/>.
/// Applied host-side when the world starts running. The Social/Event lock is a hard floor in the
/// permission gate (<see cref="DataModelPermissionController.SocialLock"/>), so it holds against a
/// any client AND the host - the only way to "edit" again is to host the world in Builder mode.
/// </summary>
public static class WorldModePermissions
{
    private const string ModeTagPrefix = "mode:";

    /// <summary>Session-listing tag advertising a world's mode (e.g. "mode:social"), for the browser.</summary>
    public static string ModeTag(WorldMode mode) => ModeTagPrefix + mode.ToString().ToLowerInvariant();

    /// <summary>Read the world mode out of a session's tags; defaults to Builder if none/unparseable.</summary>
    public static WorldMode ParseMode(IEnumerable<string>? tags)
    {
        if (tags != null)
        {
            foreach (var tag in tags)
            {
                if (tag != null && tag.StartsWith(ModeTagPrefix, StringComparison.OrdinalIgnoreCase)
                    && Enum.TryParse<WorldMode>(tag.Substring(ModeTagPrefix.Length), ignoreCase: true, out var mode))
                    return mode;
            }
        }
        return WorldMode.Builder;
    }

    /// <summary>Stamp a session's tag list with the world's mode, replacing any prior mode tag.</summary>
    public static void StampModeTag(List<string>? tags, WorldMode mode)
    {
        if (tags == null)
            return;
        tags.RemoveAll(t => t != null && t.StartsWith(ModeTagPrefix, StringComparison.OrdinalIgnoreCase));
        tags.Add(ModeTag(mode));
    }

    /// <summary>Roles a host may assign to users in the given mode (the per-mode role set).</summary>
    public static IReadOnlyList<DataModelPermissionRole> AssignableRoles(DataModelPermissionController p, WorldMode mode)
    {
        if (p == null)
            return System.Array.Empty<DataModelPermissionRole>();

        // Social + Event are view/interact spaces: only moderation, normal user, and spectator make
        // sense - no Builder/Admin (there is nothing to build).
        if (mode == WorldMode.Social || mode == WorldMode.Event)
            return new[] { p.ModeratorRole, p.GuestRole, p.SpectatorRole };

        return p.AssignableRoles;
    }

    public static void Apply(World world, WorldMode mode)
    {
        var p = world?.DataModelPermissions;
        if (p == null)
            return;

        switch (mode)
        {
            case WorldMode.Builder:
                p.SocialLock = false;
                p.SetDefaultRole(DataModelAccessClass.Anonymous, p.SpectatorRole);
                p.SetDefaultRole(DataModelAccessClass.Visitor, p.BuilderRole);
                p.SetDefaultRole(DataModelAccessClass.Contact, p.BuilderRole);
                break;

            case WorldMode.Social:
                // Frozen world; users may still bring/handle their own items (Guest = "User": own
                // objects fully editable, the world view-only). The SocialLock floor denies any edit
                // of the authored world for everyone, host included.
                p.SocialLock = true;
                p.SetDefaultRole(DataModelAccessClass.Anonymous, p.SpectatorRole);
                p.SetDefaultRole(DataModelAccessClass.Visitor, p.GuestRole);
                p.SetDefaultRole(DataModelAccessClass.Contact, p.GuestRole);
                break;

            case WorldMode.Event:
                // Strictest: view + interact only, no spawning even of your own items.
                p.SocialLock = true;
                p.SetDefaultRole(DataModelAccessClass.Anonymous, p.SpectatorRole);
                p.SetDefaultRole(DataModelAccessClass.Visitor, p.SpectatorRole);
                p.SetDefaultRole(DataModelAccessClass.Contact, p.SpectatorRole);
                break;
        }
    }
}
