// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Input;

// What a controller without finger sensing knows about the hand holding it.
public struct ControllerHandInputs
{
    public float Trigger;
    public bool TriggerTouched;
    public float Grip;
    public bool ThumbTouched;
    // The controller has reported a thumb touch at least once, so a thumb it does not report is lifted.
    public bool SensesThumb;
    public float StickX;
}

// The hand a controller implies, built the same way a real skeleton is read: an open preset in hand-root
// space, curled joint by joint. Every pose here is hand-root relative, in engine axes: -Z forward, +Y up,
// +X right, right-handed. Segment frames follow the tracked-skeleton convention so both feeds look the
// same downstream: local -Z runs along the bone toward the next segment, local +Y points out of the back
// of the hand, local +X completes the right-handed set.
//
// The open preset is authored for the right hand as it sits on a controller in that controller's pose
// space (the pose the controller body node carries): palm facing -X, thumb on top at +Y, index at the top
// of the fan and pinky at the bottom, fingers reaching forward past the tracked origin. The left hand is
// the same layout mirrored in X. -xlinka
public static class HandPoseSynth
{
    public const int SegmentCount = 24;
    public const int FingerCount = 5;

    // Per-segment finger and type, in body node order (thumb has no intermediate).
    public static readonly FingerType[] SegmentFinger = BuildSegmentFingers();
    public static readonly FingerSegmentType[] SegmentType = BuildSegmentTypes();
    public static readonly int[] ParentIndex = BuildParents();

    // Curl at 1 folds every non-metacarpal joint by this much. Three finger joints at this angle bring the
    // tip back to the palm; the thumb has two and folds over the fingers rather than into them.
    public const float FingerFullCurl = 1.35f;
    public const float ThumbFullCurl = 0.9f;

    // A stick pushed fully outward fans the fingers by this much at the finger that moves most.
    public const float StickSplayMax = 0.26f;

    // How far each finger swings for one unit of fan, positive toward the pinky side. The index swings
    // the other way so the fan opens about the gap between index and middle.
    public static readonly float[] SplayFan = { 0f, -0.7f, 0.25f, 0.7f, 1f };

    // Controller estimate. Trigger owns the index; the squeeze owns the other three with the pinky side
    // leading, because a handle is narrowest under the pinky and those fingers close first on a partial
    // squeeze. A full squeeze still closes all three. Touch alone rests a finger on its control. -xlinka
    public const float IndexTouchCurl = 0.15f;
    public const float ThumbTouchCurl = 0.45f;
    public const float ThumbGripGain = 0.6f;
    // On a controller that senses the thumb, a thumb lifted off the buttons is up, grip or no grip. One
    // that never reports a touch folds the thumb in with the squeeze as before.
    public const float ThumbLiftedGripGain = 0.15f;
    public static readonly float[] GripCurlGain = { 0f, 0f, 1f, 1.08f, 1.15f };

    private struct FingerSpec
    {
        public float3 Metacarpal;
        public float3 Knuckle;
        public float3 Direction;
        public float3 Up;
        public float Proximal;
        public float Intermediate;
        public float Distal;
    }

    private static readonly FingerSpec[] RightHandSpec =
    {
        // Thumb: metacarpal near the wrist, knuckle high on the palm side, reaching up and forward.
        new FingerSpec
        {
            Metacarpal = new float3(-0.006f, 0.020f, 0.088f),
            Knuckle = new float3(-0.010f, 0.040f, 0.045f),
            Direction = new float3(-0.35f, 0.45f, -0.80f).Normalized,
            Up = float3.Up,
            Proximal = 0.036f, Intermediate = 0f, Distal = 0.030f,
        },
        new FingerSpec
        {
            Metacarpal = new float3(0.000f, 0.012f, 0.085f),
            Knuckle = new float3(0.000f, 0.022f, 0.020f),
            Direction = new float3(0f, 0.08f, -1f).Normalized,
            Up = float3.Right,
            Proximal = 0.040f, Intermediate = 0.024f, Distal = 0.020f,
        },
        new FingerSpec
        {
            Metacarpal = new float3(0.000f, 0.004f, 0.086f),
            Knuckle = new float3(0.000f, 0.008f, 0.016f),
            Direction = new float3(0f, 0f, -1f),
            Up = float3.Right,
            Proximal = 0.045f, Intermediate = 0.028f, Distal = 0.021f,
        },
        new FingerSpec
        {
            Metacarpal = new float3(0.000f, -0.004f, 0.087f),
            Knuckle = new float3(0.000f, -0.006f, 0.019f),
            Direction = new float3(0f, -0.08f, -1f).Normalized,
            Up = float3.Right,
            Proximal = 0.041f, Intermediate = 0.026f, Distal = 0.020f,
        },
        new FingerSpec
        {
            Metacarpal = new float3(0.000f, -0.012f, 0.089f),
            Knuckle = new float3(0.000f, -0.019f, 0.026f),
            Direction = new float3(0f, -0.17f, -1f).Normalized,
            Up = float3.Right,
            Proximal = 0.032f, Intermediate = 0.020f, Distal = 0.018f,
        },
    };

