// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Turns the user at this machine so their hips face a rotation, the whole body turning about where it stands.
// The hips face where Users/Body Pose says they do. Anyone else stays as they are.
[Node("Users/Turn Hips", Capability.UsersAffect)]
public sealed class TurnHips : ActionNode
{
    public static void Run(ScriptContext ctx, User? user, floatQ rotation)
    {
        if (!ScriptReads.HereFor(ctx, user))
            return;
        if (user.Root is not { IsDestroyed: false } root)
        {
            ctx.Fail("the user has no body here");
            return;
        }
        using (ctx.AsSelf(user))
            root.TurnBy(rotation.Normalized * root.HipsRotation.Inverse);
    }
}
