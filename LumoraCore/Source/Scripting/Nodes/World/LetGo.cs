// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Input;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// One hand of the user at this machine, or both with a side of None, lets go of whatever it holds, as opening
// the grip does: thrown with the hand's motion and offered to a drop surface. Released says a hand was there
// to do it, holding anything or not, and the call fails when none was. Nobody else's hands are touched. -xlinka
[Node("World/Interaction/Let Go", Capability.UsersAffect)]
public sealed class LetGo : ActionNode
{
    [return: Port("released")]
    public static bool Run(ScriptContext ctx, Chirality side = Chirality.Right)
    {
        var user = ctx.World.LocalUser;
        if (!ctx.IsMachineUser(user) || user!.Root is not { IsDestroyed: false } root)
        {
            ctx.Fail("no one is at this machine");
            return false;
        }
        var grabbers = new List<Grabber>(4);
        var found = new List<Grabber>(2);
        foreach (var hand in side == Chirality.None ? new[] { Chirality.Left, Chirality.Right } : new[] { side })
        {
            ScriptHands.GrabbersOf(ScriptHands.Hand(root, hand), found);
            grabbers.AddRange(found);
        }
        if (grabbers.Count == 0)
        {
            ctx.Fail("no hand to let go with");
            return false;
        }
        using (ctx.AsSelf(user))
        {
            foreach (var grabber in grabbers)
                grabber.ReleaseAll();
        }
        return true;
    }
}
