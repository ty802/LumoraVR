// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Moves the user at this machine so their hips are at a point, carrying the whole body along. The hips are
// where Users/Body Pose says they are. Anyone else stays where they are. A point that is not a finite
// number fails.
[Node("Users/Place Hips", Capability.UsersAffect)]
public sealed class PlaceHips : ActionNode
{
    public static void Run(ScriptContext ctx, User? user, float3 position)
    {
        if (!ScriptReads.HereFor(ctx, user))
            return;
        if (user.Root is not { IsDestroyed: false } root)
        {
            ctx.Fail("the user has no body here");
            return;
        }
        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
        {
            ctx.Fail("the position is not a finite number");
            return;
        }
        using (ctx.AsSelf(user))
            root.HipsPosition = position;
    }
}
