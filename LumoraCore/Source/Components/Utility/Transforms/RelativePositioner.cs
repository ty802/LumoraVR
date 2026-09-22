// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Parks this slot at a fixed offset from another slot, in that slot's space.
//
// Anchoring on bounds is what makes it useful for labels and badges: "just above whatever this is",
// without hand-measuring the model. Bounds cost a walk of the reference's whole subtree, so they are
// measured on a slow clock rather than every frame - the anchor point of a thing that is not changing
// shape does not need recomputing sixty times a second. -xlinka
[ComponentCategory("Utility/Transforms")]
public class RelativePositioner : Component
{
    private const double BoundsRefreshSeconds = 0.5;

    public readonly SyncRef<Slot> Reference;

    // Where on the reference's bounds to sit, per axis, from -1 at the low face through 0 at the centre
    // to 1 at the high face. Ignored while UseBounds is off.
    public readonly Sync<float3> Anchor;

    // Added after the anchor, in the reference's space.
    public readonly Sync<float3> Offset;

    public readonly Sync<bool> UseBounds;

    protected readonly FieldDrive<float3> _position;

    private BoundingBox _bounds;
    private bool _hasBounds;
    private double _nextMeasure;

    public RelativePositioner()
    {
        Reference = new SyncRef<Slot>(this);
        Anchor = new Sync<float3>(this, float3.Zero);
        Offset = new Sync<float3>(this, float3.Zero);
        UseBounds = new Sync<bool>(this, false);
        _position = new FieldDrive<float3>(this) { LocalValueOnly = true };
    }

    public override void OnStart()
    {
        base.OnStart();
        if (_position.ShouldApplyDefault)
            _position.DriveTarget(Slot.LocalPosition);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        // A retargeted reference must not keep the old model's box for half a second.
        _hasBounds = false;
        _nextMeasure = 0d;
    }

    public override void OnUpdate(float delta)
    {
        if (!_position.IsLinkValid)
            return;

        var reference = Reference.Target;
        var slot = Slot;
        if (reference == null || reference.IsDestroyed || slot == null)
            return;

        var local = Offset.Value;
        if (UseBounds.Value)
        {
            RefreshBounds(reference);
            if (_hasBounds)
            {
                var extents = _bounds.Size * 0.5f;
                var anchor = Anchor.Value;
                local += _bounds.Center + new float3(
                    extents.x * anchor.x,
                    extents.y * anchor.y,
                    extents.z * anchor.z);
            }
        }

        var global = reference.LocalPointToGlobal(local);
        var parent = slot.Parent;
        _position.SetValue(parent != null ? parent.GlobalPointToLocal(global) : global);
    }

    private void RefreshBounds(Slot reference)
    {
        double now = UtilityClock.Seconds(World);
        if (_hasBounds && now < _nextMeasure)
            return;
        _nextMeasure = now + BoundsRefreshSeconds;
        _hasBounds = SlotBoundsHelper.TryComputeLocalBounds(reference, out _bounds);
    }
}
