// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.UI;
using Lumora.Core.Input;

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

    private InteractionLaser? _laser;

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
        _laser.SetIgnoreRoot(Slot);        // never let the pointer trip over its own rig
        _laser.ShowDesktopBeam.Value = false;
        _laser.SetBeamSuppressed(true);    // just the cursor on the panel, no laser line stabbing out
        _laser.SetDormant(true);           // parked until the dash opens
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (_laser == null) return;

        var input = Engine.Current?.InputInterface;
        // Desktop has one mouse cursor, so only the right hand drives the dash there. In VR each
        // hand has its own tracked laser, so both hands are live and whichever one you point at the
        // dash shows a cursor on it. -xlinka
        bool active = input != null && input.IsDashboardOpen
            && (input.IsVRActive || Side.Value == Chirality.Right);

        // Feed state BEFORE the laser's own update (it runs a touch later), so when it casts this
        // frame it sees the fresh press state and the dash as its only valid target. -xlinka
        _laser.SetDormant(!active);
        if (active)
        {
            _laser.SetExclusiveRoot(UserspaceDashboard.LocalInstance?.SurfaceSlot);
            _laser.SetToolState(ReadPrimary(input!), false);
        }

        input?.SetUserspaceLaserActive(Side.Value, active, active && _laser.IsActive);
    }

    private bool ReadPrimary(InputInterface input)
    {
        if (input.IsVRActive)
        {
            var controller = Side.Value == Chirality.Left ? input.LeftController : input.RightController;
            return controller != null && controller.TriggerPressed;
        }
        return input.Mouse?.LeftButton.Held == true;
    }

    public override void OnDestroy()
    {
        Engine.Current?.InputInterface?.SetUserspaceLaserActive(Side.Value, false, false);
        base.OnDestroy();
    }
}
