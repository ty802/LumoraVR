// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Places a slot in world space by a matrix, decomposed into a world position, rotation and scale and
// written as local ones exactly the way Set Position, Set Rotation and Set Scale write a world value.
// All three are checked before any is written, so a refusal changes nothing. A matrix with shear or a
// projective bottom row is placed by the parts Decompose reads from it. One that flattens an axis, or
// holds a number that is not finite, is a fault, not a write.
[Node("World/Slots/Set World Matrix", Capability.WorldWrite)]
public sealed class SetSlotWorldMatrix : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, float4x4 matrix)
    {
        if (!ScriptReads.Visible(ctx, slot))
            return;
        matrix.Decompose(out var position, out var rotation, out var scale);
        ScriptWrites.Finite(position, "position");
        var unit = ScriptWrites.Unit(rotation);
        ScriptWrites.Scale(scale);

        var parent = slot!.Parent;
        var localPosition = parent == null ? position : parent.GlobalPointToLocal(position);
        var localRotation = parent == null ? unit : parent.GlobalRotation.Inverse * unit;
        var localScale = ScriptWrites.Scale(parent == null ? scale : parent.GlobalScaleToLocal(scale));

        ScriptWrites.Check(ctx, slot.LocalPosition);
        ScriptWrites.Check(ctx, slot.LocalRotation);
        ScriptWrites.Check(ctx, slot.LocalScale);
        ScriptWrites.Write(ctx, slot.LocalPosition, localPosition);
        ScriptWrites.Write(ctx, slot.LocalRotation, localRotation);
        ScriptWrites.Write(ctx, slot.LocalScale, localScale);
    }
}
