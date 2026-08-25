// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Magnets;

// Makes the object it sits on snap. Let go of it near a MagnetSocket that will have it and it drops
// into the socket; let go near a MagnetGuide and it lines up with the shape instead. Grab it again
// and it comes loose.
//
// The whole decision runs on the ONE peer whose hand let go, and only the outcome replicates: the
// reparent, the socket's occupancy, and the final pose. Every peer running the search would have
// them all racing to claim the same socket over a distance test that nobody agrees on to the
// millimetre. -xlinka
[ComponentCategory("Interaction/Magnets")]
public class Magnet : Component, ICustomInspectorUI
{
    // Extra reach this item brings to a socket's own snap distance, in the item's local scale.
    public readonly Sync<float> Radius;

    // Measure from the middle of the object's visual bounds instead of its slot origin.
    public readonly Sync<bool> UseBoundsCenter;

    // Free-form labels a socket's tag whitelist can ask for.
    public readonly SyncFieldList<string> Tags;

    // When non-empty, only these sockets are ever considered and the world scan is skipped.
    public readonly SyncRefList<MagnetSocket> SocketWhitelist;

    // The spot on this object that lands on the socket, in local space.
    public readonly Sync<float3> SnapPoint;

    // Stay level when snapping: take yaw from the socket, keep the object's own up.
    public readonly Sync<bool> KeepUpright;

    // Let shape guides pull this object when no socket takes it.
    public readonly Sync<bool> UseGuides;

    // Let proximity sockets take this object out of the hand mid-carry.
    public readonly Sync<bool> AllowAutoAttach;

    // Ten checks a second while carrying is well under the noise floor next to what a held object
    // already costs, and no hand moves far enough in 100 ms to overshoot a socket's reach. -xlinka
    private const float AutoAttachInterval = 0.1f;

    // How long a magnet that found no grabbable above it waits before looking again.
    private const float RebindInterval = 0.5f;

    private Grabbable? _grabbable;
    private bool _hooked;
    private bool _carriedLocally;

    private bool _resolvePending;
    private float3 _releasePoint;
    private floatQ _releaseRotation;
    private Slot? _releaseParent;

    private bool _settling;
    private float _settleElapsed;
    private float _settleDuration;
    private Slot? _settleParent;
    private float3 _settleStartPosition;
    private floatQ _settleStartRotation;
    private float3 _settleEndPosition;
    private floatQ _settleEndRotation;

    private MagnetPlacementUndo? _carryUndo;
    private float _autoAttachTimer;
    private float _rebindTimer;

    private readonly List<MagnetSocket> _candidateScratch = new();

    public Magnet()
    {
        Radius = new Sync<float>(this, 0.05f);
        UseBoundsCenter = new Sync<bool>(this, false);
        Tags = new SyncFieldList<string>(this);
        SocketWhitelist = new SyncRefList<MagnetSocket>(this);
        SnapPoint = new Sync<float3>(this, float3.Zero);
        KeepUpright = new Sync<bool>(this, false);
        UseGuides = new Sync<bool>(this, true);
        AllowAutoAttach = new Sync<bool>(this, true);
    }

    // Radius in world units.
    public float WorldRadius => Slot != null ? Slot.LocalScaleToGlobal(System.Math.Max(0f, Radius.Value)) : 0f;

    public float3 LocalSnapPoint
    {
        get
        {
            var slot = Slot;
            if (slot == null || slot.IsDestroyed)
                return SnapPoint.Value;
            if (!UseBoundsCenter.Value)
                return SnapPoint.Value;
            if (!SlotBoundsHelper.TryComputeWorldBounds(slot, out var bounds))
                return SnapPoint.Value;
            return slot.GlobalPointToLocal(bounds.Center) + SnapPoint.Value;
        }
    }

