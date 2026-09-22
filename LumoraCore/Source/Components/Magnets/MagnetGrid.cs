// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Magnets;

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
