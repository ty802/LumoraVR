// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

// A hand socket is a wrist pose, not the controller's aim pose. Run after its parent controller's
// positioner and publish the resulting local transform through the hand's normal tracking streams.
[ComponentCategory("Users")]
[DefaultUpdateOrder(-900000)]
public sealed class HandTrackingDriver : UserRootComponent, IInputUpdateReceiver
{
    public readonly Sync<Chirality> Side = new();
    public readonly Sync<float3> PositionOffset = new();
    public readonly Sync<floatQ> RotationOffset = new();
    private InputInterface? _registeredInput;
    private bool _heldTrackedPose;

    public override void OnInit()
    {
        base.OnInit();
        Side.Value = Chirality.Left;
        RotationOffset.Value = floatQ.Identity;
    }

    public override void OnStart()
    {
        base.OnStart();
        RegisterInput();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        RegisterInput();
    }

    private void RegisterInput()
    {
        var input = IsUnderLocalUser ? Engine.Current?.InputInterface : null;
        if (ReferenceEquals(input, _registeredInput))
            return;
        _registeredInput?.UnregisterInputEventReceiver(this);
        _registeredInput = input;
        _registeredInput?.RegisterInputEventReceiver(this);
    }

    public void BeforeInputUpdate()
    {
        if (!IsUnderLocalUser || World == null || World.Focus == World.WorldFocus.Background)
            return;
        ApplyHand(_registeredInput?.GetHand(Side.Value));
    }

    private void ApplyHand(HandDevice? hand)
    {
        float3 localPosition = PositionOffset.Value;
        floatQ localRotation = RotationOffset.Value;
        float3 position = default;
        floatQ rotation = floatQ.Identity;
        bool tracking = hand != null && hand.TryGetSocketPose(out position, out rotation);
        if (tracking && Slot.Parent is { } parent)
        {
            var space = hand!.Wrist.TrackingSpace;
            var worldPosition = space?.Transform(position) ?? position;
            var worldRotation = space?.Transform(rotation) ?? rotation;
            localPosition = parent.GlobalPointToLocal(worldPosition) + parent.GlobalRotationToLocal(worldRotation) * PositionOffset.Value;
            localRotation = (parent.GlobalRotationToLocal(worldRotation) * RotationOffset.Value).Normalized;
        }

        // A hand that loses tracking in VR keeps the wrist pose it had: the controller slot above holds
        // its last transform, and resetting this one to the bare offset would jump the wrist by its
        // skeleton offset the instant the cameras lose the controller, then back on reacquire. The
        // arm holds through the gap (AvatarIK keeps it weighted), so the wrist holds with it. Off VR
        // the reset stands, so the desktop hand sits at its authored offset. -xlinka
        bool vr = _registeredInput?.IsVRActive == true;
        if (tracking)
            _heldTrackedPose = vr;
        else if (!vr)
            _heldTrackedPose = false;
        if (tracking || !_heldTrackedPose)
        {
            Slot.LocalPosition.SetValueSilently(localPosition, change: true);
            Slot.LocalRotation.SetValueSilently(localRotation, change: true);
        }
        if (Slot.GetComponent<Avatar.AvatarSocket>() is { } socket)
        {
            socket.IsTracking.Value = tracking;
            socket.IsActive.Value = _registeredInput?.IsVRActive != true || hand?.IsDeviceActive == true;
        }
    }

    public void AfterInputUpdate() { }

    public override void OnDestroy()
    {
        _registeredInput?.UnregisterInputEventReceiver(this);
        _registeredInput = null;
        base.OnDestroy();
    }
}
