// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Sizes a slot to a world-space scale, written as its local scale. A zero axis, or one that is not a
// finite number, is a fault, not a write.
[Node("World/Slots/Set Scale", Capability.WorldWrite)]
public sealed class SetSlotScale : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, float3 scale)
    {
        if (!ScriptReads.Visible(ctx, slot))
            return;
        ScriptWrites.Scale(scale);
        var parent = slot!.Parent;
        ScriptWrites.Write(ctx, slot.LocalScale, ScriptWrites.Scale(parent == null ? scale : parent.GlobalScaleToLocal(scale)));
    }
}
