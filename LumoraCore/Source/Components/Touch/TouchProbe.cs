// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Touch;

// A thing that can touch. Subclasses say where the tip is and how to find what it is aimed at; this
// base owns the part that must be identical everywhere: the per-frame sample, the out-of-sight
// rejection, and the hover/contact state machine that guarantees every Begin is eventually closed
// by an End.
//
// A probe only ever runs on the machine of the user driving it, and only while that user's world is
// in front of them. Everyone else sees the RESULT - the target's synced state - rather than
// re-deriving contact from replicated hand positions that arrive a frame or three late. -xlinka
public abstract class TouchProbe : Component, ICustomInspectorUI
{
    // The probe idles unless this is the local user.
    public readonly SyncRef<User> Owner;

    // Drives haptics and per-hand filtering.
    public readonly Sync<Chirality> Hand;

    // Half-angle in degrees off the toucher's view direction inside which contact counts. Past it,
    // only targets that explicitly accept out-of-sight touch respond.
    public readonly Sync<float> OutOfSightAngle;

    // Metres the tip may travel past a surface before the probe gives up on it.
    public readonly Sync<float> MaxPenetration;

    // Metres of approach before contact in which the target counts as hovered.
    public readonly Sync<float> HoverMargin;

    private ITouchTarget? _target;
    private bool _contacting;
    private float3 _lastPoint;
    private float3 _lastNormal;
    private float3 _lastDirection = float3.Forward;

    private readonly List<Slot> _excludeBuffer = new(2);

    private TouchRelay? _relay;
    private double _nextRelaySearch;

    private const double RelaySearchInterval = 0.25;
    private const string RelaySlotName = "Touch Relay ";

    protected TouchProbe()
    {
        Owner = new SyncRef<User>(this);
        Hand = new Sync<Chirality>(this, Chirality.None);
        OutOfSightAngle = new Sync<float>(this, 70f);
        MaxPenetration = new Sync<float>(this, 0.05f);
        HoverMargin = new Sync<float>(this, 0.04f);
    }

    // World-space position of the touching tip.
    public abstract float3 TipPosition { get; }

    // World-space direction the tip pushes along.
    public abstract float3 TipDirection { get; }

    public abstract TouchProbeKind Kind { get; }

    public ITouchTarget? CurrentTarget => _target;

    // Not merely hovering it.
    public bool IsContacting => _contacting;

    // For gizmos and inspector rows.
    public float3 CurrentPoint => _lastPoint;

    // Return false for "nothing", which the base turns into a clean End on whatever was held.
    protected abstract bool Sample(out TouchSample sample);

    protected readonly struct TouchSample
    {
        public readonly ITouchTarget Target;
        public readonly float3 Point;
        public readonly float3 Normal;

        // Negative while the tip is still approaching.
        public readonly float Penetration;

        public TouchSample(ITouchTarget target, in float3 point, in float3 normal, float penetration)
        {
            Target = target;
            Point = point;
            Normal = normal;
            Penetration = penetration;
        }
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (!ShouldRun())
        {
            EndTouch();
            return;
        }

        UpdateTouch();
    }

    public override void OnDisabled() => EndTouch();

    public override void OnDestroy()
    {
        EndTouch();
        base.OnDestroy();
    }

    private bool ShouldRun()
    {
        var world = World;
        if (world == null || world.Focus == World.WorldFocus.Background)
            return false;

        // Rig setup binds the owner at attach, but during a join the User to UserRoot link can still be
        // settling then, leaving it null. Retry here rather than idling for the rest of the session on
        // one unlucky frame. -xlinka
        var owner = Owner.Target;
        if (owner == null || owner.IsDestroyed)
        {
            BindOwnerFromHierarchy();
            owner = Owner.Target;
        }

        return owner != null && !owner.IsDestroyed && owner == world.LocalUser;
    }

