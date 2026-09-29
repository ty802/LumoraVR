// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// Carrying values between a slot's own space and world space, or from one slot's space into another's, for
// the space nodes. A slot the script cannot see, or none, stands for world space.
//
// Into a slot's space is worked one level at a time, each level's position taken off, its rotation undone
// and its scale divided out, so a skewed chain comes back exact. A zero scale axis divides as one, the way
// the slot setters treat a flattened parent, so a flattened slot gives a finite answer instead of the
// identity or infinity. -xlinka
internal static class ScriptSpaces
{
    public static Slot? Space(ScriptContext ctx, Slot? slot) => ScriptReads.Visible(ctx, slot) ? slot : null;

    public static float4x4 ToWorld(Slot? space) => space?.LocalToWorld ?? float4x4.Identity;

    public static float4x4 FromWorld(Slot? space)
    {
        var matrix = float4x4.Identity;
        for (var level = space; level != null; level = level.Parent)
            matrix = matrix * Undo(level);
        return matrix;
    }

    // From one space into the other, as one matrix.
    public static float4x4 Between(Slot? from, Slot? to) => FromWorld(to) * ToWorld(from);

    public static float3 Point(Slot? from, Slot? to, float3 point)
        => ReferenceEquals(from, to) ? point : Between(from, to).MultiplyPoint(point);

    public static float3 Vector(Slot? from, Slot? to, float3 vector)
        => ReferenceEquals(from, to) ? vector : Between(from, to).MultiplyVector(vector);

    // Where a line drawn along the direction in one space points in the other, its length kept. Where a
    // flattened slot loses the line, the rotations alone carry it. A direction that is not a finite number
    // comes back as zero.
    public static float3 Direction(Slot? from, Slot? to, float3 direction)
    {
        float length = direction.Length;
        if (!(length > 0f) || !float.IsFinite(length))
            return float3.Zero;
        if (ReferenceEquals(from, to))
            return direction;
        var carried = Between(from, to).MultiplyVector(direction);
        float reach = carried.Length;
        if (reach > 1e-20f && float.IsFinite(reach))
            return carried * (length / reach);
        return Rotation(from, to, floatQ.Identity) * direction;
    }

    public static floatQ Rotation(Slot? from, Slot? to, floatQ rotation)
    {
        if (ReferenceEquals(from, to))
            return rotation;
        var world = from == null ? rotation : from.GlobalRotation * rotation;
        return to == null ? world : to.GlobalRotation.Inverse * world;
    }

    // Axis by axis, rotation left out: each slot's scale is its own times every parent's.
    public static float3 Scale(Slot? from, Slot? to, float3 scale)
    {
        if (ReferenceEquals(from, to))
            return scale;
        var world = from == null ? scale : scale * from.GlobalScale;
        if (to == null)
            return world;
        var by = to.GlobalScale;
        return new float3(Divide(world.x, by.x), Divide(world.y, by.y), Divide(world.z, by.z));
    }

    // The box around the one given, carried through: exactly the box around its eight corners. An empty box
    // stays empty.
    public static BoundingBox Bounds(Slot? from, Slot? to, BoundingBox bounds)
        => ReferenceEquals(from, to) ? bounds : bounds.Transformed(Between(from, to));

    public static float3 Axis(SlotAxis axis) => axis switch
    {
        SlotAxis.Backward => float3.Backward,
        SlotAxis.Up => float3.Up,
        SlotAxis.Down => float3.Down,
        SlotAxis.Left => float3.Left,
        SlotAxis.Right => float3.Right,
        _ => float3.Forward,
    };

    // The world rotation that points the slot's axis along a unit direction, keeping a second axis as near
    // to where current points it as it can: up for forward and backward, forward for the rest. When that one
    // lies along the direction, the third axis is kept instead.
    public static floatQ Aimed(floatQ current, SlotAxis axis, float3 direction)
    {
        var aimed = Axis(axis);
        var held = axis is SlotAxis.Forward or SlotAxis.Backward ? float3.Up : float3.Forward;
        var third = float3.Cross(aimed, held);
        var heldTo = Across(current * held, direction);
        if (heldTo.LengthSquared < 1e-12f)
            heldTo = float3.Cross(Across(current * third, direction), direction);
        if (heldTo.LengthSquared < 1e-12f)
            heldTo = Across(System.MathF.Abs(direction.y) < 0.9f ? float3.Up : float3.Forward, direction);
        var thirdTo = float3.Cross(direction, heldTo);
        var forward = float3.Dot(float3.Forward, aimed) * direction + float3.Dot(float3.Forward, held) * heldTo
            + float3.Dot(float3.Forward, third) * thirdTo;
        var up = float3.Dot(float3.Up, aimed) * direction + float3.Dot(float3.Up, held) * heldTo
            + float3.Dot(float3.Up, third) * thirdTo;
        return floatQ.LookRotation(forward, up).Inverse;
    }

    // The part of value square to a unit direction, made unit; zero when there is none.
    private static float3 Across(float3 value, float3 direction)
    {
        var across = value - float3.Dot(value, direction) * direction;
        float length = across.Length;
        return length > 1e-6f ? across / length : float3.Zero;
    }

    // One level's own position, rotation and scale, undone.
    private static float4x4 Undo(Slot level)
    {
        var scale = level.LocalScale.Value;
        var shrink = new float3(Divide(1f, scale.x), Divide(1f, scale.y), Divide(1f, scale.z));
        return float4x4.Scale(shrink) * float4x4.Rotate(level.LocalRotation.Value).Inverse * float4x4.Translate(-level.LocalPosition.Value);
    }

    private static float Divide(float value, float by) => by == 0f ? value : value / by;
}
