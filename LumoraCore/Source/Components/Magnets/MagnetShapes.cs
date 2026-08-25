// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Magnets;

// Convention across every shape here: a SURFACE guide (plane, sphere) hands back a rotation whose
// UP is the surface normal, because you set things down on a surface. A DIRECTIONAL guide (line,
// ring) hands back one whose FORWARD runs along the rail or out along the radius, because you line
// things up with a direction. Nothing else has to remember which is which. -xlinka

// Single fixed pose. The item lands exactly on this slot.
[ComponentCategory("Interaction/Magnets")]
public class MagnetPoint : MagnetGuide
{
    public override bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
        {
            position = worldPosition;
            rotation = worldRotation;
            return false;
        }
        position = slot.GlobalPosition;
        rotation = slot.GlobalRotation;
        return true;
    }
}

// Regular lattice inside a box. Positions quantise to the cell size and clamp to the bounds;
// rotation comes from the grid slot, so everything dropped on it lines up with everything else.
[ComponentCategory("Interaction/Magnets")]
public class MagnetGrid : MagnetGuide
{
    // Full extents of the box the lattice fills, in local space.
    public readonly Sync<float3> Bounds;

    // A zero component leaves that axis unquantised.
    public readonly Sync<float3> CellSize;

    public MagnetGrid()
    {
        Bounds = new Sync<float3>(this, float3.One);
        CellSize = new Sync<float3>(this, new float3(0.05f, 0.05f, 0.05f));
    }

    public override bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
        {
            position = worldPosition;
            rotation = worldRotation;
            return false;
        }

        var local = slot.GlobalPointToLocal(worldPosition);
        var cell = CellSize.Value;
        local = new float3(Quantise(local.x, cell.x), Quantise(local.y, cell.y), Quantise(local.z, cell.z));

        var half = Bounds.Value * 0.5f;
        local = new float3(
            System.Math.Clamp(local.x, -half.x, half.x),
            System.Math.Clamp(local.y, -half.y, half.y),
            System.Math.Clamp(local.z, -half.z, half.z));

        position = slot.LocalPointToGlobal(local);
        rotation = slot.GlobalRotation;
        return true;
    }

    private static float Quantise(float value, float step)
    {
        if (step <= 0f)
            return value;
        return MathF.Round(value / step) * step;
    }
}

// Rail between two local points. The item slides to the closest spot along it.
[ComponentCategory("Interaction/Magnets")]
public class MagnetLine : MagnetGuide
{
    // In local space.
    public readonly Sync<float3> Start;

    // In local space.
    public readonly Sync<float3> End;

    // Keep the result between the two points instead of running off along the infinite line.
    public readonly Sync<bool> ClampToSegment;

    public MagnetLine()
    {
        Start = new Sync<float3>(this, new float3(0f, 0f, -0.5f));
        End = new Sync<float3>(this, new float3(0f, 0f, 0.5f));
        ClampToSegment = new Sync<bool>(this, true);
    }

    public override bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation)
    {
        position = worldPosition;
        rotation = worldRotation;

        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return false;

        var a = Start.Value;
        var direction = End.Value - a;
        float lengthSq = direction.LengthSquared;
        if (lengthSq < 1e-10f)
            return false;

        var local = slot.GlobalPointToLocal(worldPosition);
        float t = float3.Dot(local - a, direction) / lengthSq;
        if (ClampToSegment.Value)
            t = System.Math.Clamp(t, 0f, 1f);

        position = slot.LocalPointToGlobal(a + direction * t);
        rotation = slot.GlobalRotation * MagnetHelper.Facing(direction, float3.Up);
        return true;
    }
}

// Infinite surface through this slot. The item drops onto it along the normal.
[ComponentCategory("Interaction/Magnets")]
public class MagnetPlane : MagnetGuide
{
    // In local space. Up makes this a table top.
    public readonly Sync<float3> Normal;

    public MagnetPlane()
    {
        Normal = new Sync<float3>(this, float3.Up);
    }

    public override bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation)
    {
        position = worldPosition;
        rotation = worldRotation;

        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return false;

        var normal = Normal.Value;
        if (normal.LengthSquared < 1e-10f)
            return false;
        normal = normal.Normalized;

        var local = slot.GlobalPointToLocal(worldPosition);
        position = slot.LocalPointToGlobal(local - normal * float3.Dot(local, normal));

        // Any direction lying in the plane will do for forward; take the guide's own forward and
        // flatten it, falling back to its right when the two are parallel.
        var forward = float3.Forward - normal * float3.Dot(float3.Forward, normal);
        if (forward.LengthSquared < 1e-8f)
            forward = float3.Right - normal * float3.Dot(float3.Right, normal);
        rotation = slot.GlobalRotation * MagnetHelper.Facing(forward, normal);
        return true;
    }
}

// Shell of a given radius. The item is pushed out onto the surface, standing on it.
[ComponentCategory("Interaction/Magnets")]
public class MagnetSphere : MagnetGuide
{
    // In local space.
    public readonly Sync<float> Radius;

    public MagnetSphere()
    {
        Radius = new Sync<float>(this, 0.5f);
    }

    public override bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation)
    {
        position = worldPosition;
        rotation = worldRotation;

        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return false;

        var local = slot.GlobalPointToLocal(worldPosition);
        if (local.LengthSquared < 1e-10f)
            return false;

        var outward = local.Normalized;
        position = slot.LocalPointToGlobal(outward * Radius.Value);
        rotation = slot.GlobalRotation * MagnetHelper.Facing(float3.Forward, outward);
        return true;
    }
}

// Circle of a given radius around an axis. The item swings out onto the rim.
[ComponentCategory("Interaction/Magnets")]
public class MagnetRing : MagnetGuide
{
    // In local space.
    public readonly Sync<float> Radius;

    // In local space.
    public readonly Sync<float3> Axis;

    public MagnetRing()
    {
        Radius = new Sync<float>(this, 0.5f);
        Axis = new Sync<float3>(this, float3.Up);
    }

    public override bool Constrain(in float3 worldPosition, in floatQ worldRotation, out float3 position, out floatQ rotation)
    {
        position = worldPosition;
        rotation = worldRotation;

        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return false;

        var axis = Axis.Value;
        if (axis.LengthSquared < 1e-10f)
            return false;
        axis = axis.Normalized;

        var local = slot.GlobalPointToLocal(worldPosition);
        var radial = local - axis * float3.Dot(local, axis);
        // Dead on the axis there is no angle to pick, so the ring has nothing to say.
        if (radial.LengthSquared < 1e-10f)
            return false;

        var outward = radial.Normalized;
        position = slot.LocalPointToGlobal(outward * Radius.Value);
        rotation = slot.GlobalRotation * MagnetHelper.Facing(outward, axis);
        return true;
    }
}
