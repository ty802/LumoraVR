// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core;

// Applied host-side when the world starts running. The Social/Event lock is a hard floor in the permission
// gate (SocialLock), so it holds against a any client AND the host - the only way to "edit" again is to
// host the world in Builder mode. The presets themselves live with the gate, below the engine; this is the
// session-tag plumbing around them plus the call that loads one.
public static class WorldModePermissions
{
    private const string ModeTagPrefix = "mode:";

    public static string ModeTag(WorldMode mode) => ModeTagPrefix + mode.ToString().ToLowerInvariant();

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

    public static void StampModeTag(List<string>? tags, WorldMode mode)
    {
        if (tags == null)
            return;
        tags.RemoveAll(t => t != null && t.StartsWith(ModeTagPrefix, StringComparison.OrdinalIgnoreCase));
        tags.Add(ModeTag(mode));
    }

    public static IReadOnlyList<DataModelPermissionRole> AssignableRoles(DataModelPermissionController p, WorldMode mode)
    {
        if (p == null)
            return System.Array.Empty<DataModelPermissionRole>();

        return p.AssignableRolesFor(mode);
    }

    public static void Apply(World world, WorldMode mode)
    {
        var p = world?.DataModelPermissions;
        if (p == null)
            return;

        p.ApplyMode(mode);
    }
}
