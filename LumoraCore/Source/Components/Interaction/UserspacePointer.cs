// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.UI;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// The userspace dash pointer. It lives in the userspace overlay world, not on any game-world
// avatar, so the dashboard always has a working cursor of its own - even right after you delete
// the world you were standing in, which used to kill the avatar and leave the dash with nothing
// to point at. It sits on a controller-tracked hand in the userspace pointer rig: on desktop the
// laser aims off the platform free-cursor ray, in VR off the tracked controller pose. Either way
// it casts at the dash surface and feeds the real press (mouse left / controller trigger) straight
// into it, exactly like an in-world hand tool would, just permanently parked in userspace. While
// it is live it flags the input layer so the in-world hand tools stand down and you never get two
// cursors fighting over the same panel. -xlinka
[ComponentCategory("Interaction")]
[DefaultUpdateOrder(-1000)]
public sealed class UserspacePointer : Component
{
    public readonly Sync<Chirality> Side = new();

    // Anything closer than this on a grab would sit inside the hand.
    private const float MinHoldDistance = 0.1f;

    private static readonly UserspacePointer?[] _bySide = new UserspacePointer?[2];

    private Slot? _laserSlot;
    private InteractionLaser? _laser;
    private Grabber? _grabber;
    private bool _prevGrabHeld;
    private bool _laserHold;
    private float _holdDistance;

    public static UserspacePointer? ForSide(Chirality side)
    {
        var pointer = _bySide[SideIndex(side)];
        return pointer != null && !pointer.IsDestroyed ? pointer : null;
    }

    private static int SideIndex(Chirality side) => side == Chirality.Left ? 0 : 1;

    // The widget panel this hand is carrying, if any. The grids read it to know whether the pointer
    // over them is placing a widget or just pointing.
    public WidgetPanel? HeldPanel
    {
        get
        {
            if (_grabber == null)
                return null;
            var held = _grabber.GrabbedObjects;
            for (int i = 0; i < held.Count; i++)
            {
                var panel = WidgetPanel.From(held[i]);
                if (panel != null)
                    return panel;
            }
            return null;
        }
    }

    public override void OnInit()
    {
        base.OnInit();
        Side.Value = Chirality.Right;
    }

    public override void OnStart()
    {
        base.OnStart();

        // On a child slot, not this one: the beam has to come off the same point as the world hand's,
        // which sits at the tool holder, and this slot is the tracked controller itself. -xlinka
        _laserSlot = Slot.FindChild("Laser", recursive: false) ?? Slot.AddSlot("Laser");
        _laser = _laserSlot.GetComponent<InteractionLaser>() ?? _laserSlot.AttachComponent<InteractionLaser>();
        _laser.ControllerSide.Value = Side.Value;
        _laser.SetRole(InteractionLaser.Role.Userspace);
        _laser.SetIgnoreRoot(Slot);        // never let the pointer trip over its own rig, held items included
        // VR draws the beam from the hand to the panel, off the same tip as the world beam. Desktop keeps
        // just the cursor, the same as the world laser there.
        _laser.ShowDesktopBeam.Value = false;
        _laser.SetDormant(true);           // parked until the dash opens
        _grabber = Slot.GetComponent<Grabber>() ?? Slot.AttachComponent<Grabber>();
        _bySide[SideIndex(Side.Value)] = this;
    }

    // The world hand this pointer rides on, lifted above the dash while a panel is up. Per side, so a
    // hand that is not pointing at anything in userspace still shows over the dash: it is the hand you
    // are about to point with.
    private readonly UserspaceHandOverlay _handOverlay = new();

    public UserspaceHandOverlay HandOverlay => _handOverlay;

    // What the pointer published this frame, for anything that wants the verdict without the input layer.
    public bool OwnsHand { get; private set; }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (_laser == null) return;