    public float3 WorldSnapPoint
    {
        get
        {
            var slot = Slot;
            if (slot == null || slot.IsDestroyed)
                return float3.Zero;
            return slot.LocalPointToGlobal(LocalSnapPoint);
        }
    }

    public MagnetSocket? AttachedSocket
    {
        get
        {
            var socket = Slot?.Parent?.GetComponent<MagnetSocket>();
            return socket != null && ReferenceEquals(socket.CurrentItem, this) ? socket : null;
        }
    }

    public Grabbable? Carrier => _grabbable;

    // True when this magnet sits on the very slot the hand grabs, not on a child of it.
    public bool IsOnCarriedRoot => _grabbable == null || ReferenceEquals(_grabbable.Slot, Slot);

    // True while the settle glide is playing on this machine.
    public bool IsSettling => _settling;

    public override void OnStart()
    {
        base.OnStart();
        BindCarrier(0f);
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        FinalizeInterruptedSettle();
    }

    public override void OnDestroy()
    {
        FinalizeInterruptedSettle();
        UnhookCarrier();
        _carryUndo = null;
        base.OnDestroy();
    }

    // The settled pose is the one write that replicates, and it lands on the LAST frame of the
    // glide. Stop ticking before that frame and every other peer is left looking at the drop pose
    // forever, so anything that kills the glide early has to post the answer on its way out. -xlinka
    private void FinalizeInterruptedSettle()
    {
        if (!_settling)
            return;
        _settling = false;
        var slot = Slot;
        if (slot != null && !slot.IsDestroyed && ReferenceEquals(slot.Parent, _settleParent))
            WriteSettledPose();
    }

    public override void OnUpdate(float delta)
    {
        BindCarrier(delta);

        if (_resolvePending)
        {
            _resolvePending = false;
            ResolvePlacement();
            return;
        }

        if (_settling)
        {
            StepSettle(delta);
            return;
        }

        if (_carriedLocally)
            PollAutoAttach(delta);
    }

    // CARRIER BINDING
    // Walk up for the grabbable that governs this object, and then LEAVE IT ALONE while the object
    // is in a hand: mid-carry the object hangs under the grabber's holder slot, so re-walking would
    // find whatever grabbable the hand rig happens to carry instead of the one we started with.
    //
    // A magnet on something nobody can pick up never finds one, so the failed walk is rate limited
    // rather than repeated every frame of every magnet in the world. -xlinka
    private void BindCarrier(float delta)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return;

        if (_grabbable != null && !_grabbable.IsDestroyed)
        {
            if (_grabbable.IsGrabbed || _carriedLocally)
                return;
            var carrierSlot = _grabbable.Slot;
            if (carrierSlot != null && !carrierSlot.IsDestroyed
                && (ReferenceEquals(carrierSlot, slot) || slot.IsDescendantOf(carrierSlot)))
                return;
        }

        _rebindTimer -= delta;
        if (_grabbable == null && _rebindTimer > 0f)
            return;
        _rebindTimer = RebindInterval;

        UnhookCarrier();
        var found = slot.GetComponentInParents<Grabbable>();
        if (found == null || found.IsDestroyed)
            return;

