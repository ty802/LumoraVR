// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// In the parent's space. A rotation that is not a finite, non-zero quaternion is a fault, not a write.
[Node("World/Slots/Set Local Rotation", Capability.WorldWrite)]
public sealed class SetSlotLocalRotation : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, floatQ rotation)
    {
        if (ScriptReads.Visible(ctx, slot))
            ScriptWrites.Write(ctx, slot!.LocalRotation, ScriptWrites.Unit(rotation));
    }
}
