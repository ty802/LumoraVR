// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Buttons")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class PressSetValue<T> : ButtonAction
{
    public readonly SyncRef<IField<T>> TargetValue;

    public readonly Sync<T> Value;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public PressSetValue()
    {
        TargetValue = new SyncRef<IField<T>>(this);
        Value = new Sync<T>(this, SyncCoder.GetDefault<T>());
    }

    protected override void Pressed(User? actor)
    {
        var target = TargetValue.Target;
        if (target != null && target.CanWrite)
            target.Value = Value.Value;
    }
}

// Advances a field to the next entry in a list when the button is pressed, wrapping at the end.
[ComponentCategory("Utility/Buttons")]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class PressCycleValue<T> : ButtonAction
{
    public readonly SyncRef<IField<T>> TargetValue;

    public readonly SyncFieldList<T> Values;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public PressCycleValue()
    {
        TargetValue = new SyncRef<IField<T>>(this);
        Values = new SyncFieldList<T>();
    }

    protected override void Pressed(User? actor)
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

[ComponentCategory("Utility/Buttons")]
public class PressToggle : ButtonAction
{
    public readonly SyncRef<IField<bool>> TargetValue;

    public PressToggle()
    {
        TargetValue = new SyncRef<IField<bool>>(this);
    }

    protected override void Pressed(User? actor)
    {
        var target = TargetValue.Target;
        if (target != null && target.CanWrite)
            target.Value = !target.Value;
    }
}

[ComponentCategory("Utility/Buttons")]
public class PressAppendText : ButtonAction
{
    public readonly SyncRef<IField<string>> TargetString;

    public readonly Sync<string> Text;

    // Add the text at the front instead of the end.
    public readonly Sync<bool> Prepend;

    public PressAppendText()
    {
        TargetString = new SyncRef<IField<string>>(this);
        Text = new Sync<string>(this, "");
        Prepend = new Sync<bool>(this, false);
    }

    protected override void Pressed(User? actor)
    {
        var target = TargetString.Target;
        if (target == null || !target.CanWrite)
            return;
        string current = target.Value ?? "";
        string addition = Text.Value ?? "";
        target.Value = Prepend.Value ? addition + current : current + addition;
    }
}

[ComponentCategory("Utility/Buttons")]
public class PressEraseText : ButtonAction
{
    public readonly SyncRef<IField<string>> TargetString;

    public readonly Sync<int> Count;

    // Remove from the front instead of the end.
    public readonly Sync<bool> FromStart;

    public PressEraseText()
    {
        TargetString = new SyncRef<IField<string>>(this);
        Count = new Sync<int>(this, 1);
        FromStart = new Sync<bool>(this, false);
    }

    protected override void Pressed(User? actor)
    {
        var target = TargetString.Target;
        if (target == null || !target.CanWrite)
            return;

        string current = target.Value ?? "";
        if (current.Length == 0)
            return;

        // Clamp the count so a backspace held past the start of the string is a no-op rather than a
        // range exception on the pressing user's peer.
        int count = LuminaMath.Clamp(Count.Value, 0, current.Length);
        target.Value = FromStart.Value ? current[count..] : current[..(current.Length - count)];
    }
}
