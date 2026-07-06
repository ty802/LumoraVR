// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// owns a holder slot and a list of currently-grabbed IGrabbables. one per hand.
// candidate selection (sphere/raycast) is driven externally by InteractionLaser. - xlinka
// TODO - xlinka: externally-held items
[ComponentCategory("Interaction")]
public class Grabber : Component
{
    private readonly List<IGrabbable> _grabbed = new();
    private Slot? _holderSlot;

    // How far around the hand to look for a drop target when letting go. - xlinka
    private const float ReleaseCheckRadius = 1.5f;

    // lazy: create a child "Holder" slot the first time something is grabbed. - xlinka
    public Slot? HolderSlot
    {
        get
        {
            if (_holderSlot != null && !_holderSlot.IsRemoved) return _holderSlot;
            if (Slot == null || Slot.IsRemoved) return null;
            _holderSlot = Slot.AddSlot("Holder");
            return _holderSlot;
        }
    }

    // The user whose hand this grabber belongs to (via the slot's active user root). Used by
    // Grabbable.CanGrab to tell "stealing from myself" from "stealing from someone else". - xlinka
    public User? OwningUser => Slot?.ActiveUser;

    public IReadOnlyList<IGrabbable> GrabbedObjects
    {
        get { CleanupGrabbed(); return _grabbed; }
    }

    public bool IsHoldingObjects
    {
        get { CleanupGrabbed(); return _grabbed.Count > 0; }
    }

    public bool TryGrab(IGrabbable target)
    {
        if (target == null || !target.CanGrab(this)) return false;

        var holder = HolderSlot;
        if (holder == null) return false;

        var grabbed = target.Grab(this, holder);
        if (grabbed == null) return false;

        // Only record it if we actually came away holding it. On a client whose contested grab the host
        // rejects, the holder ref points elsewhere and CleanupGrabbed would drop it anyway - skip the
        // round trip and don't claim it. - xlinka
        bool held = ReferenceEquals(grabbed.Grabber, this);
        if (held && !_grabbed.Contains(grabbed)) _grabbed.Add(grabbed);
        return held;
    }

    // Touch / proximity grab: sphere-overlap at 'point' and grab the best grabbable found in the overlapped
    // colliders' parents (highest GrabPriority, nearest on a tie). Unlike the laser path this DOES accept
    // AllowOnlyPhysicalGrab objects - that flag means "only reachable by hand". Arbitration still runs
    // through CanGrab/TryGrab (host authority); 'grabbed' is set only when the hold is actually recorded. - xlinka
    public bool TryGrabNearby(float3 point, float radius, out IGrabbable? grabbed)
    {
        grabbed = null;
        var world = World;
        if (world?.Physics == null) return false;

        var hits = new List<Slot>();
        // Grabbables are sensor (Trigger/Area3D) colliders on this platform, so the overlap must include
        // triggers or it would never find anything to grab. - xlinka
        world.Physics.OverlapSphere(point, radius, hits, hitTriggers: true);
        if (hits.Count == 0) return false;

        IGrabbable? best = null;
        int bestPriority = int.MinValue;
        float bestDistSq = float.MaxValue;

        foreach (var slot in hits)
        {
            var candidate = FindGrabbableInParents(slot);
            if (candidate == null || !candidate.CanGrab(this)) continue;

            // Never grab our own hand rig.
            if (candidate is Component cc && cc.Slot != null && cc.Slot.IsDescendantOf(Slot)) continue;

            float distSq = (slot.GlobalPosition - point).LengthSquared;
            if (candidate.GrabPriority > bestPriority ||
                (candidate.GrabPriority == bestPriority && distSq < bestDistSq))
            {
                best = candidate;
                bestPriority = candidate.GrabPriority;
                bestDistSq = distSq;
            }
        }

        if (best == null || !TryGrab(best)) return false;
        grabbed = best;
        return true;
    }

    // Walk up from a hit slot for the first grabbable this grabber may take. Stops at a SearchBlock (a
    // boundary another rig owns), except on the originating slot. Mirrors HandTool.FindBestGrabbable but
    // does NOT exclude AllowOnlyPhysicalGrab (touch is the physical path). - xlinka
    private IGrabbable? FindGrabbableInParents(Slot? hitSlot)
    {
        var current = hitSlot;
        while (current != null && !current.IsRemoved)
        {
            if (!ReferenceEquals(current, hitSlot) && current.GetComponent<SearchBlock>() != null)
                break;

            foreach (var g in current.GetComponentsImplementing<IGrabbable>())
            {
                if (g.CanGrab(this)) return g;
            }

            current = current.Parent;
        }
        return null;
    }

