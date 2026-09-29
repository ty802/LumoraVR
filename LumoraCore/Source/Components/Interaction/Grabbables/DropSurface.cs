// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// A surface things are set down on. Let go of something receivable above it and it comes to rest there:
// dropped along this slot's own down onto the first of this object's colliders below it, its lowest point
// on the surface, still turned the way it was. Reach is how far below it looks, in this slot's scale.
// With ParentDropped the thing goes under this slot as well, so it rides along when the surface moves.
//
// The surface needs a collider, on its own slot or a child: a release finds drop targets by overlapping
// a sphere around the hand. The drop happens on the machine of whoever let go, and Received tells anything
// listening there. A body keeps the push the hand gave it as it let go. -xlinka
[ComponentCategory("Interaction/Grabbables")]
public sealed class DropSurface : Component, IGrabbableReceiver
{
    private const int MaxCasts = 8;
    private const float StepPast = 0.001f;

    public readonly Sync<float> Reach;
    public readonly Sync<float> Lift;
    public readonly Sync<bool> ParentDropped;

    public event Action<IGrabbable, Grabber>? Received;

    private readonly List<Slot> _exclude = new(1);

    public DropSurface()
    {
        Reach = new Sync<float>(this, 1f);
        Lift = new Sync<float>(this, 0f);
        ParentDropped = new Sync<bool>(this, false);
    }

    public float? GetReceiveDistance(IGrabbable grabbable, Grabber grabber)
        => grabbable is Component { Slot: { IsDestroyed: false } slot } && TryLanding(slot, out var landing)
            ? float3.Distance(slot.GlobalPosition, landing)
            : null;

    public void Receive(IGrabbable grabbable, Grabber grabber)
    {
        if (grabbable is not Component { Slot: { IsDestroyed: false } slot } || !TryLanding(slot, out var landing))
            return;
        // Inside the hand's release, which offers everything it let go of in turn: a refused write ends
        // here instead of taking the rest of that release with it.
        try
        {
            if (ParentDropped.Value && !ReferenceEquals(slot.Parent, Slot) && !Slot.IsDescendantOf(slot))
                slot.SetParent(Slot, preserveGlobalTransform: true);
            slot.GlobalPosition = landing;
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"DropSurface on {ParentHierarchyToString()} could not take {slot.Name.Value}: {ex.Message}");
            return;
        }
        try
        {
            Received?.Invoke(grabbable, grabber);
        }
        catch (Exception ex)
        {
            Logging.Logger.Error($"DropSurface on {ParentHierarchyToString()}: a listener threw: {ex.Message}");
        }
    }

    // Where the slot's origin ends up once its lowest point, along this surface's down, rests on it.
    private bool TryLanding(Slot dropped, out float3 landing)
    {
        landing = default;
        var world = World;
        if (world?.Physics == null || IsDestroyed || !Enabled.Value || !Slot.IsActive
            || ReferenceEquals(dropped, Slot) || Slot.IsDescendantOf(dropped))
            return false;

        var down = Slot.LocalDirectionToGlobal(float3.Down);
        float stretch = down.Length;
        if (!(stretch > 1e-6f) || !float.IsFinite(stretch))
            return false;
        down /= stretch;
        float reach = MathF.Max(Reach.Value, 0f) * stretch;
        var origin = dropped.GlobalPosition;
        if (!(reach > 0f) || !float.IsFinite(origin.x) || !float.IsFinite(origin.y) || !float.IsFinite(origin.z))
            return false;

        _exclude.Clear();
        _exclude.Add(dropped);
        float travelled = 0f;
        for (int cast = 0; cast < MaxCasts && travelled < reach; cast++)
        {
            if (!world.Physics.Raycast(origin + down * travelled, down, reach - travelled, _exclude, out var hit, hitTriggers: true))
                return false;
            float along = travelled + hit.Distance;
            if (hit.Slot != null && (ReferenceEquals(hit.Slot, Slot) || hit.Slot.IsDescendantOf(Slot)))
            {
                float support = LowestAlong(dropped, origin, down);
                landing = origin + down * (along - support) - down * (Lift.Value * stretch);
                return true;
            }
            travelled = along + StepPast;
        }
        return false;
    }

    // How far past its origin the slot's contents reach along a direction; zero when it has no size.
    private static float LowestAlong(Slot slot, float3 origin, float3 direction)
    {
        if (!SlotBoundsHelper.TryComputeWorldBounds(slot, out var bounds) || bounds.IsEmpty)
            return 0f;
        var min = bounds.Min;
        var max = bounds.Max;
        float lowest = float.MinValue;
        for (int corner = 0; corner < 8; corner++)
        {
            var point = new float3((corner & 1) == 0 ? min.x : max.x, (corner & 2) == 0 ? min.y : max.y, (corner & 4) == 0 ? min.z : max.z);
            lowest = MathF.Max(lowest, float3.Dot(point - origin, direction));
        }
        return float.IsFinite(lowest) ? lowest : 0f;
    }
}
