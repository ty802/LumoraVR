// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
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

    // TWO-HAND SCALING: second grip on an object this user already holds stretches it instead of
    // stealing it. Local state - only the resulting LocalScale writes replicate. - xlinka
    private const float MinScaleGrabDistance = 0.05f; // hands nearly touching at start would explode the ratio
    private const float MinScaleFactor = 0.02f;
    private const float MaxScaleFactor = 50f;

    private IGrabbable? _scaleTarget;
    private Grabber? _scalePartner;
    private float _scaleStartDistance;
    private float3 _scaleStartScale;
    private SlotTransformUndoBatch? _scaleUndo;

    // THROW HAND-OFF: while something is held we keep a short rolling window of its world pose, so letting go
    // can hand the physics body the motion the object actually had instead of dropping it dead. Sampled per
    // held object rather than off the hand, so the desktop push/pull (which slides the object along the
    // holder) and any in-hand alignment show up in the throw exactly like controller motion does. - xlinka
    private readonly Dictionary<IGrabbable, MotionWindow> _motion = new();

    // A scale assist on the other hand means the pair was resizing, not aiming a spin. The holder still
    // throws, but the angular hand-off is dropped: stretching rolls the wrist around the object and that
    // reads as a fast tumble nobody asked for. Remembered briefly so letting go of the assist an instant
    // before the hold still counts. - xlinka
    private const float ScaleAssistMemory = 0.25f;
    private IGrabbable? _scaleAssistedTarget;
    private double _scaleAssistedStamp;

    public bool TryGrab(IGrabbable target) => TryGrab(target, out _);

    // Same grab, but reporting WHICH object ended up in the hand. Grab is allowed to hand the grip to
    // something other than the thing that was aimed at - a dispenser stamps out a copy and gives you
    // that instead of moving itself - so the return value of Grab is the hold, and the target is only
    // ever the request. Callers that go on to do something with what they grabbed (in-hand alignment,
    // tool routing) have to read it from here or they'll be working on the wrong object. - xlinka
    public bool TryGrab(IGrabbable target, out IGrabbable? held)
    {
        held = null;
        if (target == null) return false;

        // Second hand of the SAME user on a Scalable object: that's the resize gesture, not a steal.
        // Runs before CanGrab so grab arbitration/steal rules never see it.
        if (IsScaleAssistCandidate(target))
        {
            if (!BeginScaleAssist(target, target.Grabber!)) return false;
            held = target;
            return true;
        }

        if (!target.CanGrab(this)) return false;

        var holder = HolderSlot;
        if (holder == null) return false;

        var grabbed = target.Grab(this, holder);
        if (grabbed == null) return false;

        // Only record it if we actually came away holding it. On a client whose contested grab the host
        // rejects, the holder ref points elsewhere and CleanupGrabbed would drop it anyway - skip the
        // round trip and don't claim it. - xlinka
        bool isHeld = ReferenceEquals(grabbed.Grabber, this);
        if (!isHeld) return false;

        if (!_grabbed.Contains(grabbed)) _grabbed.Add(grabbed);
        // Fresh window per hold: whatever the object was doing before the hand closed on it is not a throw.
        _motion[grabbed] = new MotionWindow();
        held = grabbed;
        return true;
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
            if (candidate == null) continue;

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

        // Report what is actually in the hand, not what was reached for: a dispenser under the fingers
        // answers a grip with a fresh copy, and the caller's in-hand alignment has to act on that.
        return best != null && TryGrab(best, out grabbed) && grabbed != null;
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
                if (g.CanGrab(this) || IsScaleAssistCandidate(g)) return g;
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
            {
                if (g != null) _motion.Remove(g);
                _grabbed.RemoveAt(i);
            }
        }
    }

    public void Release(IGrabbable target)
    {
        if (target == null) return;
        if (!_grabbed.Remove(target)) return;
        StageReleaseMomentum(target);
        target.Release(this);
        _motion.Remove(target);
    }

    // A Scalable object THIS user already holds in the OTHER hand: grabbing it with this hand is the
    // two-hand resize gesture. CanGrab deliberately rejects same-user steals, so the candidate filters
    // must accept these explicitly or the second grip never reaches TryGrab. - xlinka
    private bool IsScaleAssistCandidate(IGrabbable target)
        => target.Scalable && target.Grabber is { } other && !ReferenceEquals(other, this)
           && other.OwningUser != null && ReferenceEquals(other.OwningUser, OwningUser);

    private bool BeginScaleAssist(IGrabbable target, Grabber partner)
    {
        var slot = (target as Component)?.Slot;
        if (slot == null || slot.IsDestroyed || Slot == null || partner.Slot == null)
            return false;

        _scaleTarget = target;
        _scalePartner = partner;
        _scaleStartDistance = HandDistanceTo(partner);
        if (_scaleStartDistance < MinScaleGrabDistance)
            _scaleStartDistance = MinScaleGrabDistance;
        _scaleStartScale = slot.LocalScale.Value;
        _scaleUndo = SlotTransformUndoBatch.Begin(slot, UndoLocale.Scale);
        partner.NoteScaleAssist(target);
        return true;
    }

    // The assisting hand tells the holder its throw is part of a resize. Both grabbers are the same user's,
    // so this stays local state - nothing about the stretch needs replicating beyond the scale writes. - xlinka
    public void NoteScaleAssist(IGrabbable target)
    {
        _scaleAssistedTarget = target;
        _scaleAssistedStamp = World?.Time.TotalTime ?? 0d;
    }

    public override void OnUpdate(float delta)
    {
        SampleHeldMotion();
        UpdateScaleAssist();
    }

    private void SampleHeldMotion()
    {
        // Also the only guaranteed per-frame prune of the window map: an object destroyed or stolen out of
        // the hand leaves its entry behind otherwise. - xlinka
        CleanupGrabbed();
        if (_grabbed.Count == 0) return;

        // Only the hand's own peer ever releases it, so no other peer needs a window.
        var owner = OwningUser;
        if (owner != null && !ReferenceEquals(owner, World?.LocalUser)) return;

        var clock = World?.Time;
        if (clock == null) return;

        double now = clock.TotalTime;
        for (int i = 0; i < _grabbed.Count; i++)
        {
            var g = _grabbed[i];
            var slot = (g as Component)?.Slot;
            if (slot == null || slot.IsRemoved) continue;

            if (!_motion.TryGetValue(g, out var window))
            {
                window = new MotionWindow();
                _motion[g] = window;
            }
            window.Sample(now, slot.GlobalPosition, slot.GlobalRotation);
        }
    }

    // Measure the hold and hand the numbers to the grabbable BEFORE it lets go, so the velocity and the
    // holder clear ride the same delta batch with the velocity first. - xlinka
    private void StageReleaseMomentum(IGrabbable target)
    {
        // Only the full Grabbable carries the throw tunables and the replicated hand-off fields. The other
        // IGrabbable implementations (spawners, gizmo handles) are never physics bodies. - xlinka
        if (target is not Grabbable grabbable || grabbable.IsDestroyed) return;

        float3 linear = float3.Zero;
        float3 angular = float3.Zero;

        var clock = World?.Time;
        if (clock != null && _motion.TryGetValue(target, out var window))
        {
            var slot = grabbable.Slot;
            if (slot != null && !slot.IsRemoved)
                window.Sample(clock.TotalTime, slot.GlobalPosition, slot.GlobalRotation);

            window.Evaluate(out linear, out angular);

            if (ReferenceEquals(_scaleAssistedTarget, target)
                && clock.TotalTime - _scaleAssistedStamp < ScaleAssistMemory)
            {
                angular = float3.Zero;
            }
        }

        grabbable.StageReleaseMomentum(linear, angular);
    }

    private void UpdateScaleAssist()
    {
        if (_scaleTarget == null)
            return;

        var slot = (_scaleTarget as Component)?.Slot;
        // The gesture dies with either hand, the object, or the partner's hold (released or stolen).
        if (_scalePartner?.Slot == null || Slot == null || slot == null || slot.IsDestroyed
            || !ReferenceEquals(_scaleTarget.Grabber, _scalePartner))
        {
            EndScaleAssist();
            return;
        }

        float factor = HandDistanceTo(_scalePartner) / _scaleStartDistance;
        if (factor < MinScaleFactor) factor = MinScaleFactor;
        if (factor > MaxScaleFactor) factor = MaxScaleFactor;
        slot.LocalScale.Value = _scaleStartScale * factor;

        // Keep the holder's "this is a resize" stamp fresh for as long as the stretch runs.
        _scalePartner.NoteScaleAssist(_scaleTarget);
    }

    private float HandDistanceTo(Grabber partner)
    {
        var delta = Slot!.GlobalPosition - partner.Slot!.GlobalPosition;
        return MathF.Sqrt(delta.LengthSquared);
    }

    private void EndScaleAssist()
    {
        if (_scaleTarget == null)
            return;
        _scaleTarget = null;
        _scalePartner = null;
        // One undo step for the whole stretch (start scale -> final scale).
        InspectorUndo.Record(this, _scaleUndo?.Commit());
        _scaleUndo = null;
    }

    public void ReleaseAll()
    {
        EndScaleAssist();
        // Snapshot what we're letting go so the receivable ones can be offered to a drop target after
        // they've been released back to the world. - xlinka
        var released = new List<IGrabbable>(_grabbed);
        for (int i = _grabbed.Count - 1; i >= 0; i--)
        {
            StageReleaseMomentum(_grabbed[i]);
            _grabbed[i].Release(this);
        }
        _grabbed.Clear();
        _motion.Clear();
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

    // Rolling world-pose window for one held object. Fixed ring, no allocation per frame.
    //
    // Release velocity is NOT the last frame's delta. The frame you let go on is the worst frame to trust:
    // the button press comes with a hand jerk, and a stutter right there either stalls the object (throw dies)
    // or teleports it (throw launches). So we average the pairwise velocities across the window, weighted by
    // how long each pair covered, after throwing out any pair whose speed sits nowhere near the window
    // median. Consistent motion has a median equal to the real speed, so trimming costs nothing when nothing
    // went wrong and eats exactly the one bad frame when something did. - xlinka
    private sealed class MotionWindow
    {
        private const int Capacity = 8;
        private const float WindowSeconds = 0.12f;
        private const float MinStep = 1e-5f;

        private readonly double[] _time = new double[Capacity];
        private readonly float3[] _position = new float3[Capacity];
        private readonly floatQ[] _rotation = new floatQ[Capacity];
        private int _count;
        private int _next;

        public void Sample(double time, in float3 position, in floatQ rotation)
        {
            // A release lands in the same frame as that frame's update sample; a zero-length pair would be a
            // divide by nothing.
            if (_count > 0 && time - _time[(_next - 1 + Capacity) % Capacity] < MinStep) return;

            _time[_next] = time;
            _position[_next] = position;
            _rotation[_next] = rotation;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        public bool Evaluate(out float3 linear, out float3 angular)
        {
            linear = float3.Zero;
            angular = float3.Zero;
            if (_count < 2) return false;

            int oldest = (_next - _count + Capacity) % Capacity;
            double newest = _time[(_next - 1 + Capacity) % Capacity];

            Span<float3> linears = stackalloc float3[Capacity];
            Span<float3> angulars = stackalloc float3[Capacity];
            Span<float> spans = stackalloc float[Capacity];
            int pairs = 0;

            for (int i = 0; i < _count - 1; i++)
            {
                int a = (oldest + i) % Capacity;
                int b = (oldest + i + 1) % Capacity;
                if (newest - _time[a] > WindowSeconds) continue;

                float dt = (float)(_time[b] - _time[a]);
                if (dt < MinStep) continue;

                linears[pairs] = (_position[b] - _position[a]) / dt;
                angulars[pairs] = AngularStep(_rotation[a], _rotation[b], dt);
                spans[pairs] = dt;
                pairs++;
            }

            if (pairs == 0) return false;

            linear = TrimmedAverage(linears, spans, pairs);
            angular = TrimmedAverage(angulars, spans, pairs);
            return true;
        }

        private static float3 TrimmedAverage(Span<float3> values, Span<float> weights, int count)
        {
            if (count == 1) return values[0];

            float3 sum = float3.Zero;
            float total = 0f;

            // Under four pairs there is no median worth trusting, so take the lot.
            if (count >= 4)
            {
                float median = MedianMagnitude(values, count);
                // The absolute slack keeps a slow, near-still hold from trimming itself to nothing.
                float low = median * 0.35f - 0.25f;
                float high = median * 2.5f + 0.25f;

                for (int i = 0; i < count; i++)
                {
                    float magnitude = values[i].Length;
                    if (magnitude < low || magnitude > high) continue;
                    sum += values[i] * weights[i];
                    total += weights[i];
                }
            }

            if (total <= 0f)
            {
                sum = float3.Zero;
                for (int i = 0; i < count; i++)
                {
                    sum += values[i] * weights[i];
                    total += weights[i];
                }
            }

            return total > 0f ? sum / total : float3.Zero;
        }

        private static float MedianMagnitude(Span<float3> values, int count)
        {
            Span<float> magnitudes = stackalloc float[Capacity];
            for (int i = 0; i < count; i++) magnitudes[i] = values[i].Length;

            for (int i = 1; i < count; i++)
            {
                float key = magnitudes[i];
                int j = i - 1;
                while (j >= 0 && magnitudes[j] > key)
                {
                    magnitudes[j + 1] = magnitudes[j];
                    j--;
                }
                magnitudes[j + 1] = key;
            }

            return (count & 1) == 1
                ? magnitudes[count / 2]
                : (magnitudes[count / 2 - 1] + magnitudes[count / 2]) * 0.5f;
        }

        // Axis times radians per second, in world space.
        private static float3 AngularStep(floatQ from, floatQ to, float dt)
        {
            // Double cover: without this a small spin one way reads as a nearly full turn the other.
            if (floatQ.Dot(from, to) < 0f) to = new floatQ(-to.x, -to.y, -to.z, -to.w);

            var delta = (to * from.Inverse).Normalized;
            float w = delta.w;
            if (w > 1f) w = 1f;
            else if (w < -1f) w = -1f;

            float angle = 2f * MathF.Acos(w);
            var axis = new float3(delta.x, delta.y, delta.z);
            float length = axis.Length;
            if (length < 1e-6f || angle < 1e-6f) return float3.Zero;

            return axis * (angle / (length * dt));
        }
    }
}
