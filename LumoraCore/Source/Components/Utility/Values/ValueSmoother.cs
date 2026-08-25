// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Drives a field so it eases toward a target value instead of snapping to it.
//
// Update order is deliberately POSITIVE: a smoother has to run after whatever produced the value it is
// chasing, or it spends every frame easing toward last frame's target and lags by one frame forever.
//
// The blend factor is exp-based rather than a raw delta*speed lerp, so the same Speed converges at the
// same real rate at 30fps and at 144fps. A plain lerp is frame-rate dependent and visibly stiffer on a
// fast machine. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(100)]
[ComponentGenericTypes(
    typeof(float), typeof(double), typeof(int), typeof(long),
    typeof(float2), typeof(float3), typeof(float4), typeof(floatQ),
    typeof(color), typeof(colorHDR))]
public class ValueSmoother<T> : Component
{
    public readonly Sync<T> TargetValue;

    // Higher is snappier, zero holds still.
    public readonly Sync<float> Speed;

    public readonly FieldDrive<T> Value;

    public static bool IsValidGenericType => DrivenValueTypes.SupportsBlend(typeof(T));

    public ValueSmoother()
    {
        TargetValue = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Speed = new Sync<float>(this, 8f);
        Value = new FieldDrive<T>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        var blend = ValueOps<T>.Blend;
        if (blend == null || !Value.IsLinkValid)
            return;

        float speed = Speed.Value;
        if (speed <= 0f || delta <= 0f)
            return;

        // Read the current value back out of the driven field. Keeping a private copy instead would go
        // stale the moment anything else wrote the field (a reparent, a load, a hand edit) and the
        // smoother would yank it back from a position nothing is at any more.
        float factor = 1f - MathF.Exp(-speed * delta);
        Value.SetValue(blend(Value.DrivenValue, TargetValue.Value, factor));
    }
}
