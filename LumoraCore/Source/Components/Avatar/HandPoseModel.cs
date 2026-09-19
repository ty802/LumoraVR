// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// A flat, open hand in hand space (see IHandPoseSource): metacarpal bases fanning out of the wrist,
// knuckles across the palm, each finger a straight run of segments with a small outward splay. Every
// named preset is this pose with curl and splay turned in through HandPoseHelper.ModifyPose, so the
// geometry only has to be plausible, not measured; segment lengths are metres off an average adult
// hand. Built for the right hand and mirrored in X for the left. -xlinka
public static class HandPoseModel
{
    private readonly struct FingerSpec
    {
        public readonly float3 Base;
        public readonly float3 Knuckle;
        public readonly float SplayRadians;
        public readonly float DropRadians;
        public readonly float Proximal;
        public readonly float Intermediate;
        public readonly float Distal;

        public FingerSpec(float3 @base, float3 knuckle, float splayDegrees, float dropDegrees,
            float proximal, float intermediate, float distal)
        {
            Base = @base;
            Knuckle = knuckle;
            SplayRadians = splayDegrees * (MathF.PI / 180f);
            DropRadians = dropDegrees * (MathF.PI / 180f);
            Proximal = proximal;
            Intermediate = intermediate;
            Distal = distal;
        }
    }

    // Right hand. +X is the thumb side; splay is positive toward it.
    private static readonly FingerSpec Thumb =
        new(new float3(0.028f, -0.010f, 0.020f), new float3(0.058f, -0.014f, 0.046f), 48f, 6f, 0.032f, 0f, 0.030f);
    private static readonly FingerSpec Index =
        new(new float3(0.016f, 0.002f, 0.018f), new float3(0.030f, 0.006f, 0.085f), 7f, 0f, 0.044f, 0.028f, 0.023f);
    private static readonly FingerSpec Middle =
        new(new float3(0.005f, 0.003f, 0.018f), new float3(0.010f, 0.007f, 0.088f), 0f, 0f, 0.047f, 0.031f, 0.025f);
    private static readonly FingerSpec Ring =
        new(new float3(-0.006f, 0.002f, 0.018f), new float3(-0.010f, 0.005f, 0.084f), -6f, 0f, 0.043f, 0.029f, 0.024f);
    private static readonly FingerSpec Pinky =
        new(new float3(-0.017f, 0f, 0.017f), new float3(-0.030f, 0.001f, 0.077f), -13f, 0f, 0.034f, 0.023f, 0.020f);

    private static FingerSpec SpecFor(FingerType finger) => finger switch
    {
        FingerType.Thumb => Thumb,
        FingerType.Index => Index,
        FingerType.Middle => Middle,
        FingerType.Ring => Ring,
        _ => Pinky,
    };

    // Fills one side's flat open pose: 24 positions and the rotations that follow from them.
    public static void BuildOpenPose(Chirality side, float3[] positions, floatQ[] rotations)
    {
        float mirror = side == Chirality.Left ? -1f : 1f;
        foreach (var finger in HandPoseHelper.Fingers)
        {
            var spec = SpecFor(finger);
            float3 direction = floatQ.AxisAngle(float3.Up, spec.SplayRadians)
                * (floatQ.AxisAngle(float3.Right, spec.DropRadians) * float3.Forward);

            float3 point = spec.Knuckle;
            Set(positions, finger, FingerSegmentType.Metacarpal, Mirror(spec.Base, mirror));
            Set(positions, finger, FingerSegmentType.Proximal, Mirror(point, mirror));

            if (finger == FingerType.Thumb)
            {
                point += direction * spec.Proximal;
                Set(positions, finger, FingerSegmentType.Distal, Mirror(point, mirror));
                point += direction * spec.Distal;
                Set(positions, finger, FingerSegmentType.Tip, Mirror(point, mirror));
            }
            else
            {
                point += direction * spec.Proximal;
                Set(positions, finger, FingerSegmentType.Intermediate, Mirror(point, mirror));
                point += direction * spec.Intermediate;
                Set(positions, finger, FingerSegmentType.Distal, Mirror(point, mirror));
                point += direction * spec.Distal;
                Set(positions, finger, FingerSegmentType.Tip, Mirror(point, mirror));
            }
        }
        HandPoseHelper.RotationsFromPositions(side, positions, rotations);
    }

    private static float3 Mirror(float3 point, float mirror) => new(point.x * mirror, point.y, point.z);

    private static void Set(float3[] positions, FingerType finger, FingerSegmentType segment, float3 point)
        => positions[HandPoseHelper.IndexOf(finger, segment)] = point;
}
