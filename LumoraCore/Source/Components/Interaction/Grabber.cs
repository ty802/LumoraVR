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
        world.Physics.OverlapSphere(point, radius, hits);

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
