// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Turns a slot so one of its own axes points along a world direction. Forward and backward keep its up as
// near to where it pointed as they can, the other four keep its forward; when the kept axis lies along the
// direction, the third axis is kept instead. Written as its local rotation like any other turn. A zero
// direction changes nothing and fails, and one that is not a finite number is a fault, not a write.
[Node("World/Slots/Set Direction", Capability.WorldWrite)]
public sealed class SetSlotDirection : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, SlotAxis axis, float3 direction)
    {
        if (!ScriptReads.Reached(ctx, slot))
            return;
        ScriptWrites.Finite(direction, "direction");
        float largest = MathF.Max(MathF.Abs(direction.x), MathF.Max(MathF.Abs(direction.y), MathF.Abs(direction.z)));
        if (largest == 0f)
        {
            ctx.Fail("a zero direction points nowhere");
            return;
        }
        var scaled = direction / largest;
        var rotation = ScriptSpaces.Aimed(slot!.GlobalRotation, axis, scaled / scaled.Length);
        var parent = slot.Parent;
        ScriptWrites.Write(ctx, slot.LocalRotation, ScriptWrites.Unit(parent == null ? rotation : parent.GlobalRotation.Inverse * rotation));
    }
}
