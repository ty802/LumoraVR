// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Avatar;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Stands the user at this machine up out of a seat they sit in. Says whether they stood: an empty seat, or
// someone else in it, is a no, and no failure, since every machine runs the flow for its own person. No
// seat, or one that would not let them go, fails.
[Node("Avatars/Seats/Leave Seat", Capability.UsersAffect)]
public sealed class LeaveSeat : ActionNode
{
    [return: Port("released")]
    public static bool Run(ScriptContext ctx, Slot? seat)
    {
        var me = ctx.World.LocalUser;
        if (!ctx.IsMachineUser(me))
        {
            ctx.Fail("no one is at this machine");
            return false;
        }
        if (!ScriptReads.Reached(ctx, seat, "seat"))
            return false;
        if (seat.GetComponent<Seat>() is not { IsDestroyed: false } chair)
        {
            ctx.Fail($"{ScriptReads.Named(seat)} is not a seat");
            return false;
        }
        if (!chair.IsLocalUserSeated)
            return false;
        bool released;
        using (ctx.AsSelf(me!))
            released = chair.Release();
        if (!released)
            ctx.Fail($"{ScriptReads.Named(seat)} would not let them go");
        return released;
    }
}
