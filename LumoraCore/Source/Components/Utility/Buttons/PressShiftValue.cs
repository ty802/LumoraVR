// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Steps a numeric field by a fixed amount when the button is pressed, held inside a range.
//
// Rotations step by composing rather than adding, and have no component range, so Min, Max and Wrap
// do nothing for a floatQ target. -xlinka
[ComponentCategory("Utility/Buttons")]
[ComponentGenericTypes(
    typeof(int), typeof(long), typeof(float), typeof(double),
    typeof(float2), typeof(float3), typeof(float4), typeof(floatQ),
    typeof(color), typeof(colorHDR))]
public class PressShiftValue<T> : ButtonAction
{
    public readonly SyncRef<IField<T>> TargetValue;

    // Negative steps down.
    public readonly Sync<T> Delta;

    public readonly Sync<T> Min;

    public readonly Sync<T> Max;

    // Come round to the other end of the range instead of stopping at it.
    public readonly Sync<bool> Wrap;

    // Whether the range limits are enforced at all.
    public readonly Sync<bool> UseRange;

    public static bool IsValidGenericType => DrivenValueTypes.SupportsStep(typeof(T));

    public PressShiftValue()
    {
        TargetValue = new SyncRef<IField<T>>(this);
        Delta = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Min = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Max = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Wrap = new Sync<bool>(this, false);
        UseRange = new Sync<bool>(this, false);
    }

    protected override void Pressed(User? actor)
    {
        var target = TargetValue.Target;
        var step = ValueOps<T>.Step;
        if (target == null || !target.CanWrite || step == null)
            return;

        var value = step(target.Value, Delta.Value);

        if (UseRange.Value)
        {
            var limit = Wrap.Value ? ValueOps<T>.Wrap : ValueOps<T>.Clamp;
            if (limit != null)
                value = limit(value, Min.Value, Max.Value);
        }

        target.Value = value;
    }
}
