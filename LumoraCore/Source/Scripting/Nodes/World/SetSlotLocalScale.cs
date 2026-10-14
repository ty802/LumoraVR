// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Relative to the parent's scale. A zero axis, or one that is not a finite number, is a fault, not a
// write.
[Node("World/Slots/Set Local Scale", Capability.WorldWrite)]
public sealed class SetSlotLocalScale : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, float3 scale)
    {
        if (ScriptReads.Reached(ctx, slot))
            ScriptWrites.Write(ctx, slot!.LocalScale, ScriptWrites.Scale(scale));
    }
}