    // Public so a rig can drive it by hand.
    public void UpdateTouch()
    {
        if (!Sample(out var sample) || sample.Target == null)
        {
            EndTouch();
            return;
        }

        var target = sample.Target;
        if (!IsUsable(target) || !target.CanTouch(this))
        {
            EndTouch();
            return;
        }

        float3 tip = TipPosition;
        float3 direction = TipDirection;
        float penetration = MathF.Max(sample.Penetration, 0f);
        bool contacting = sample.Penetration >= 0f;

        // Reaching blindly behind you should not press anything. Measure against the surface point,
        // not the tip: the tip can be buried inside the object where its position says nothing about
        // whether the user can see what they are pressing. -xlinka
        if (contacting && !target.AcceptsOutOfSight && IsOutOfSight(sample.Point))
            contacting = false;

        // A new target inherits nothing. Close the old one out first so its Begin/End pairs stay
        // balanced even when a hand sweeps straight from one control onto the next. -xlinka
        bool isNewTarget = !ReferenceEquals(target, _target);
        if (isNewTarget)
            EndTouch();

        TouchPhase hover = isNewTarget ? TouchPhase.Begin : TouchPhase.Stay;
        TouchPhase contact = contacting
            ? (_contacting ? TouchPhase.Stay : TouchPhase.Begin)
            : (_contacting ? TouchPhase.End : TouchPhase.None);

        _target = target;
        _contacting = contacting;
        _lastPoint = sample.Point;
        _lastNormal = sample.Normal;
        _lastDirection = direction;

        Dispatch(target, new TouchContact(
            hover, contact, in sample.Point, in sample.Normal, in tip, in direction,
            penetration, this, Owner.Target));
    }

    // Safe to call repeatedly and from teardown.
    public void EndTouch()
    {
        var target = _target;
        _target = null;
        bool wasContacting = _contacting;
        _contacting = false;

        if (target == null || !IsUsable(target))
            return;

        float3 tip = TipPosition;
        Dispatch(target, new TouchContact(
            TouchPhase.End,
            wasContacting ? TouchPhase.End : TouchPhase.None,
            in _lastPoint, in _lastNormal, in tip, in _lastDirection,
            0f, this, Owner.Target));
    }