    private static readonly float3 RightPalmPosition = new float3(-0.004f, 0.003f, 0.052f);
    private static readonly float3 RightWristPosition = new float3(0.000f, 0.000f, 0.105f);

    public const float DefaultJointRadius = 0.009f;

    public static float FullCurl(FingerType finger)
        => finger == FingerType.Thumb ? ThumbFullCurl : FingerFullCurl;

    public static float3 PalmPosition(Chirality side) => Mirror(RightPalmPosition, side);

    public static floatQ PalmRotation(Chirality side)
        => FrameFromForwardUp(float3.Backward, BackOfHand(side));

    public static float3 WristPosition(Chirality side) => Mirror(RightWristPosition, side);

    public static floatQ WristRotation(Chirality side)
        => FrameFromForwardUp(float3.Backward, BackOfHand(side));

    public static float3 BackOfHand(Chirality side)
        => side == Chirality.Left ? float3.Left : float3.Right;

    // The open hand, flat (hand-root relative). Rotations are the segment frames described above.
    public static void OpenPose(Chirality side, Span<float3> positions, Span<floatQ> rotations)
    {
        CheckSpans(positions, rotations);
        for (int f = 0; f < FingerCount; f++)
        {
            var finger = (FingerType)f;
            var spec = RightHandSpec[f];
            float3 meta = Mirror(spec.Metacarpal, side);
            float3 knuckle = Mirror(spec.Knuckle, side);
            float3 dir = Mirror(spec.Direction, side);
            float3 up = Mirror(spec.Up, side);
            floatQ boneFrame = FrameFromForwardUp(dir, up);

            Set(positions, rotations, finger, FingerSegmentType.Metacarpal, meta, FrameFromForwardUp(knuckle - meta, up));
            float3 pos = knuckle;
            Set(positions, rotations, finger, FingerSegmentType.Proximal, pos, boneFrame);
            pos += dir * spec.Proximal;
            if (finger != FingerType.Thumb)
            {
                Set(positions, rotations, finger, FingerSegmentType.Intermediate, pos, boneFrame);
                pos += dir * spec.Intermediate;
            }
            Set(positions, rotations, finger, FingerSegmentType.Distal, pos, boneFrame);
            pos += dir * spec.Distal;
            Set(positions, rotations, finger, FingerSegmentType.Tip, pos, boneFrame);
        }
    }

    private static void Set(Span<float3> positions, Span<floatQ> rotations, FingerType finger, FingerSegmentType seg, float3 p, floatQ r)
    {
        int i = IndexOf(finger, seg);
        positions[i] = p;
        rotations[i] = r;
    }

    // Flat (every segment relative to the hand root) to hierarchical (every segment relative to the one
    // before it; metacarpals stay root relative).
    public static void ToHierarchy(Span<float3> positions, Span<floatQ> rotations)
    {
        CheckSpans(positions, rotations);
        Span<float3> flatPos = stackalloc float3[SegmentCount];
        Span<floatQ> flatRot = stackalloc floatQ[SegmentCount];
        positions.CopyTo(flatPos);
        rotations.CopyTo(flatRot);
        for (int i = 0; i < SegmentCount; i++)
        {
            int parent = ParentIndex[i];
            if (parent < 0)
                continue;
            floatQ inv = flatRot[parent].Inverse;
            positions[i] = inv * (flatPos[i] - flatPos[parent]);
            rotations[i] = inv * flatRot[i];
        }
    }

