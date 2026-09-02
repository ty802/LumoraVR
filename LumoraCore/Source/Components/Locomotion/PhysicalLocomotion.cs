// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

// One module for stick AND keyboard. Reads movement / turn / jump through
// LocomotionInputHelper which collapses the input source decision behind a
// uniform interface, then drives the CharacterController. Snap turn comes
// from TurnSubmodule (active only when a stick is present, desktop just gets
// nothing on that axis because mouse-look already handles yaw).
// - xlinka
[ComponentCategory("Users/Locomotion")]
public class PhysicalLocomotion : SmoothLocomotionBase
{
    public override string DisplayName => "Walk";
    public override bool CanActivate() => true;

    private CharacterController _characterController = null!;
    private bool _wasJumpPressed;
    private bool _isCrouching;
    private bool _isSprinting;

    protected override void OnActivated()
    {
        base.OnActivated();
        _characterController = Owner?.CharacterController!;
        _wasJumpPressed = false;
        _isCrouching = false;
        _isSprinting = false;

        // Resume gravity/collision in case a non-physical module (noclip) left
        // it suspended.
        _characterController?.SetSimulationEnabled(true);

        // Desktop look needs mouse capture; in VR the headset drives view.
        if (!IsVRActive())
            Owner?.InputState?.SetMouseCaptureRequested(true);
    }

    protected override void OnDeactivated()
    {
        _characterController = null!;
        base.OnDeactivated();
    }

    public override void OnModuleUpdate(float delta)
    {
        if (Owner == null || _characterController == null || !_characterController.IsReady)
            return;

        if (InputBlocked)
        {
            _characterController.SetMovementDirection(float3.Zero);
            return;
        }

        // Seated. The legs are parked but the user can still turn where they sit, so the turn axis keeps
        // being serviced and jump is left alone - jump is how the seat lets go.
        if (MovementBlocked)
        {
            _characterController.SetMovementDirection(float3.Zero);
            ApplyTurn(delta);
            return;
        }

        if (InputInterface == null && Engine.Current?.InputInterface != null)
        {
            // The base class does the late bind; this is only a defensive null check.
            return;
        }

        ApplyMovement();
        ApplyTurn(delta);
        ApplyActions();
    }

    private void ApplyMovement()
    {
        var rawAxis = LocomotionInputHelper.ReadMovementAxis(InputInterface);
        if (ExclusiveAxisMode)
            rawAxis = LocomotionInputHelper.SnapToDominantAxis(rawAxis);
        var axis = LocomotionInputHelper.ApplyDeadzone(rawAxis, MovementDeadzone);

        float3 moveDir = float3.Zero;
        if (axis.LengthSquared > 0.001f)
        {
            Owner.GetMovementBasis(out var forward, out var right);
            moveDir = (forward * axis.y + right * axis.x).Normalized;
        }
        _characterController.SetMovementDirection(moveDir);
    }

    private void ApplyTurn(float delta)
    {
        // Desktop has no stick; ReadTurnAxis returns 0 and TurnSubmodule no-ops.
        // VR feeds right-stick X here.
        float axis = LocomotionInputHelper.ReadTurnAxis(InputInterface);
        Turn.Update(axis, delta);
    }

    private void ApplyActions()
    {
        bool jumpPressed = LocomotionInputHelper.ReadJump(InputInterface);
        if (jumpPressed && !_wasJumpPressed && !_isCrouching)
            _characterController.RequestJump();
        _wasJumpPressed = jumpPressed;

        bool crouchPressed = LocomotionInputHelper.ReadCrouch(InputInterface);
        if (crouchPressed != _isCrouching)
        {
            _isCrouching = crouchPressed;
            _characterController.SetCrouching(_isCrouching);
        }

        bool sprintPressed = LocomotionInputHelper.ReadSprint(InputInterface);
        bool wantsSprint = sprintPressed && !_isCrouching;
        if (wantsSprint != _isSprinting)
        {
            _isSprinting = wantsSprint;
            _characterController.SetSprinting(_isSprinting);
        }
    }

    private bool IsVRActive()
    {
        var input = InputInterface ?? Engine.Current?.InputInterface;
        if (input == null) return false;
        bool controllersTracked = input.LeftController?.IsTracked == true || input.RightController?.IsTracked == true;
        bool headTracked = input.HeadDevice?.IsTracked == true;
        return input.VR_Active && (controllersTracked || headTracked);
    }
}
