// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Core.Components.Utility;

namespace Lumora.Core.Components.Variables;

// A value that runs from StartValue to EndValue with distance from this slot, inside a sphere. The same
// start and end make it constant. Distances are in this slot's space, so the sphere scales with it.
[ComponentCategory("Data/Variables")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class SphereValueZone<T> : ValueZone<T>
{
    public readonly Sync<float> Radius;

    // How far inside the edge a point has to be before it counts at full weight.
    public readonly Sync<float> BlendDistance;

    public readonly Sync<ZoneBlendMode> BlendMode;

    public readonly Sync<T> StartValue;

    public readonly Sync<T> EndValue;

    // StartValue holds at this distance and EndValue at EndDistance; either may be the nearer one.
    public readonly Sync<float> StartDistance;

    public readonly Sync<float> EndDistance;

    public readonly Sync<ZoneDistanceMode> DistanceMode;

    public static bool IsValidGenericType => ValueOps<T>.CanBlend;

    public SphereValueZone()
    {
        Radius = new Sync<float>(this, 0.5f);
        BlendDistance = new Sync<float>(this, 0f);
        BlendMode = new Sync<ZoneBlendMode>(this, ZoneBlendMode.Ratio);
        StartValue = new Sync<T>(this, Networking.Sync.SyncCoder.GetDefault<T>());
        EndValue = new Sync<T>(this, Networking.Sync.SyncCoder.GetDefault<T>());
        StartDistance = new Sync<float>(this, 1f);
        EndDistance = new Sync<float>(this, 0f);
        DistanceMode = new Sync<ZoneDistanceMode>(this, ZoneDistanceMode.Normalized);
    }

    protected override bool SampleLocal(in float3 localPoint, out T value, out float weight)
    {
        float radius = MathF.Abs(Radius.Value);
        float distance = localPoint.Length;
        if (!(distance <= radius))
        {
            value = default!;
            weight = 0f;
            return false;
        }

        float normalized = radius > 0f ? distance / radius : 0f;
        weight = BlendMode.Value == ZoneBlendMode.Absolute
            ? Weight(radius - distance, BlendDistance.Value)
            : Weight(1f - normalized, BlendDistance.Value);

        float at = DistanceMode.Value == ZoneDistanceMode.Absolute ? distance : normalized;
        float span = EndDistance.Value - StartDistance.Value;
        float t = span != 0f ? (at - StartDistance.Value) / span : float.NaN;
        if (float.IsNaN(t))
        {
            value = default!;
            return true;
        }
        t = t <= 0f ? 0f : t >= 1f ? 1f : t;
        var blend = ValueOps<T>.Blend;
        value = blend != null ? blend(StartValue.Value, EndValue.Value, t) : (t < 0.5f ? StartValue.Value : EndValue.Value);
        return true;
    }
}
