// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Variables;

// One value everywhere inside a box centred on this slot.
[ComponentCategory("Data/Variables")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class BoxValueZone<T> : ValueZone<T>
{
    // Full size, in this slot's space.
    public readonly Sync<float3> Size;

    // How far inside the box a point has to be before it counts at full weight.
    public readonly Sync<float> BlendDistance;

    public readonly Sync<ZoneBlendMode> BlendMode;

    public readonly Sync<T> Value;

    public static bool IsValidGenericType => Utility.DrivenValueTypes.IsPrimitive(typeof(T));

    public BoxValueZone()
    {
        Size = new Sync<float3>(this, float3.One);
        BlendDistance = new Sync<float>(this, 0.1f);
        BlendMode = new Sync<ZoneBlendMode>(this, ZoneBlendMode.Ratio);
        Value = new Sync<T>(this, Networking.Sync.SyncCoder.GetDefault<T>());
    }

    protected override bool SampleLocal(in float3 localPoint, out T value, out float weight)
    {
        var half = Size.Value * 0.5f;
        float hx = MathF.Abs(half.x), hy = MathF.Abs(half.y), hz = MathF.Abs(half.z);
        float px = MathF.Abs(localPoint.x), py = MathF.Abs(localPoint.y), pz = MathF.Abs(localPoint.z);
        if (px > hx || py > hy || pz > hz)
        {
            value = default!;
            weight = 0f;
            return false;
        }

        float depth;
        if (BlendMode.Value == ZoneBlendMode.Absolute)
        {
            depth = MathF.Min(hx - px, MathF.Min(hy - py, hz - pz));
        }
        else
        {
            // In a box of unit size: half the smallest share of each axis still ahead of the point.
            float rx = hx > 0f ? 1f - px / hx : 0f;
            float ry = hy > 0f ? 1f - py / hy : 0f;
            float rz = hz > 0f ? 1f - pz / hz : 0f;
            depth = 0.5f * MathF.Min(rx, MathF.Min(ry, rz));
        }
        weight = Weight(depth, BlendDistance.Value);
        value = Value.Value;
        return true;
    }
}
