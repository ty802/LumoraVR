// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Gizmos;

// Pad that drags ONE numeric field along an axis: a collider radius, a box size component, a light
// range, a camera clip plane. Sliding the pad out one metre along its axis adds one metre of extent,
// so the number tracks the hand instead of being scaled by some ratio.
//
// Everything about the drag session comes from TransformHandle - the laser ray, the
// grip and tool-primary paths, the analytic pick box, winning the hit over whatever geometry the pad
// is buried in. The only two things replaced are what the drag WRITES (a field, not a pose) and what
// it records (a field edit, not a transform batch), and both are one override each.
//
// The value is delta-driven, not absolute: grabbing the pad anywhere still leaves the field where it
// was, and a pad that is a couple of centimetres off the true extent does not snap the object the
// moment it is touched. DragScale carries the world-units-per-value factor the owning
// gizmo computed, which is where the target's scale is accounted for - a collider on a slot scaled to
// a tenth has to edit ten times slower than the hand moves, or the shape runs away from the pad.
// -xlinka
public class ExtentHandle : TransformHandle
{
    public readonly SyncRef<IField<float>> FloatField;

    public readonly SyncRef<IField<float3>> VectorField;

    // 0=x, 1=y, 2=z
    public readonly Sync<int> VectorAxis;

    // before scale; a radius is 1, a box half-extent 0.5
    public readonly Sync<float> DistancePerUnit;

    // scale included; set by the gizmo when it places the pad
    public readonly Sync<float> DragScale;

    // extents that would invert a shape are refused rather than passed to a renderer that has to
    // guess what a negative radius means
    public readonly Sync<float> MinValue;

    public readonly Sync<float> MaxValue;

    // shown on the undo entry
    public readonly Sync<string> ValueName;

    private float3 _axis;
    private float3 _lineOrigin;
    private float _startParam;
    private float _startValue;
    private bool _valid;

    private IField? _undoField;
    private object? _undoBefore;

    public ExtentHandle()
    {
        FloatField = new SyncRef<IField<float>>(this);
        VectorField = new SyncRef<IField<float3>>(this);
        VectorAxis = new Sync<int>(this, 0);
        DistancePerUnit = new Sync<float>(this, 1f);
        DragScale = new Sync<float>(this, 1f);
        MinValue = new Sync<float>(this, 0f);
        MaxValue = new Sync<float>(this, float.MaxValue);
        ValueName = new Sync<string>(this, "Extent");
    }

    protected override string DragDescription => $"Edit {ValueName.Value}";

    private IField? DrivenField
    {
        get
        {
            if (FloatField.Target is { IsDestroyed: false } scalar)
                return scalar;
            if (VectorField.Target is { IsDestroyed: false } vector)
                return vector;
            return null;
        }
    }

    public float Value
    {
        get
        {
            if (FloatField.Target is { IsDestroyed: false } scalar)
                return scalar.Value;
            if (VectorField.Target is { IsDestroyed: false } vector)
            {
                var v = vector.Value;
                return VectorAxis.Value switch { 0 => v.x, 1 => v.y, _ => v.z };
            }
            return 0f;
        }
        set
        {
            // Clamp throws when the bounds cross, and they can: the camera pads clamp against each
            // OTHER, so a far plane dragged onto the near one would put the limits the wrong way round
            // for one frame. Losing an edit is fine there; throwing out of a drag is not.
            float low = MinValue.Value;
            float high = MathF.Max(MaxValue.Value, low);
            float clamped = System.Math.Clamp(value, low, high);
            if (FloatField.Target is { IsDestroyed: false } scalar)
            {
                if (scalar.CanWrite)
                    scalar.Value = clamped;
                return;
            }
            if (VectorField.Target is not { IsDestroyed: false } vector || !vector.CanWrite)
                return;
            var v = vector.Value;
            switch (VectorAxis.Value)
            {
                case 0: v.x = clamped; break;
                case 1: v.y = clamped; break;
                default: v.z = clamped; break;
            }
            vector.Value = v;
        }
    }

    protected override void BeginDrag(Slot target, float3 rayOrigin, float3 rayDirection)
    {
        _axis = AxisWorld;
        // Frozen at grab: the axis line must not chase the shape the drag is resizing.
        _lineOrigin = CenterWorld;
        _startValue = Value;
        _startParam = ClosestLineParam(_lineOrigin, _axis, rayOrigin, rayDirection);
        _valid = !float.IsNaN(_startParam) && DrivenField != null;
    }

    protected override void UpdateDrag(Slot target, float3 rayOrigin, float3 rayDirection)
    {
        if (!_valid)
            return;
        float param = ClosestLineParam(_lineOrigin, _axis, rayOrigin, rayDirection);
        if (float.IsNaN(param))
            return; // beam swung parallel to the axis this frame - hold the value

        float scale = DragScale.Value;
        if (MathF.Abs(scale) < 1e-5f)
            return;
        Value = _startValue + (param - _startParam) / scale;
    }

    // A pose batch would record nothing here (the target slot never moves), so the drag opens a field
    // record instead and lands as one entry from grip down to grip up.
    protected override void OpenUndo(Slot target)
    {
        _undoField = DrivenField;
        _undoBefore = _undoField?.BoxedValue;
    }

    protected override void CloseUndo()
    {
        var field = _undoField;
        _undoField = null;
        var before = _undoBefore;
        _undoBefore = null;

        if (field is not SyncElement { IsDestroyed: false })
            return;
        var after = field.BoxedValue;
        if (Equals(before, after))
            return; // a touch that moved nothing must not land in the history
        InspectorUndo.RecordEdit(this, field, before, after);
    }
}
