// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Scripting;

// What scripts read about the session and the world's settings. The settings are the world root's
// WorldSettings as they stand, never made here: a machine they have not reached yet reads nothing, and
// each node answers its type's default. Writing any of it stays with the Session dashboard.
//
// The join link is the one address a script could hand out that points at a machine: the session's
// addresses carry the host's own network addresses. So it goes only to the world's own scripts and the
// host's, and only for a session others may join. -xlinka
internal static class ScriptSessionReads
{
    public static WorldSettings? Settings(ScriptContext ctx)
        => ctx.World.RootSlot?.GetComponent<WorldSettings>() is { IsDestroyed: false } settings ? settings : null;

    // Users whose machine has this world in front of them right now, the host included.
    public static int Present(ScriptContext ctx)
    {
        int count = 0;
        foreach (var user in ScriptReads.Users(ctx))
        {
            if (user.IsPresent.Value)
                count++;
        }
        return count;
    }

    public static Uri? JoinLink(ScriptContext ctx)
    {
        var world = ctx.World;
        if (!world.IsAuthority || ctx.IsLocal)
            return null;
        if (!ctx.Principal.IsWorldContent && ctx.Principal.Author?.IsHost != true)
            return null;
        if (Settings(ctx) is not { } settings || settings.AccessLevel.Value == World.WorldAccessLevel.Private)
            return null;
        var urls = world.SessionURLs;
        return urls.Count > 0 ? urls[0] : null;
    }
}
