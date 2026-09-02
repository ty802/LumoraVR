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

        _laser = Slot.GetComponent<InteractionLaser>() ?? Slot.AttachComponent<InteractionLaser>();
        _laser.ControllerSide.Value = Side.Value;
        _laser.SetIgnoreRoot(Slot);        // never let the pointer trip over its own rig, held items included
        _laser.ShowDesktopBeam.Value = false;
        _laser.SetBeamSuppressed(true);    // just the cursor on the panel, no laser line stabbing out
        _laser.SetDormant(true);           // parked until the dash opens
        _grabber = Slot.GetComponent<Grabber>() ?? Slot.AttachComponent<Grabber>();
        _bySide[SideIndex(Side.Value)] = this;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (_laser == null) return;

        var input = Engine.Current?.InputInterface;
        // Two things can want this pointer: the dash, and the VR keyboard, which shows itself whenever
        // a text field takes focus and needs a cursor of its own to be typed on. The keyboard term is
        // VR-gated so desktop behaves exactly as it did - dash-driven, right hand only. Desktop has one
        // mouse cursor; in VR each hand has its own tracked laser, so both are live and whichever one
        // you point at a panel shows a cursor on it. -xlinka
        bool wantsPointer = input != null
            && (input.IsDashboardOpen || (input.IsVRActive && VrKeyboard.LocalShown));
        bool active = wantsPointer && (input!.IsVRActive || Side.Value == Chirality.Right);

        // Feed state BEFORE the laser's own update (it runs a touch later), so when it casts this
        // frame it sees the fresh press state and userspace as its only valid target. -xlinka
        _laser.SetDormant(!active);
        if (active)
        {
            // The whole userspace subtree, not just the dash surface: the keyboard is a sibling of the
            // dash, and restricting to one of them makes the other unclickable. Everything outside
            // userspace stays out of reach either way, which is the point of the restriction. -xlinka
            _laser.SetExclusiveRoot(Templates.Userspace.LocalRoot ?? UserspaceDashboard.LocalInstance?.SurfaceSlot);
            _laser.SetToolState(ReadPrimary(input!), false);
        }

        input?.SetUserspaceLaserActive(Side.Value, active, active && _laser.IsActive);
        UpdateGrab(input, active);
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
            return;
        }
        var holder = _grabber.HolderSlot;
        if (holder == null)
            return;
        holder.GlobalPosition = _laser.RayOrigin + _laser.RayDirection * _holdDistance;
        holder.GlobalRotation = HoldRotation();
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
            holder.GlobalRotation = HoldRotation();
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
    }

    // Held items face the dash while it is up, so a widget carried over it lies flat on the surface.
    private floatQ HoldRotation()
        => UserspaceDashboard.LocalInstance?.SurfaceSlot?.GlobalRotation ?? Slot.GlobalRotation;

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
        Engine.Current?.InputInterface?.SetUserspaceLaserActive(Side.Value, false, false);
        base.OnDestroy();
    }
}