        var input = Engine.Current?.InputInterface;
        FollowWorldTip();
        // Two things can want this pointer: the dash, and the VR keyboard, which shows itself whenever
        // a text field takes focus and needs a cursor of its own to be typed on. The keyboard term is
        // VR-gated so desktop behaves exactly as it did - dash-driven, right hand only. Desktop has one
        // mouse cursor; in VR each hand has its own tracked laser, so both are live and whichever one
        // you point at a panel shows a cursor on it. -xlinka
        bool wantsPointer = input != null
            && (input.IsDashboardOpen || (input.IsVRActive && VrKeyboard.LocalShown));
        bool vr = input?.IsVRActive == true;
        bool live = wantsPointer && (vr || Side.Value == Chirality.Right);

        // Feed state BEFORE the laser casts, so when it does it sees the fresh press state and userspace
        // as its only valid target. -xlinka
        _laser.SetDormant(!live);
        if (live)
        {
            // The whole userspace subtree, not just the dash surface: the keyboard is a sibling of the
            // dash, and restricting to one of them makes the other unclickable. Everything outside
            // userspace stays out of reach either way, which is the point of the restriction. -xlinka
            _laser.SetExclusiveRoot(Templates.Userspace.LocalRoot ?? UserspaceDashboard.LocalInstance?.SurfaceSlot);
            // A beam with something riding it is a hand carrying, not a hand pressing: the trigger
            // stays out of the panel under it, the same rule the in-world hand tool applies while it
            // holds an object on its laser. Otherwise moving the keyboard types on it. -xlinka
            _laser.SetToolState(ReadPrimary(input!) && !_laserHold, _laserHold);
            // A carried item rides the beam at its hold distance; the cursor sits on it there.
            _laser.SetHoldDistance(_laserHold ? _holdDistance : null);
        }

        // Cast now rather than a moment later in the laser's own update: the world laser of this hand
        // reads the verdict below when its world updates, right after this one, and a verdict from last
        // frame's cast would let a press land on the panel a frame after the hand moved off it. The
        // laser's own update is frame-guarded, so this is the one cast. -xlinka
        _laser.RefreshNow(delta);

        // In VR the pointer owns the hand while its laser is on a panel, whatever is nearer in the world
        // (see InteractionLaser.Role), or while it is carrying something it picked up there: a widget
        // lifted off the dash rides the beam past the panel edge, and losing the hand at the edge would
        // drop it. On desktop the dash owns the one pointer for as long as it is open, hit or no hit, as
        // it always has. -xlinka
        bool active = live && (!vr || _laser.IsActive || _laserHold);
        OwnsHand = active;
        if (input != null)
        {
            float hitDistance = float.PositiveInfinity;
            if (active && _laserHold)
                hitDistance = _holdDistance;
            else if (active && _laser.IsActive)
                hitDistance = _laser.CurrentHitDistance;
            input.SetUserspaceLaserActive(Side.Value, active, live && _laser.RawHitFound);
            input.SetUserspaceLaserHit(Side.Value, hitDistance);
        }

        bool liftHand = vr && wantsPointer;
        _handOverlay.Sync(liftHand ? Engine.Current?.WorldManager?.FocusedWorld?.LocalUser?.Root : null, Side.Value, liftHand);