    // One bad target must not take the whole probe down with it, or a single throwing button would
    // freeze both hands for the rest of the session. -xlinka
    private void Dispatch(ITouchTarget target, in TouchContact contact)
    {
        try
        {
            if (World?.IsAuthority == true || !Relay(target, in contact))
                target.OnTouch(in contact);
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"Touch target {DescribeTarget(target)} threw during OnTouch: {ex}");
        }
    }

    // Off the authority a touch is a REQUEST, not a fact. The control's own state and everything its
    // responders write belong to whoever owns them, and on world content that is the host - so the
    // contact goes into this user's own relay and the authority runs the reaction. Returns false when
    // there is nowhere to publish (a probe standing outside a user rig), which falls back to dispatching
    // locally exactly as before. -xlinka
    private bool Relay(ITouchTarget target, in TouchContact contact)
    {
        if (target is not TouchControl control || control.RunsOnToucher)
            return false;

        // A closing dispatch can come from OnDisabled or OnDestroy, which is no moment to be building
        // slots. By then a relay either exists from the contact that opened this or there was never
        // anything to close.
        var relay = ResolveRelay(create: contact.Hover != TouchPhase.End);
        if (relay == null)
            return false;

        // Feedback is this machine's own hardware. It stays here rather than waiting out a round trip,
        // which is the one part of a touch that has to be immediate to feel right.
        if (contact.Hover == TouchPhase.Begin)
            Haptics.Pulse(contact.User, contact.Hand, control.HoverHaptics.Value);
        if (contact.Contact == TouchPhase.Begin || contact.Contact == TouchPhase.End)
            Haptics.Pulse(contact.User, contact.Hand, control.ContactHaptics.Value);

        relay.Publish(control, in contact);
        return true;
    }

    // The probe itself is a local element (see AvatarAssembler), so it cannot carry the signal. The
    // relay hangs off the replicated node the probe's local slot sits under, minted in the local user's
    // own namespace, one per probe kind so a fingertip and a beam can be on two controls at once.
    private TouchRelay? ResolveRelay(bool create)
    {
        if (_relay != null && !_relay.IsDestroyed)
            return _relay;
        _relay = null;

        var world = World;
        var parent = Slot?.Parent;
        if (world == null || parent == null || parent.IsDestroyed || parent.ReferenceID.IsLocalID)
            return null;

        // Throttled: the rig node this hangs under can still be syncing in, and a miss must not turn
        // into a hierarchy search every frame for the first second of the session.
        double now = world.Time.TotalTime;
        if (now < _nextRelaySearch)
            return null;
        _nextRelaySearch = now + RelaySearchInterval;

        string name = RelaySlotName + Kind;
        var existing = parent.FindChild(name, false)?.GetComponent<TouchRelay>();
        if (existing != null && !existing.IsDestroyed)
        {
            _relay = existing;
            return _relay;
        }

        if (!create || IsDestroyed || !world.IsLocalAllocationReady)
            return null;

        var slot = world.AddLocalUserSlot(parent, name);
        slot.Persistent.Value = false;
        using (world.EnterLocalUserAllocation())
            _relay = slot.AttachComponent<TouchRelay>();
        return _relay;
    }

    private static string DescribeTarget(ITouchTarget target)
        => target is Component component ? component.ParentHierarchyToString() : target.GetType().Name;

    private static bool IsUsable(ITouchTarget target)
        => target is not Component component || (!component.IsDestroyed && component.Enabled.Value && component.Slot?.IsActive == true);

    private bool IsOutOfSight(in float3 point)
    {
        var root = ResolveOwnerRoot();
        if (root == null || root.IsDestroyed)
            return false;

        float3 toPoint = point - root.HeadPosition;
        float length = toPoint.Length;
        if (length < 1e-5f)
            return false;

        float3 forward = root.HeadFacingDirection;
        float forwardLength = forward.Length;
        if (forwardLength < 1e-5f)
            return false;

        float cosine = float3.Dot(toPoint / length, forward / forwardLength);
        float limit = MathF.Cos(MathF.Min(MathF.Max(OutOfSightAngle.Value, 0f), 180f) * (MathF.PI / 180f));
        return cosine < limit;
    }

    // Walk up from a collider slot to the nearest touch target that will take this probe. The
    // collider, the mesh and the control routinely live on different slots of the same object, and a
    // nested control must win over its own container. Stops at a search block, same boundary the
    // pointer honours, so touching something inside a container never resolves out to the container.
    // -xlinka
    protected ITouchTarget? ResolveTarget(Slot? slot)
    {
        var current = slot;
        while (current != null)
        {
            foreach (var candidate in current.GetComponentsImplementing<ITouchTarget>())
            {
                if (candidate is Component component && (component.IsDestroyed || !component.Enabled.Value))
                    continue;
                if (!Accepts(candidate))
                    continue;
                if (!candidate.CanTouch(this))
                    continue;
                return candidate;
            }

            if (!ReferenceEquals(current, slot) && current.GetComponent<Interaction.SearchBlock>() != null)
                break;

            current = current.Parent;
        }
        return null;
    }

    private bool Accepts(ITouchTarget target)
        => Kind == TouchProbeKind.Fingertip ? target.AcceptsFingertip : target.AcceptsRemote;

    // Slots the probe's own casts must skip: the toucher's body. Without this a fingertip probe
    // spends every frame hitting the user's own hand collider instead of the world.
    protected IReadOnlyList<Slot> OwnBodyExclusions()
    {
        _excludeBuffer.Clear();
        var rootSlot = ResolveOwnerRoot()?.Slot;
        if (rootSlot != null && !rootSlot.IsDestroyed)
            _excludeBuffer.Add(rootSlot);
        return _excludeBuffer;
    }

    // User.Root is only populated on the machine that registered it; the replicated ref is the one
    // that is always there. Take whichever resolves. -xlinka
    private UserRoot? ResolveOwnerRoot()
    {
        var owner = Owner.Target;
        if (owner == null || owner.IsDestroyed)
            return null;
        var root = owner.Root ?? owner.UserRootRef.Target;
        return root != null && !root.IsDestroyed ? root : null;
    }

    // What the probe is doing RIGHT NOW, read back off live state rather than recomputed from the
    // fields. When a control will not respond this is the block that tells you whether the probe is
    // even running, what it is on, and how far the tip is from it. -xlinka
    public void BuildInspectorBody(UIBuilder ui)
    {
        var owner = Owner.Target;
        InspectorStats.AddRow(ui, "Kind", $"{Kind} / {Hand.Value}");
        InspectorStats.AddRow(ui, "Owner", owner == null
            ? "unbound"
            : $"{owner.UserName.Value}{(owner == World?.LocalUser ? " (local)" : " (remote - idle)")}");

        var target = CurrentTarget;
        InspectorStats.AddRow(ui, "Target", target is Component component
            ? $"{component.GetType().Name} on {component.Slot?.SlotName.Value}"
            : "none");
        InspectorStats.AddRow(ui, "State", target == null
            ? "-"
            : (IsContacting ? "contacting" : "hovering"));

        float3 tip = TipPosition;
        InspectorStats.AddRow(ui, "Tip", $"{tip.x:0.###}, {tip.y:0.###}, {tip.z:0.###}");
        if (target != null)
        {
            float3 gap = CurrentPoint - tip;
            InspectorStats.AddRow(ui, "Tip to surface", $"{gap.Length * 100f:0.#} cm");
        }
    }

    // Called by rig setup and re-checked on start, because a probe can be built before its user root
    // has finished syncing in.
    public void BindOwnerFromHierarchy()
    {
        if (Owner.Target != null && !Owner.Target.IsDestroyed)
            return;

        var user = Slot?.ActiveUserRoot?.ActiveUser;
        if (user != null)
            Owner.Target = user;
    }
}
