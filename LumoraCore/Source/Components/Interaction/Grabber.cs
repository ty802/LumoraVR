// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Touch;
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

    // DISPENSE REQUESTS
    // A dispenser the world's author placed makes world things. Someone whose role may not bring things into
    // the world still pulls from one: the hand asks here, and the authority makes the copy as the world and
    // puts it in this hand. Only the hand's own user writes these; only the authority acts on them, at most
    // ten times a second per hand, whatever arrives. - xlinka
    [NonPersistent]
    public readonly SyncRef<GrabSpawner> DispenseFrom = new();

    [NonPersistent]
    public readonly Sync<int> DispenseCount = new();

    private const double DispenseInterval = 0.1;
    private const double DispenseWait = 5.0;
    private int _dispenseSeen;
    private double _lastServed = double.NegativeInfinity;
    private bool _awaitingDispense;
    private bool _dispenseLetGo;
    private double _requestedAt;

    // lazy: create a child "Holder" slot the first time something is grabbed. - xlinka
    public Slot? HolderSlot
    {
        get
        {
            if (_holderSlot != null && !_holderSlot.IsRemoved) return _holderSlot;
            if (Slot == null || Slot.IsRemoved) return null;
            // The host's own hand is made in the host rig scope (World.EnterHostRigScope).
            using (World?.EnterHostRigScope(Slot))
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

    // TWO-HAND SCALING. The second hand of the same user closing on something the first hand holds does
    // not steal it: it becomes the SCALE REFERENCE for that hand, and from then on the first hand's holder
    // is scaled by how far the two holders have moved apart since the gesture began. Scaling the holder
    // rather than the object means the whole hand-load grows about the grab point, and the size lands on
    // the object's own scale when the first hand lets go (the release bakes the holder scale in). Local
    // state on both hands; only the holder scale writes replicate. - xlinka
    private const float MinScaleGrabDistance = 0.05f; // holders nearly touching at start would explode the ratio
    private const float MinScaleFactor = 0.02f;
    private const float MaxScaleFactor = 50f;

    private Grabber? _scaleReference;
    private float3 _baseScale;
    private float _baseDistance;

    public bool IsScaling => _scaleReference != null;

    public Grabber? ScaleReference => _scaleReference;

    // A hand may narrow its own stretch further for the gesture under way: the userspace pointer keeps a
    // carried panel between a size that can still be read and one that does not swallow the view. Zero is
    // no limit, and every new gesture starts with none. - xlinka
    private float _gestureMinFactor;
    private float _gestureMaxFactor;

    public void LimitScaleFactor(float min, float max)
    {
        _gestureMinFactor = min;
        _gestureMaxFactor = max;
    }

    // One record per held object: the throw window plus the holder scale the object came in under, so a
    // release can tell a stretch that landed on the object from one that did not. - xlinka
    private sealed class Hold
    {
        // THROW HAND-OFF: while something is held we keep a short rolling window of its world pose, so letting
        // go can hand the physics body the motion the object actually had instead of dropping it dead. Sampled
        // per held object rather than off the hand, so the desktop push/pull (which slides the object along
        // the holder) and any in-hand alignment show up in the throw exactly like controller motion does.
        public readonly MotionWindow Motion = new();
        public float3 HolderScaleAtGrab = float3.One;
    }

    private readonly Dictionary<IGrabbable, Hold> _holds = new();

    // A scale reference on the other hand means the pair was resizing, not aiming a spin. The holder still
    // throws, but the angular hand-off is dropped: stretching rolls the wrist around the object and that
    // reads as a fast tumble nobody asked for. Remembered briefly so letting go of the reference an instant
    // before the hold still counts. - xlinka
    private const float ScaleAssistMemory = 0.25f;
    private double _scaleAssistedStamp = double.NegativeInfinity;

    // Whether a stretch may act on this hand's load: everything held has to be Scalable, and the role has
    // to leave any room to resize at all. Bounds that meet (or cross) pin the factor to one value, which is
    // the host saying this role does not resize. - xlinka
    public bool CanScaleHeldObjects
    {
        get
        {
            CleanupGrabbed();
            if (_grabbed.Count == 0) return false;
            for (int i = 0; i < _grabbed.Count; i++)
            {
                if (!_grabbed[i].Scalable) return false;
            }
            return !RoleScaleIsPinned();
        }
    }

    public bool TryGrab(IGrabbable target) => TryGrab(target, out _);

    // Same grab, but reporting WHICH object ended up in the hand. Grab is allowed to hand the grip to
    // something other than the thing that was aimed at - a dispenser stamps out a copy and gives you
    // that instead of moving itself - so the return value of Grab is the hold, and the target is only
    // ever the request. Callers that go on to do something with what they grabbed (in-hand alignment,
    // tool routing) have to read it from here or they'll be working on the wrong object. A grab that
    // became a scale reference reports the target it is now scaling against, with IsScaling set; the
    // object stays in the other hand. - xlinka
    public bool TryGrab(IGrabbable target, out IGrabbable? held)
    {
        held = null;
        if (target == null) return false;

        // Second hand of the SAME user on a load the other hand may scale: that's the resize gesture, not
        // a steal. Runs before CanGrab so grab arbitration/steal rules never see it.
        if (IsScaleReferenceCandidate(target))
        {
            if (IsScaling || !BeginScaleReference(target.Grabber!)) return false;
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
        // Fresh record per hold: whatever the object was doing before the hand closed on it is not a throw.
        _holds[grabbed] = new Hold { HolderScaleAtGrab = holder.LocalScale.Value };
        held = grabbed;
        Pulse(TouchHaptics.Medium);
        return true;
    }

    // The hand this grabber is: the world hand tool above it, or the userspace pointer it sits on. Only this
    // machine's own hand is felt, never a hand somebody else is moving here. - xlinka
    private void Pulse(TouchHaptics preset)
    {
        if (Slot == null)
            return;
        if (Slot.GetComponentInParents<HandTool>() is { } hand)
            Haptics.Pulse(OwningUser, hand.Side.Value, preset);
        else if (Slot.GetComponent<UserspacePointer>() is { } pointer)
            Haptics.Pulse(pointer.Side.Value, preset);
    }

    // Touch / proximity grab: sphere-overlap at 'point'; each overlapped collider offers the nearest grabbable
    // at or above it, and the hand takes every one of those sharing the best GrabPriority, the ones it may
    // take now ahead of the rest and the nearest first, except one that hangs under another it is taking.
    // Unlike the laser path this DOES accept AllowOnlyPhysicalGrab objects - that flag means "only reachable
    // by hand". Arbitration still runs through CanGrab/TryGrab (host authority); 'grabbed' is the first hold
    // actually recorded. - xlinka
    public bool TryGrabNearby(float3 point, float radius, out IGrabbable? grabbed)
    {
        grabbed = null;
        var world = World;
        if (world?.Physics == null) return false;

        var hits = new List<Slot>();
        // Plenty of grabbables are held by Trigger colliders (orbs, grab handles, query-only parts), so the
        // overlap includes triggers or it would miss them. - xlinka
        world.Physics.OverlapSphere(point, radius, hits, hitTriggers: true);
        if (hits.Count == 0) return false;

        var candidates = new List<(IGrabbable Grabbable, bool Now, float DistSq)>();
        foreach (var slot in hits)
        {
            var candidate = FindGrabbableInParents(slot);
            if (candidate == null) continue;

            // Never grab our own hand rig.
            if (candidate is Component cc && cc.Slot != null && cc.Slot.IsDescendantOf(Slot)) continue;

            float distSq = (slot.GlobalPosition - point).LengthSquared;
            int at = candidates.FindIndex(c => ReferenceEquals(c.Grabbable, candidate));
            if (at < 0)
                candidates.Add((candidate, candidate.CanGrab(this), distSq));
            else if (distSq < candidates[at].DistSq)
                candidates[at] = (candidate, candidates[at].Now, distSq);
        }
        if (candidates.Count == 0) return false;

        candidates.Sort((a, b) => a.Now != b.Now ? (a.Now ? -1 : 1)
            : a.Grabbable.GrabPriority != b.Grabbable.GrabPriority ? b.Grabbable.GrabPriority.CompareTo(a.Grabbable.GrabPriority)
            : a.DistSq.CompareTo(b.DistSq));

        int? taken = null;
        foreach (var (candidate, _, _) in candidates)
        {
            if (taken.HasValue && candidate.GrabPriority != taken.Value) break;
            if (UnderAnother(candidate, candidates)) continue;

            // Report what is actually in the hand, not what was reached for: a dispenser under the fingers
            // answers a grip with a fresh copy, and the caller's in-hand alignment has to act on that.
            if (!TryGrab(candidate, out var held) || held == null) continue;
            grabbed ??= held;
            taken = candidate.GrabPriority;
            // A second hand on the other hand's load is a resize, not a grab of everything around it.
            if (IsScaling) break;
        }
        return grabbed != null;
    }

    private bool UnderAnother(IGrabbable candidate, List<(IGrabbable Grabbable, bool Now, float DistSq)> candidates)
    {
        if (candidate is not Component { Slot: { } slot }) return false;
        foreach (var (other, now, _) in candidates)
        {
            if (!ReferenceEquals(other, candidate) && now && other.GrabPriority == candidate.GrabPriority
                && other is Component { Slot: { } otherSlot } && slot.IsDescendantOf(otherSlot))
                return true;
        }
        return false;
    }

    // Walk up from a hit slot for the first grabbable this grabber may take. Stops at a SearchBlock (a
    // boundary another rig owns), except on the originating slot. Mirrors HandTool.FindBestGrabbable but
    // does NOT exclude AllowOnlyPhysicalGrab (touch is the physical path). - xlinka
    private IGrabbable? FindGrabbableInParents(Slot? hitSlot)
    {
        var current = hitSlot;
        while (current != null && !current.IsRemoved && !current.IsRootSlot)
        {
            if (!ReferenceEquals(current, hitSlot) && current.GetComponent<SearchBlock>() != null)
                break;

            // Nothing at or above a grab block is offered from below it.
            if (current.GetComponent<GrabBlock>() is { IsDestroyed: false } block && block.Enabled.Value)
                return null;

            foreach (var g in current.GetComponentsImplementing<IGrabbable>())
            {
                if (g.CanGrab(this) || IsScaleReferenceCandidate(g)) return g;
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
                if (g != null) _holds.Remove(g);
                _grabbed.RemoveAt(i);
            }
        }
    }

    public void Release(IGrabbable target)
    {
        if (target == null) return;
        if (!_grabbed.Remove(target)) return;
        StageReleaseMomentum(target);
        _holds.TryGetValue(target, out var hold);
        target.Release(this);
        RecordScaleUndo(target, hold);
        _holds.Remove(target);
        Pulse(TouchHaptics.Light);
    }

    // Something THIS user already holds in the OTHER hand, and that hand's load may be resized: grabbing
    // it with this hand is the two-hand resize gesture. CanGrab deliberately rejects same-user steals, so
    // the candidate filters must accept these explicitly or the second grip never reaches TryGrab. - xlinka
    internal bool IsScaleCandidate(IGrabbable target) => IsScaleReferenceCandidate(target);

    private bool IsScaleReferenceCandidate(IGrabbable target)
        => target.Grabber is { } other && !ReferenceEquals(other, this)
           && other.OwningUser != null && ReferenceEquals(other.OwningUser, OwningUser)
           && other.CanScaleHeldObjects;

    private bool BeginScaleReference(Grabber partner)
    {
        if (partner.IsDestroyed || HolderSlot == null || partner.HolderSlot == null)
            return false;

        _scaleReference = partner;
        _gestureMinFactor = 0f;
        _gestureMaxFactor = 0f;
        _baseScale = partner.HolderSlot.LocalScale.Value;
        _baseDistance = HolderDistanceTo(partner);
        if (_baseDistance < MinScaleGrabDistance)
            _baseDistance = MinScaleGrabDistance;
        partner.NoteScaleAssist();
        Pulse(TouchHaptics.Medium);
        return true;
    }

    // The reference hand tells the holder its throw is part of a resize. Both grabbers are the same user's,
    // so this stays local state - nothing about the stretch needs replicating beyond the scale writes. - xlinka
    public void NoteScaleAssist()
    {
        _scaleAssistedStamp = World?.Time.TotalTime ?? 0d;
    }

    public override void OnStart()
    {
        base.OnStart();
        // Whatever count is already there the first time the authority sees this hand is history.
        _dispenseSeen = DispenseCount.Value;
    }

    public override void OnUpdate(float delta)
    {
        SampleHeldMotion();
        UpdateScaleReference();
        if (World?.IsAuthority == true && DispenseCount.Value != _dispenseSeen)
            ServeDispense();
    }

    // On this hand's own machine: ask the authority for a copy from a world dispenser. The same grip asking
    // twice (the touch grab and then the laser) asks once.
    internal void RequestDispense(GrabSpawner spawner)
    {
        double now = World?.Time.TotalTime ?? 0d;
        if (_awaitingDispense && !_dispenseLetGo && ReferenceEquals(DispenseFrom.Target, spawner) && now - _requestedAt < 0.5)
            return;
        if (!ReferenceEquals(DispenseFrom.Target, spawner))
            DispenseFrom.Target = spawner;
        DispenseCount.Value = DispenseCount.Value + 1;
        _awaitingDispense = true;
        _dispenseLetGo = false;
        _requestedAt = now;
    }

    private void ServeDispense()
    {
        _dispenseSeen = DispenseCount.Value;
        double now = World!.Time.TotalTime;
        if (now - _lastServed < DispenseInterval)
            return;
        var owner = OwningUser;
        if (owner == null || ReferenceEquals(owner, World.LocalUser) || DispenseFrom.Target is not { IsDestroyed: false } spawner)
            return;
        _lastServed = now;
        spawner.DispenseFor(this);
    }

    // What the authority put in this hand for a dispense it asked for: held from now on as if gripped here.
    // One that arrives after the grip has already let go is let go again on the next update, once the
    // batch that brought it has finished landing. Anything else the authority hands this hand unasked is
    // left to it.
    internal bool Adopt(IGrabbable grabbable)
    {
        if (_grabbed.Contains(grabbable))
            return false;
        double now = World?.Time.TotalTime ?? 0d;
        if (!_awaitingDispense || now - _requestedAt > DispenseWait)
            return false;
        bool letGo = _dispenseLetGo;
        _awaitingDispense = false;
        _dispenseLetGo = false;
        if (letGo)
        {
            RunInUpdates(1, () =>
            {
                if (ReferenceEquals(grabbable.Grabber, this))
                    grabbable.Release(this);
            });
            return false;
        }
        _grabbed.Add(grabbable);
        _holds[grabbable] = new Hold { HolderScaleAtGrab = _holderSlot?.LocalScale.Value ?? float3.One };
        return true;
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

            if (!_holds.TryGetValue(g, out var hold))
            {
                hold = new Hold();
                _holds[g] = hold;
            }
            hold.Motion.Sample(now, slot.GlobalPosition, slot.GlobalRotation);
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
        if (clock != null && _holds.TryGetValue(target, out var hold))
        {
            var slot = grabbable.Slot;
            if (slot != null && !slot.IsRemoved)
                hold.Motion.Sample(clock.TotalTime, slot.GlobalPosition, slot.GlobalRotation);

            hold.Motion.Evaluate(out linear, out angular);

            if (clock.TotalTime - _scaleAssistedStamp < ScaleAssistMemory)
                angular = float3.Zero;
        }

        grabbable.StageReleaseMomentum(linear, angular);
    }

    private void UpdateScaleReference()
    {
        var partner = _scaleReference;
        if (partner == null)
            return;

        var holder = HolderSlot;
        // The gesture dies with either hand or with the partner's hold: released, stolen, or no longer
        // something this role may resize.
        if (partner.IsDestroyed || holder == null || partner.HolderSlot == null || !partner.CanScaleHeldObjects)
        {
            EndScaleReference();
            return;
        }

        float factor = ClampScaleFactor(HolderDistanceTo(partner) / _baseDistance);
        partner.HolderSlot.LocalScale.Value = _baseScale * factor;

        // Keep the holder's "this is a resize" stamp fresh for as long as the stretch runs.
        partner.NoteScaleAssist();
    }

    private float HolderDistanceTo(Grabber partner)
        => float3.Distance(HolderSlot!.GlobalPosition, partner.HolderSlot!.GlobalPosition);

    // The hard limits keep a near-zero base distance from launching the object; the role bounds are the
    // host's word on how far this user may resize per grab, and pinned bounds forbid it outright. - xlinka
    private float ClampScaleFactor(float factor)
    {
        if (factor < MinScaleFactor) factor = MinScaleFactor;
        if (factor > MaxScaleFactor) factor = MaxScaleFactor;
        if (_gestureMinFactor > 0f && factor < _gestureMinFactor) factor = _gestureMinFactor;
        if (_gestureMaxFactor > 0f && factor > _gestureMaxFactor) factor = _gestureMaxFactor;

        var permissions = World?.DataModelPermissions;
        if (permissions != null && permissions.TryGetScaleBounds(OwningUser, out float min, out float max))
        {
            if (min > 0f && factor < min) factor = min;
            if (max > 0f && factor > max) factor = max;
        }
        return factor;
    }

    private bool RoleScaleIsPinned()
    {
        var permissions = World?.DataModelPermissions;
        if (permissions == null || !permissions.TryGetScaleBounds(OwningUser, out float min, out float max))
            return false;
        return min > 0f && max > 0f && min >= max;
    }

    // Also ended by a hand that takes over the load it was stretching when the holding hand lets go.
    public void EndScaleReference()
    {
        _scaleReference = null;
        _gestureMinFactor = 0f;
        _gestureMaxFactor = 0f;
    }

    // A stretch lives on the holder until the release bakes it into the object, so this is the first
    // moment the object's own scale carries it. One field step from the size it would have come back
    // at to the size it did; undo after a plain hold records nothing. - xlinka
    private void RecordScaleUndo(IGrabbable target, Hold? hold)
    {
        // Ordinary grabbables record their complete pose, including holder-driven scaling.
        // Specialized grabbables (such as handles) keep their existing path.
        if (target is Grabbable) return;
        if (hold == null) return;
        var slot = (target as Component)?.Slot;
        if (slot == null || slot.IsDestroyed) return;
        var holder = _holderSlot;
        if (holder == null || holder.IsRemoved) return;

        var ratio = holder.LocalScale.Value / hold.HolderScaleAtGrab;
        if (ratio == float3.One) return;
        if (float.IsNaN(ratio.x) || float.IsNaN(ratio.y) || float.IsNaN(ratio.z)
            || float.IsInfinity(ratio.x) || float.IsInfinity(ratio.y) || float.IsInfinity(ratio.z))
            return;

        var after = slot.LocalScale.Value;
        var before = after / ratio;
        InspectorUndo.Record(this, new FieldEditUndoBatch(slot.LocalScale, before, after, UndoLocale.Scale));
    }

    public void ReleaseAll()
    {
        EndScaleReference();
        if (_awaitingDispense)
            _dispenseLetGo = true;
        // Snapshot what we're letting go so the receivable ones can be offered to a drop target after
        // they've been released back to the world. - xlinka
        var released = new List<IGrabbable>(_grabbed);
        for (int i = _grabbed.Count - 1; i >= 0; i--)
        {
            var g = _grabbed[i];
            StageReleaseMomentum(g);
            _holds.TryGetValue(g, out var hold);
            g.Release(this);
            RecordScaleUndo(g, hold);
        }
        _grabbed.Clear();
        _holds.Clear();
        if (released.Count > 0)
            Pulse(TouchHaptics.Light);
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
        // Receiver surfaces often ride on Trigger colliders, so include triggers here too or the
        // drop-into-receiver overlap finds nothing. - xlinka
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
