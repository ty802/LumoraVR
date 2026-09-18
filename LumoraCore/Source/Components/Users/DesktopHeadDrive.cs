// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components;

// The single writer of the local user's head slot on a screen. Holds the head simulation and stamps
// its answer onto the head in the head's own parent frame; the locomotion controller feeds it once a
// frame after mouse look and before the body turn, and takes the simulated face heading back for the
// root in third person. Lives on the local user's root next to UserInputState, attached the same way.
// A headset-tracked head never comes through here: the positioner owns the slot then. -xlinka
[ComponentCategory("Users")]
public class DesktopHeadDrive : UserRootComponent
{
    private const float PositionThresholdSq = 0.0001f * 0.0001f;
    private const float RotationThreshold = 0.0000001f;

    public DesktopHeadSimulation Simulation { get; } = new();

    public DesktopViewMode Mode => Simulation.Mode;
    public float HeadYaw => Simulation.HeadYaw;
    public float HeadPitch => Simulation.HeadPitch;
    public float FaceYaw => Simulation.FaceYaw;
    public floatQ HeadRotation => Simulation.HeadRotation;
    public float Transition => Simulation.Transition;

    public static DesktopViewMode ResolveMode(UserInputState? state)
    {
        if (state == null)
            return DesktopViewMode.FirstPerson;
        if (state.FreeCamActive)
            return DesktopViewMode.FreeCam;
        if (state.ExternalCameraActive)
            return DesktopViewMode.ThirdPerson;
        return DesktopViewMode.FirstPerson;
    }

    public void Step(in DesktopHeadSimulation.Frame frame) => Simulation.Step(frame);

    public void ApplyToHead(Slot head, in float3 localPosition)
    {
        if (head == null || head.IsDestroyed)
            return;

        if ((localPosition - head.LocalPosition.Value).LengthSquared > PositionThresholdSq)
            head.LocalPosition.Value = localPosition;

        var parent = head.Parent;
        floatQ world = Simulation.HeadRotation;
        floatQ local = parent != null && !parent.IsDestroyed ? parent.GlobalRotationToLocal(world) : world;
        floatQ current = head.LocalRotation.Value;
        float dot = floatQ.Dot(local, current);
        if (1f - (dot < 0f ? -dot : dot) > RotationThreshold)
            head.LocalRotation.Value = local;
    }
}
