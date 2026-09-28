// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Moves a slot to a world-space point, written as its local position like any other move. A point that
// is not a finite number is a fault, not a write.
[Node("World/Slots/Set Position", Capability.WorldWrite)]
public sealed class SetSlotPosition : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, float3 position)
    {
        if (!ScriptReads.Visible(ctx, slot))
            return;
        ScriptWrites.Finite(position, "position");
        var parent = slot!.Parent;
        ScriptWrites.Write(ctx, slot.LocalPosition, parent == null ? position : parent.GlobalPointToLocal(position));
    }
}