    // Called by a Grabbable when it notices the host gave it to a different holder - drop our claim so
    // we stop driving its transform. - xlinka
    internal void NotifyStolen(IGrabbable target)
    {
        _grabbed.Remove(target);
    }

    // The replicated holder ref is the source of truth. Drop any local entry whose holder is no longer
    // us (stolen, host-rejected, or released elsewhere) or that's gone. - xlinka
    private void CleanupGrabbed()
    {
        for (int i = _grabbed.Count - 1; i >= 0; i--)
        {
            var g = _grabbed[i];
            if (g == null || (g is Component c && c.IsDestroyed) || !ReferenceEquals(g.Grabber, this))
                _grabbed.RemoveAt(i);
        }
    }

    public void Release(IGrabbable target)
    {
        if (target == null) return;
        if (!_grabbed.Remove(target)) return;
        target.Release(this);
    }

    public void ReleaseAll()
    {
        // Snapshot what we're letting go so the receivable ones can be offered to a drop target after
        // they've been released back to the world. - xlinka
        var released = new List<IGrabbable>(_grabbed);
        for (int i = _grabbed.Count - 1; i >= 0; i--)
        {
            _grabbed[i].Release(this);
        }
        _grabbed.Clear();
        InformOfReleasedObjects(released);
        ResetHolderTransform();
    }

    // After a full release, hand each receivable object to the closest nearby receiver that will take
    // it. Runs locally on the releasing peer (the receiver's Receive() owns any replicated state). - xlinka
    private void InformOfReleasedObjects(List<IGrabbable> objects)
    {
        // Only receivable objects are eligible for a drop target. - xlinka
        objects.RemoveAll(g => g == null || !g.Receivable);
        if (objects.Count == 0) return;

        var holder = _holderSlot;
        if (holder == null || holder.IsRemoved) return;

        var world = World;
        if (world?.Physics == null) return;

        float3 point = holder.GlobalPosition;
        float radius = ReleaseCheckRadius;

        // Guard against a garbage sphere (NaN/Inf position, absurd radius). - xlinka
        if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z)
            || float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z)
            || float.IsNaN(radius) || float.IsInfinity(radius) || radius >= 100000f)
            return;

        var hits = new List<Slot>();
        // Receiver surfaces ride on grabbable/sensor slots (Trigger/Area3D), so include triggers here too
        // or the drop-into-receiver overlap finds nothing. - xlinka
        world.Physics.OverlapSphere(point, radius, hits, hitTriggers: true);

        // Collect enabled, active receivers in the parents of each overlapped slot, deduped. - xlinka
        var receivers = new HashSet<IGrabbableReceiver>();
        foreach (var slot in hits)
        {
            var recv = FindReceiverInParents(slot);
            if (recv != null) receivers.Add(recv);
        }
        if (receivers.Count == 0) return;

        // For each released object, hand it to the closest receiver that will take it. - xlinka
        foreach (var obj in objects)
        {
            float best = float.MaxValue;
            IGrabbableReceiver? winner = null;
            foreach (var recv in receivers)
            {
                var d = recv.GetReceiveDistance(obj, this);
                if (d.HasValue && d.Value < best) { best = d.Value; winner = recv; }
            }
            winner?.Receive(obj, this);
        }
    }

    // Walk up from the slot for the nearest enabled receiver whose slot is active. Uses the
    // interface-aware component lookup per slot. - xlinka
    private static IGrabbableReceiver? FindReceiverInParents(Slot? slot)
    {
        while (slot != null && !slot.IsRemoved)
        {
            if (slot.IsActive)
            {
                foreach (var recv in slot.GetComponentsImplementing<IGrabbableReceiver>())
                {
                    if (recv is Component c && c.Enabled.Value && !c.IsDestroyed)
                        return recv;
                }
            }
            slot = slot.Parent;
        }
        return null;
    }

    private void ResetHolderTransform()
    {
        if (_holderSlot == null || _holderSlot.IsRemoved) return;

        _holderSlot.LocalPosition.Value = float3.Zero;
        _holderSlot.LocalRotation.Value = floatQ.Identity;
        _holderSlot.LocalScale.Value = float3.One;
    }

    public override void OnDestroy()
    {
        ReleaseAll();
        base.OnDestroy();
    }
}