        _grabbable = found;
        found.OnLocalGrabbed += HandleGrabbed;
        found.OnLocalReleased += HandleReleased;
        _hooked = true;
    }

    private void UnhookCarrier()
    {
        if (_hooked && _grabbable != null)
        {
            _grabbable.OnLocalGrabbed -= HandleGrabbed;
            _grabbable.OnLocalReleased -= HandleReleased;
        }
        _hooked = false;
        _grabbable = null;
    }

    // GRAB
    private void HandleGrabbed(IGrabbable grabbable)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed || grabbable is not Grabbable carrier)
            return;

        _carriedLocally = true;
        _settling = false;
        _resolvePending = false;
        _autoAttachTimer = 0f;

        // The grab already reparented us under the hand with the world pose preserved, so the
        // pre-grab parent only survives in the grabbable's own restore ref.
        var priorParent = carrier.LastParentRef.Target;
        var socket = priorParent?.GetComponent<MagnetSocket>();
        if (socket != null && !ReferenceEquals(socket.CurrentItem, this) && !ReferenceEquals(socket.Attached.Target, this))
            socket = null;

        BeginCarryUndo(carrier, priorParent, socket);

        if (socket == null)
            return;

        socket.ReleaseItem(this);
        if (!socket.UnparentOnDetach.Value)
            return;

        // Otherwise a release with nothing in range would restore the socket as the parent and the
        // object would slide back into the thing it was just pulled out of.
        var fallback = priorParent?.Parent ?? World?.RootSlot;
        if (fallback != null && !fallback.IsDestroyed)
            carrier.LastParentRef.Target = fallback;
    }

    private void BeginCarryUndo(Grabbable carrier, Slot? priorParent, MagnetSocket? socket)
    {
        var slot = Slot!;
        var reference = priorParent != null && !priorParent.IsDestroyed ? priorParent : World?.RootSlot;
        if (reference == null || reference.IsDestroyed)
        {
            _carryUndo = null;
            return;
        }

        var position = reference.GlobalPointToLocal(slot.GlobalPosition);
        var rotation = reference.GlobalRotation.Inverse * slot.GlobalRotation;
        var scale = reference.GlobalScaleToLocal(slot.GlobalScale);
        _carryUndo = MagnetPlacementUndo.Begin(this, reference, in position, in rotation, in scale, socket, "Move");
    }

    // RELEASE
    private void HandleReleased(IGrabbable grabbable)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed || !_carriedLocally)
            return;

        _carriedLocally = false;
        _resolvePending = true;
        _releasePoint = WorldSnapPoint;
        _releaseRotation = slot.GlobalRotation;
        _releaseParent = slot.Parent;
    }

    // The resolve deliberately waits a tick instead of running straight out of the release event.
    //
    // Grabber.ReleaseAll offers everything it just dropped to nearby IGrabbableReceivers, and
    // HandTool offers it to an IProxyReceiver under the pointer, both SYNCHRONOUSLY after the
    // grabbable has already raised its released event. A receiver is a target the user aimed at; a
    // magnet is ambient scenery that happens to be in reach. Aimed beats ambient, so the magnet goes
    // last, and one deferred tick is what puts it there without reaching into the grabber's flow at
    // all. The pose used for the search is the one captured AT release, so a frame of physics
    // between the drop and the resolve cannot drag the object out of a socket it had earned. -xlinka
    private void ResolvePlacement()
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
        {
            _carryUndo = null;
            return;
        }

        // Still in someone's hand. Our own re-grab would have cleared the pending flag before we got
        // here, so this is a steal: the drag belongs to the thief's machine now, and so does the
        // undo step for it.
        if (_grabbable != null && _grabbable.IsGrabbed)
        {
            _carryUndo = null;
            return;
        }

        // A receiver took it: reparented, consumed, or filed away. Leave it where it was put.
        if (!ReferenceEquals(slot.Parent, _releaseParent))
        {
            FinishCarry(null);
            return;
        }

        var socket = MagnetHelper.FindBestSocket(this, in _releasePoint, in _releaseRotation);
        if (socket != null)
        {
            AttachTo(socket);
            return;
        }

        if (UseGuides.Value
            && MagnetHelper.TryConstrain(World, in _releasePoint, in _releaseRotation, WorldRadius,
                out var guidePosition, out var guideRotation, out var guide)
            && guide != null)
        {
            ApplyGuide(guide, in guidePosition, in guideRotation);
            return;
        }

        FinishCarry(null);
    }

    // Gliding onto its pose. Callable from a tool or script.
    public bool AttachTo(MagnetSocket socket)
    {
        var slot = Slot;
        var socketSlot = socket?.Slot;
        if (slot == null || slot.IsDestroyed || socket == null || socketSlot == null || socketSlot.IsDestroyed)
            return false;
        if (!socket.CanAttach(this, WorldSnapPoint, slot.GlobalRotation, ignorePose: true))
        {
            FinishCarry(null);
            return false;
        }

        slot.SetParent(socketSlot, preserveGlobalTransform: true);
        socket.Claim(this);
        socket.ComputeItemPose(this, slot.LocalScale.Value, out var endPosition, out var endRotation);
        BeginSettle(in endPosition, in endRotation, socket.SettleTime.Value);
        FinishCarry(socket);
        return true;
    }

    private void ApplyGuide(MagnetGuide guide, in float3 worldPosition, in floatQ worldRotation)
    {
        var slot = Slot!;

        var parent = guide.AttachUnder.Target;
        if (parent != null && !parent.IsDestroyed
            && !ReferenceEquals(slot.Parent, parent) && !parent.IsDescendantOf(slot))
        {
            slot.SetParent(parent, preserveGlobalTransform: true);
        }

        // A guide constrains the SNAP POINT, not the object origin, so back the object off by
        // wherever its snap point sits under the orientation it is about to take.
        var endWorldRotation = guide.AlignRotation.Value ? worldRotation : slot.GlobalRotation;
        var offset = endWorldRotation * (slot.GlobalScale * LocalSnapPoint);
        var endWorldPosition = worldPosition - offset;

        var host = slot.Parent;
        float3 endPosition;
        floatQ endRotation;
        if (host == null || host.IsDestroyed)
        {
            endPosition = endWorldPosition;
            endRotation = endWorldRotation;
        }
        else
        {
            endPosition = host.GlobalPointToLocal(endWorldPosition);
            endRotation = host.GlobalRotation.Inverse * endWorldRotation;
        }

        BeginSettle(in endPosition, in endRotation, guide.SettleTime.Value);
        FinishCarry(null);
    }

    // SETTLE
    // The glide is a LOCAL courtesy and nothing more: every frame of it goes in through the silent
    // setters, which dirty the transform for rendering without queueing a delta, and the one write
    // that replicates is the final pose at the end. Remote peers therefore see the drop pose for the
    // length of the glide and then the settled pose, never the in-between frames. Writing the final
    // up front instead would not work: the delta encodes the field's value at flush time, so the
    // rewind that starts the glide would ship as the replicated answer. -xlinka
    private void BeginSettle(in float3 endPosition, in floatQ endRotation, float duration)
    {
        var slot = Slot!;
        _settleEndPosition = endPosition;
        _settleEndRotation = endRotation;
        _settleParent = slot.Parent;

        if (duration <= 0f)
        {
            _settling = false;
            WriteSettledPose();
            return;
        }

        _settleStartPosition = slot.LocalPosition.Value;
        _settleStartRotation = slot.LocalRotation.Value;
        _settleDuration = duration;
        _settleElapsed = 0f;
        _settling = true;
    }

    private void StepSettle(float delta)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed || !ReferenceEquals(slot.Parent, _settleParent))
        {
            // Something else moved the object out from under us mid-glide. Its pose is now that
            // system's business, not ours.
            _settling = false;
            return;
        }

        _settleElapsed += delta;
        float t = _settleDuration <= 0f ? 1f : System.Math.Clamp(_settleElapsed / _settleDuration, 0f, 1f);
        if (t >= 1f)
        {
            _settling = false;
            WriteSettledPose();
            return;
        }

        float eased = t * t * (3f - 2f * t);
        slot.LocalPosition.SetValueSilently(float3.Lerp(_settleStartPosition, _settleEndPosition, eased), change: true);
        slot.LocalRotation.SetValueSilently(floatQ.Slerp(_settleStartRotation, _settleEndRotation, eased), change: true);
    }

    private void WriteSettledPose()
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return;
        slot.LocalPosition.Value = _settleEndPosition;
        slot.LocalRotation.Value = _settleEndRotation;
    }

    // AUTO-ATTACH
    // Polled from the item rather than pushed from a trigger volume on the socket. A contact-driven
    // version needs a proxy collider per socket, kept in step with the snap distance field, in the
    // broadphase forever. This costs a short list walk on the one object that is currently in a
    // hand, and only in worlds that actually contain a proximity socket. -xlinka
    private void PollAutoAttach(float delta)
    {
        if (!AllowAutoAttach.Value)
            return;

        _autoAttachTimer -= delta;
        if (_autoAttachTimer > 0f)
            return;
        _autoAttachTimer = AutoAttachInterval;

        var slot = Slot;
        var carrier = _grabbable;
        if (slot == null || slot.IsDestroyed || carrier == null || !carrier.IsGrabbed)
            return;

        var registry = MagnetRegistry.For(World);
        if (registry == null || (SocketWhitelist.Count == 0 && !registry.HasAutoAttachSockets))
            return;

        var point = WorldSnapPoint;
        var rotation = slot.GlobalRotation;
        if (MagnetHelper.CollectSockets(this, in point, in rotation, _candidateScratch, autoAttachOnly: true) == 0)
            return;

        MagnetSocket? best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < _candidateScratch.Count; i++)
        {
            float distance = _candidateScratch[i].DistanceTo(in point);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = _candidateScratch[i];
            }
        }
        if (best == null)
            return;

        // Take it out of the hand first, then place it. Going through the Grabber rather than the
        // Grabbable is what drops it from the hand's own hold list too, so the next grip press does
        // not try to let go of something it no longer has. The release raises the grabbable's event,
        // which queues the ordinary deferred resolve; clearing that flag afterwards is what stops
        // the same snap from being decided twice.
        carrier.Grabber?.Release(carrier);

        _carriedLocally = false;
        _resolvePending = false;
        AttachTo(best);
    }

    private void FinishCarry(MagnetSocket? socket)
    {
        var undo = _carryUndo;
        _carryUndo = null;
        if (undo == null)
            return;

        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return;

        // Report the pose the object is HEADED for, not the mid-glide one it is wearing right now.
        var parent = _settling ? _settleParent : slot.Parent;
        var position = _settling ? _settleEndPosition : slot.LocalPosition.Value;
        var rotation = _settling ? _settleEndRotation : slot.LocalRotation.Value;
        InspectorUndo.Record(this, undo.Commit(socket, parent, in position, in rotation, slot.LocalScale.Value));
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        var socket = AttachedSocket;
        InspectorStats.AddRow(ui, "Attached to", socket?.Slot?.SlotName.Value ?? "loose");
        InspectorStats.AddRow(ui, "Carrier", _grabbable?.Slot?.SlotName.Value ?? "none");
        InspectorStats.AddRow(ui, "Reach", $"{WorldRadius:0.###} m world");

        var slot = Slot;
        int inRange = 0;
        if (slot != null && !slot.IsDestroyed)
        {
            var point = WorldSnapPoint;
            var rotation = slot.GlobalRotation;
            inRange = MagnetHelper.CollectSockets(this, in point, in rotation, _candidateScratch);
        }
        InspectorStats.AddRow(ui, "Sockets in range", inRange.ToString());

        string guideName = "none";
        if (slot != null && !slot.IsDestroyed && UseGuides.Value
            && MagnetHelper.TryConstrain(World, WorldSnapPoint, slot.GlobalRotation, WorldRadius,
                out _, out _, out var guide) && guide != null)
        {
            guideName = $"{guide.GetType().Name} on {guide.Slot?.SlotName.Value}";
        }
        InspectorStats.AddRow(ui, "Nearest guide", guideName);
        InspectorStats.AddRow(ui, "State", _settling ? "settling" : _carriedLocally ? "carried" : "idle");
    }
}
