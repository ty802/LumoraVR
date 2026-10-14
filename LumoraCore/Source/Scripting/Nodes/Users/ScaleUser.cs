// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Eases the user at this machine to a scale over some seconds, feet held where they stand, inside what their
// role allows. Anyone else is not this script's to resize: nothing happens.
[Node("Users/Scale User", Capability.UsersAffect)]
public sealed class ScaleUser : ActionNode
{
    public static void Run(ScriptContext ctx, User? user, float scale = 1f, float seconds = 0f)
    {
        if (!ScriptReads.HereFor(ctx, user))
            return;
        if (user.Root is not { IsDestroyed: false } root)
        {
            ctx.Fail("the user has no body here");
            return;
        }
        using (ctx.AsSelf(user))
            root.ScaleTo(scale, seconds);
    }
}
