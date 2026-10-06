// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Utility;

// Holds every target at the value a curve gives for where Input sits. Stops are points along Input's range
// with the value there; between two the value blends (rotations turn the short way), and a type that does
// not blend holds the stop at or before. Past either end it holds that end's stop.
//
// The curve is how a turned value moves things that are not themselves turned: one dial on an avatar moves
// a dozen fields, each along its own path, a size, a rotation, an object coming on halfway. Every peer reads
// the same replicated Input, so the result stays local, as with a switch driver.
//
// A field takes one driver, so a second dial moving the same field hands its share in through Offsets:
// each is added on top of the curve's value, or for a rotation turned on after it. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
[ComponentGenericTypes(GenericTypeGroup.Values)]
public class ValueCurveDriver<T> : Component
{
    public readonly SyncRef<IField<float>> Input;

    public readonly SyncList<GradientStop<T>> Stops;

    public readonly SyncList<FieldDrive<T>> Drives;

    public readonly SyncRefList<IField<T>> Offsets;

    public static bool IsValidGenericType => DrivenValueTypes.IsPrimitive(typeof(T));

    public ValueCurveDriver()
    {
        Input = new SyncRef<IField<float>>(this);
        Stops = new SyncList<GradientStop<T>>();
        Drives = new SyncList<FieldDrive<T>>();
        Offsets = new SyncRefList<IField<T>>(this);
    }

    public GradientStop<T> AddStop(float position, T value)
    {
        var stop = Stops.Add();
        stop.Position.Value = position;
        stop.Value.Value = value;
        return stop;
    }

    public FieldDrive<T> AddDrive(IField<T>? target)
    {
        var drive = Drives.Add();
        drive.DriveTarget(target);
        return drive;
    }

    // The type's default with no stops.
    public T Sample(float at)
    {
        GradientStop<T>? before = null;
        GradientStop<T>? after = null;
        GradientStop<T>? first = null;
        foreach (var stop in Stops.Elements)
        {
            float position = stop.Position.Value;
            if (first == null || position < first.Position.Value)
                first = stop;
            if (position <= at)
            {
                if (before == null || position > before.Position.Value)
                    before = stop;
            }
            else if (after == null || position < after.Position.Value)
            {
                after = stop;
            }
        }
        if (before == null)
            return first != null ? first.Value.Value : SyncCoder.GetDefault<T>();
        if (after == null)
            return before.Value.Value;
        var blend = ValueOps<T>.Blend;
        float span = after.Position.Value - before.Position.Value;
        if (blend == null || span <= 0f)
            return before.Value.Value;
        return blend(before.Value.Value, after.Value.Value, (at - before.Position.Value) / span);
    }

    public override void OnUpdate(float delta)
    {
        int count = Drives.Count;
        if (count == 0 || Stops.Count == 0)
            return;
        float at = Input.Target is { } input ? input.Value : 0f;
        if (float.IsNaN(at))
            at = 0f;
        T value = Sample(at);
        if (Offsets.Count > 0 && ValueOps<T>.Step is { } step)
        {
            for (int i = 0; i < Offsets.Count; i++)
            {
                if (Offsets[i] is { } offset)
                    value = step(value, offset.Value);
            }
        }
        for (int i = 0; i < count; i++)
        {
            var drive = Drives[i];
            if (drive == null)
                continue;
            drive.LocalValueOnly = true;
            drive.SetValue(value);
        }
    }
}
