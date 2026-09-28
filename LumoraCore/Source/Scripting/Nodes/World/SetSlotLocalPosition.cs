// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// In the parent's space. A point that is not a finite number is a fault, not a write.
[Node("World/Slots/Set Local Position", Capability.WorldWrite)]
public sealed class SetSlotLocalPosition : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, float3 position)
    {
        if (ScriptReads.Visible(ctx, slot))
            ScriptWrites.Write(ctx, slot!.LocalPosition, ScriptWrites.Finite(position, "position"));
    }
}