    public static void ToFlat(Span<float3> positions, Span<floatQ> rotations)
    {
        CheckSpans(positions, rotations);
        // Parents precede children in body node order, so one forward pass composes every chain.
        for (int i = 0; i < SegmentCount; i++)
        {
            int parent = ParentIndex[i];
            if (parent < 0)
                continue;
            positions[i] = rotations[parent] * positions[i] + positions[parent];
            rotations[i] = rotations[parent] * rotations[i];
        }
    }

    // A flexion of `angle` radians about the parent's knuckle axis. Local +X is that axis and the bone
    // runs down -Z with +Y out of the back of the hand, so in a right-handed frame the positive turn
    // about +X lifts the tip toward the back of the hand; flexion toward the palm is the negative turn. -xlinka
    public static floatQ CurlRotation(float angle)
        => floatQ.AxisAngle(float3.Right, -angle);

    // Splay swings a finger sideways in the palm plane, about the parent's back-of-hand axis. The sign
    // that moves a finger toward the pinky side flips with the hand because the two hands' frames are
    // mirror images: on the right hand the positive turn about local +Y swings toward the thumb. -xlinka
    public static floatQ SplayRotation(Chirality side, float towardPinky)
        => floatQ.AxisAngle(float3.Up, side == Chirality.Right ? -towardPinky : towardPinky);

    // The curl model on a hierarchical pose: every non-metacarpal segment turns by its finger's curl,
    // the proximal alone also takes the finger's splay. Angles in radians, splay positive toward the
    // pinky side.
    public static void ModifyPose(Chirality side, Span<floatQ> hierarchicalRotations,
        ReadOnlySpan<float> curlAngles, ReadOnlySpan<float> splayAngles)
    {
        if (hierarchicalRotations.Length != SegmentCount || curlAngles.Length != FingerCount || splayAngles.Length != FingerCount)
            throw new ArgumentException("Pose spans must hold 24 segments and 5 finger angles");

        for (int i = 0; i < SegmentCount; i++)
        {
            var seg = SegmentType[i];
            if (seg == FingerSegmentType.Metacarpal)
                continue;
            int f = (int)SegmentFinger[i];
            hierarchicalRotations[i] = CurlRotation(curlAngles[f]) * hierarchicalRotations[i];
            if (seg == FingerSegmentType.Proximal && splayAngles[f] != 0f)
                hierarchicalRotations[i] = SplayRotation(side, splayAngles[f]) * hierarchicalRotations[i];
        }
    }

    // Open preset, curled. curl01 is 0 open to 1 fist per finger, splay in radians toward the pinky side.
    public static void Synthesize(Chirality side, ReadOnlySpan<float> curl01, ReadOnlySpan<float> splay,
        Span<float3> positions, Span<floatQ> rotations)
    {
        if (curl01.Length != FingerCount || splay.Length != FingerCount)
            throw new ArgumentException("Five curls and five splays");

        OpenPose(side, positions, rotations);
        ToHierarchy(positions, rotations);
        Span<float> angles = stackalloc float[FingerCount];
        for (int f = 0; f < FingerCount; f++)
            angles[f] = System.Math.Clamp(curl01[f], 0f, 1f) * FullCurl((FingerType)f);
        ModifyPose(side, rotations, angles, splay);
        ToFlat(positions, rotations);
    }

    // Controller inputs to per-finger curl and splay.
    public static void Estimate(in ControllerHandInputs inputs, Chirality side, Span<float> curl01, Span<float> splay)
    {
        if (curl01.Length != FingerCount || splay.Length != FingerCount)
            throw new ArgumentException("Five curls and five splays");

        float trigger = System.Math.Clamp(inputs.Trigger, 0f, 1f);
        float grip = System.Math.Clamp(inputs.Grip, 0f, 1f);

        curl01[(int)FingerType.Index] = MathF.Max(trigger, inputs.TriggerTouched ? IndexTouchCurl : 0f);
        curl01[(int)FingerType.Thumb] = inputs.ThumbTouched || !inputs.SensesThumb
            ? MathF.Max(inputs.ThumbTouched ? ThumbTouchCurl : 0f, grip * ThumbGripGain)
            : grip * ThumbLiftedGripGain;
        for (int f = (int)FingerType.Middle; f <= (int)FingerType.Pinky; f++)
            curl01[f] = System.Math.Clamp(grip * GripCurlGain[f], 0f, 1f);

        // Outward on the stick is +X on the right hand and -X on the left; outward opens the fan.
        float outward = System.Math.Clamp(inputs.StickX, -1f, 1f) * (side == Chirality.Left ? -1f : 1f);
        float fan = outward * StickSplayMax;
        for (int f = 0; f < FingerCount; f++)
            splay[f] = SplayFan[f] * fan;
    }

