// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Avatar;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Sits the user at this machine in a seat, the way touching it would. Says whether they sat: a seat that is
// taken, one they may not use, or anyone else is a no. Each of those fails as well, except anyone else for a
// Local script, whose copy on that person's machine sits them.
[Node("Avatars/Seats/Sit In Seat", Capability.UsersAffect)]
public sealed class SitInSeat : ActionNode
{
    [return: Port("sat")]
    public static bool Run(ScriptContext ctx, Slot? seat, User? user)
    {
        if (!ScriptReads.HereFor(ctx, user) || !ScriptReads.Reached(ctx, seat, "seat"))
            return false;
        if (seat.GetComponent<Seat>() is not { IsDestroyed: false } chair)
        {
            ctx.Fail($"{ScriptReads.Named(seat)} is not a seat");
            return false;
        }
        bool sat;
        using (ctx.AsSelf(user))
            sat = chair.TrySit(user);
        if (!sat)
            ctx.Fail($"{ScriptReads.Named(seat)} would not take them");
        return sat;
    }
}
