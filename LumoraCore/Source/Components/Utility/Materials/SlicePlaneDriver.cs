// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Holds a cutting plane on this slot: Point gets the slot's world position and Normal the world direction of
// its local +Z, so a slice material's plane (PBS_Slice) follows a handle you can grab and turn, or a level of
// liquid that stays put while its bottle tips. Every peer computes the same plane from the same slot, so the
// drives are this machine's own and nothing streams.
//
// GrabbedVisual, when set, is shown while this slot is held and hidden when it is let go: a guide grid that
// only matters while someone is placing the plane. -xlinka
[ComponentCategory("Utility/Materials")]
public sealed class SlicePlaneDriver : Component
{
    public readonly FieldDrive<float3> Point;
    public readonly FieldDrive<float3> Normal;
    public readonly SyncRef<Slot> GrabbedVisual;

    private Grabbable? _watched;

    public SlicePlaneDriver()
    {
        Point = new FieldDrive<float3>(this) { LocalValueOnly = true };
        Normal = new FieldDrive<float3>(this) { LocalValueOnly = true };
        GrabbedVisual = new SyncRef<Slot>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        Watch();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Watch();
    }

    public override void OnDestroy()
    {
        Unwatch();
        base.OnDestroy();
    }

    public override void OnUpdate(float delta)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return;
        if (Point.IsLinkValid)
            Point.SetValue(slot.GlobalPosition);
        if (Normal.IsLinkValid)
            Normal.SetValue(slot.LocalDirectionToGlobal(new float3(0f, 0f, 1f)).Normalized);
    }

    private void Watch()
    {
        var grabbable = Slot?.GetComponent<Grabbable>();
        if (ReferenceEquals(grabbable, _watched))
            return;
        Unwatch();
        _watched = grabbable;
        if (grabbable == null)
            return;
        grabbable.OnLocalGrabbed += ShowVisual;
        grabbable.OnLocalReleased += HideVisual;
    }

    private void Unwatch()
    {
        if (_watched == null)
            return;
        _watched.OnLocalGrabbed -= ShowVisual;
        _watched.OnLocalReleased -= HideVisual;
        _watched = null;
    }

    private void ShowVisual(IGrabbable grabbable) => SetVisual(true);

    private void HideVisual(IGrabbable grabbable) => SetVisual(false);

    private void SetVisual(bool shown)
    {
        if (GrabbedVisual.Target is { IsDestroyed: false } visual && visual.ActiveSelf.Value != shown)
            visual.ActiveSelf.Value = shown;
    }
}
