// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

// Free flight that ignores gravity and collision: moves the user rig's global
// position directly along the full view direction, so looking up/down flies
// up/down. Space/C add explicit world-vertical, Shift sprints. No character
// controller involvement - the rig is moved straight, same model as the
// reference Noclip module. - xlinka
public class NoclipLocomotion : SmoothLocomotionBase
{
    public override string DisplayName => "Noclip";
    public override bool CanActivate() => true;

    public float SprintMultiplier { get; set; } = 3f;

    protected override void OnActivated()
    {
        base.OnActivated();
        // Take the character controller out of simulation: noclip moves the rig
        // directly, so gravity/collision stepping must not run or it'd pull us
        // back down and cancel flight.
        Owner?.CharacterController?.SetSimulationEnabled(false);
        // Desktop look needs mouse capture; in VR the headset drives view.
        if (!IsVRActive())
            Owner?.InputState?.SetMouseCaptureRequested(true);
    }

    protected override void OnDeactivated()
    {
        Owner?.CharacterController?.SetSimulationEnabled(true);
        base.OnDeactivated();
    }

    public override void OnModuleUpdate(float delta)
    {
        var userRoot = Owner?.UserRoot;
        if (Owner == null || userRoot == null)
            return;

        var state = Owner.InputState;
        if ((state?.FreeCamActive ?? false) || (state?.DesktopInputSuppressed ?? false))
            return;

        var rawAxis = LocomotionInputHelper.ReadMovementAxis(InputInterface, KeyboardDriver);
        var axis = LocomotionInputHelper.ApplyDeadzone(rawAxis, MovementDeadzone);
        float vertical = LocomotionInputHelper.ReadVerticalAxis(InputInterface, KeyboardDriver);

        // Full view direction (includes pitch), so looking up flies up - the
        // horizontal+pitch half of the 3-axis move.
        float3 forward = userRoot.HeadFacingRotation * float3.Backward;
        if (forward.LengthSquared < 1e-6f)
            forward = float3.Backward;
        forward = forward.Normalized;

        float3 right = float3.Cross(forward, float3.Up);
        if (right.LengthSquared < 1e-4f)
            right = float3.Right;
        right = right.Normalized;

        // Third axis: explicit world-vertical (VR right-stick Y, desktop Space/C).
        float3 move = forward * axis.y + right * axis.x + float3.Up * vertical;

        if (move.LengthSquared < 1e-5f)
        {
            // Still service turn even when not translating.
            Turn.Update(LocomotionInputHelper.ReadTurnAxis(InputInterface), delta);
            return;
        }
        if (move.LengthSquared > 1f)
            move = move.Normalized;

        float speed = EngineSettings.NoclipSpeed;
        if (LocomotionInputHelper.ReadSprint(KeyboardDriver))
            speed *= SprintMultiplier;

        // Scale flight speed with the user's own scale so it feels consistent
        // when shrunk or grown.
        float scaledSpeed = speed * (userRoot.GlobalScale <= 0f ? 1f : userRoot.GlobalScale);
        userRoot.Slot.GlobalPosition += move * scaledSpeed * delta;

        // Turn still applies (VR right-stick X; desktop mouse-look owns yaw).
        Turn.Update(LocomotionInputHelper.ReadTurnAxis(InputInterface), delta);
    }

    private bool IsVRActive()
    {
        var input = InputInterface ?? Engine.Current?.InputInterface;
        return input?.VR_Active == true;
    }
}