        UpdateGrab(input, active);
    }

    // The world hand publishes where its beam leaves the controller (tool holder plus 7.5 cm, or the
    // equipped tool's tip); the dash cursor comes off the same point so the two beams are one line. The
    // slot is a local-world child, so the write is free, and it is gated anyway because the pose only
    // changes on an equip or an avatar change. -xlinka
    private void FollowWorldTip()
    {
        if (_laserSlot == null || _laserSlot.IsDestroyed)
            return;
        HandTool.TryGetPublishedTipPose(Side.Value, out var position, out var rotation);
        if (float3.Distance(_laserSlot.LocalPosition.Value, position) > 1e-5f)
            _laserSlot.LocalPosition.Value = position;
        if (MathF.Abs(floatQ.Dot(_laserSlot.LocalRotation.Value, rotation)) < 1f - 1e-5f)
            _laserSlot.LocalRotation.Value = rotation;
    }

    // A held item rides the beam: the holder sits at the distance the grab happened at, so a widget
    // lifted off the dash slides across the dash as the pointer moves, and a keyboard grabbed from
    // across the room stays across the room. Late update, after the laser has cast this frame.
    public override void OnLateUpdate(float delta)
    {
        base.OnLateUpdate(delta);
        if (!_laserHold || _laser == null || _grabber == null)
            return;
        if (!_grabber.IsHoldingObjects)
        {
            _laserHold = false;
            _laser.SetHoldDistance(null);
            return;
        }
        var holder = _grabber.HolderSlot;
        if (holder == null)
            return;
        holder.GlobalPosition = _laser.RayOrigin + _laser.RayDirection * _holdDistance;
        holder.GlobalRotation = HoldRotation(HeldPanel != null);
    }

    // Grab for userspace items. The dash canvas gets first refusal on both ends of the gesture: in edit
    // mode a grab over a widget lifts that widget off its grid, and letting go over a grid puts the
    // carried widget down on it. Otherwise the grab takes whatever grabbable the laser is on (the
    // keyboard opts in, the dash opts out, the world is out of reach past the exclusive root) and the
    // release drops it where it sits. -xlinka
    private void UpdateGrab(InputInterface? input, bool active)
    {
        if (_grabber == null || _laser == null)
            return;

        bool held = active && input?.Actions?.Interaction(Side.Value).Grab.Held == true;
        if (held && !_prevGrabHeld)
            BeginGrab();
        else if (!held && _prevGrabHeld)
            EndGrab();
        if (!active && _grabber.IsHoldingObjects)
            EndGrab();
        _prevGrabHeld = held;
    }

    private void BeginGrab()
    {
        IGrabbable? grabbable = null;
        if (_laser!.CurrentPointerTarget is DashSurfacePortal portal)
            grabbable = portal.TryGrab(_laser);
        grabbable ??= FindGrabbable(_laser.CurrentHitSlot ?? (_laser.CurrentTarget as Component)?.Slot);
        if (grabbable == null)
            return;

        // Park the holder on the point the beam hit before taking the item, so the item keeps its offset
        // from where it was taken and follows the beam from there.
        _holdDistance = MathF.Max(MinHoldDistance, _laser.CurrentHitDistance);
        var holder = _grabber!.HolderSlot;
        if (holder != null)
        {
            holder.GlobalPosition = _laser.RayOrigin + _laser.RayDirection * _holdDistance;
            holder.GlobalRotation = HoldRotation(WidgetPanel.From(grabbable) != null);
        }
        if (_grabber.TryGrab(grabbable))
            _laserHold = true;
    }

    private void EndGrab()
    {
        if (_grabber!.IsHoldingObjects && _laser!.CurrentPointerTarget is DashSurfacePortal portal)
        {
            // A copy: a grid that takes an item releases it from this grabber mid-walk.
            var items = new List<IGrabbable>(_grabber.GrabbedObjects);
            portal.TryReceive(items, _laser);
        }
        _grabber.ReleaseAll();
        _laserHold = false;
        _laser!.SetHoldDistance(null);
    }

    // A carried widget faces the dash so it lies flat on the surface it is about to land on. Anything
    // else (the keyboard) turns with the hand like a grabbable held in the world does: the holder takes
    // the hand's rotation, and the item keeps the offset it was picked up with. -xlinka
    private floatQ HoldRotation(bool facesDash)
    {
        if (facesDash)
            return UserspaceDashboard.LocalInstance?.SurfaceSlot?.GlobalRotation ?? Slot.GlobalRotation;
        return Slot.GlobalRotation;
    }

    private IGrabbable? FindGrabbable(Slot? start)
    {
        for (var s = start; s != null; s = s.Parent)
        {
            var grabbable = s.GetComponent<Grabbable>();
            if (grabbable != null && grabbable.CanGrab(_grabber!))
                return grabbable;
        }
        return null;
    }

    private bool ReadPrimary(InputInterface input)
    {
        return input.Actions?.Interaction(Side.Value).Primary.Held == true;
    }

    public override void OnDestroy()
    {
        int index = SideIndex(Side.Value);
        if (ReferenceEquals(_bySide[index], this))
            _bySide[index] = null;
        _handOverlay.RestoreAll();
        var input = Engine.Current?.InputInterface;
        input?.SetUserspaceLaserActive(Side.Value, false, false);
        input?.SetUserspaceLaserHit(Side.Value, float.PositiveInfinity);
        base.OnDestroy();
    }
}
