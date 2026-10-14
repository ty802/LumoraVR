// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Turns a slot to a world-space rotation. A rotation that is not a finite, non-zero quaternion is a
// fault, not a write.
[Node("World/Slots/Set Rotation", Capability.WorldWrite)]
public sealed class SetSlotRotation : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, floatQ rotation)
    {
        if (!ScriptReads.Reached(ctx, slot))
            return;
        var unit = ScriptWrites.Unit(rotation);
        var parent = slot!.Parent;
        ScriptWrites.Write(ctx, slot.LocalRotation, parent == null ? unit : parent.GlobalRotation.Inverse * unit);
    }
}
