// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Touch;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Interaction/Touch")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class TouchSetValue<T> : TouchResponder
{
    public readonly SyncRef<IField<T>> TargetValue;

    public readonly Sync<T> Value;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public TouchSetValue()
    {
        TargetValue = new SyncRef<IField<T>>(this);
        Value = new Sync<T>(this, SyncCoder.GetDefault<T>());
    }

    protected override void Fire(TouchContact contact, User? actor)
    {
        var target = TargetValue.Target;
        if (target != null && target.CanWrite)
            target.Value = Value.Value;
    }
}

// Advances a field to the next entry in a list when the control is worked, wrapping at the end.
[ComponentCategory("Interaction/Touch")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class TouchCycleValue<T> : TouchResponder
{
    public readonly SyncRef<IField<T>> TargetValue;

    public readonly SyncFieldList<T> Values;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public TouchCycleValue()
    {
        TargetValue = new SyncRef<IField<T>>(this);
        Values = new SyncFieldList<T>();
    }

    protected override void Fire(TouchContact contact, User? actor)
    {
        var target = TargetValue.Target;
        int count = Values.Count;
        if (target == null || !target.CanWrite || count == 0)
            return;

        // A value that is not in the list starts the cycle at the first entry, rather than being
        // treated as index -1 and jumping to the last.
        int index = Values.IndexOf(target.Value);
        index = index < 0 ? 0 : SelectionIndex.Wrap(index + 1, count);
        target.Value = Values[index];
    }
}

// Steps a numeric field by a fixed amount when the control is worked, held inside a range.
//
// Rotations step by composing rather than adding, and have no component range, so Min, Max and Wrap
// do nothing for a floatQ target. -xlinka
[ComponentCategory("Interaction/Touch")]
[ComponentGenericTypes(
    typeof(int), typeof(long), typeof(float), typeof(double),
    typeof(float2), typeof(float3), typeof(float4), typeof(floatQ),
    typeof(color), typeof(colorHDR))]
public class TouchStepValue<T> : TouchResponder
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

    public TouchStepValue()
    {
        TargetValue = new SyncRef<IField<T>>(this);
        Delta = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Min = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Max = new Sync<T>(this, SyncCoder.GetDefault<T>());
        Wrap = new Sync<bool>(this, false);
        UseRange = new Sync<bool>(this, false);
    }

    protected override void Fire(TouchContact contact, User? actor)
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

[ComponentCategory("Interaction/Touch")]
public class TouchToggleBool : TouchResponder
{
    public readonly SyncRef<IField<bool>> TargetValue;

    public TouchToggleBool()
    {
        TargetValue = new SyncRef<IField<bool>>(this);
    }

    protected override void Fire(TouchContact contact, User? actor)
    {
        var target = TargetValue.Target;
        if (target != null && target.CanWrite)
            target.Value = !target.Value;
    }
}