    public static int IndexOf(FingerType finger, FingerSegmentType segment)
        => finger.ComposeFinger(segment, Chirality.Left).GetFingerSegmentIndex();

    public static float3 Mirror(float3 right, Chirality side)
        => side == Chirality.Left ? new float3(-right.x, right.y, right.z) : right;

    // A frame whose -Z is `forward` and whose +Y is `up` squared against it. Built by hand because
    // floatQ.LookRotation hands back the inverse of the frame it is asked for. -xlinka
    public static floatQ FrameFromForwardUp(float3 forward, float3 up)
    {
        float3 back = -forward;
        if (back.LengthSquared < 1e-12f)
            return floatQ.Identity;
        back = back.Normalized;
        float3 right = float3.Cross(up, back);
        if (right.LengthSquared < 1e-10f)
        {
            float3 alt = MathF.Abs(back.y) < 0.9f ? float3.Up : float3.Right;
            right = float3.Cross(alt, back);
        }
        right = right.Normalized;
        float3 y = float3.Cross(back, right);
        return FromBasis(right, y, back);
    }

    // Columns are the frame's axes expressed in the parent space.
    private static floatQ FromBasis(float3 x, float3 y, float3 z)
    {
        float m00 = x.x, m01 = y.x, m02 = z.x;
        float m10 = x.y, m11 = y.y, m12 = z.y;
        float m20 = x.z, m21 = y.z, m22 = z.z;
        float trace = m00 + m11 + m22;
        floatQ q;
        if (trace > 0f)
        {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            q = new floatQ((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
        }
        else if (m00 > m11 && m00 > m22)
        {
            float s = MathF.Sqrt(1f + m00 - m11 - m22) * 2f;
            q = new floatQ(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
        }
        else if (m11 > m22)
        {
            float s = MathF.Sqrt(1f + m11 - m00 - m22) * 2f;
            q = new floatQ((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
        }
        else
        {
            float s = MathF.Sqrt(1f + m22 - m00 - m11) * 2f;
            q = new floatQ((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
        }
        return q.Normalized;
    }

    private static void CheckSpans(Span<float3> positions, Span<floatQ> rotations)
    {
        if (positions.Length != SegmentCount || rotations.Length != SegmentCount)
            throw new ArgumentException("Pose spans must hold 24 segments");
    }

    private static FingerType[] BuildSegmentFingers()
    {
        var result = new FingerType[SegmentCount];
        for (int i = 0; i < SegmentCount; i++)
            result[i] = ((BodyNode)((int)BodyNode.LEFT_FINGER_START + i)).GetFingerType();
        return result;
    }

    private static FingerSegmentType[] BuildSegmentTypes()
    {
        var result = new FingerSegmentType[SegmentCount];
        for (int i = 0; i < SegmentCount; i++)
            result[i] = ((BodyNode)((int)BodyNode.LEFT_FINGER_START + i)).GetFingerSegmentType();
        return result;
    }

    private static int[] BuildParents()
    {
        var result = new int[SegmentCount];
        for (int i = 0; i < SegmentCount; i++)
        {
            var finger = SegmentFinger[i];
            var seg = SegmentType[i];
            result[i] = seg switch
            {
                FingerSegmentType.Metacarpal => -1,
                FingerSegmentType.Proximal => IndexOf(finger, FingerSegmentType.Metacarpal),
                FingerSegmentType.Intermediate => IndexOf(finger, FingerSegmentType.Proximal),
                FingerSegmentType.Distal => IndexOf(finger, finger == FingerType.Thumb ? FingerSegmentType.Proximal : FingerSegmentType.Intermediate),
                _ => IndexOf(finger, FingerSegmentType.Distal),
            };
        }
        return result;
    }
}
