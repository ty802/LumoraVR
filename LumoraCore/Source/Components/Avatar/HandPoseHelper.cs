// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Array math over one hand's 24 finger nodes (index = BodyNode.GetFingerSegmentIndex). Sources hand
// out flat poses (every segment relative to the hand root); blending and curl offsets want each
// segment relative to its parent segment, so the processors go to hierarchy, work, and come back. The
// thumb has no intermediate, so its four nodes are Metacarpal, Proximal, Distal, Tip. -xlinka
public static class HandPoseHelper
{
    public const int NodesPerHand = 24;

    public static readonly FingerType[] Fingers =
    {
        FingerType.Thumb, FingerType.Index, FingerType.Middle, FingerType.Ring, FingerType.Pinky,
    };

    public static readonly Chirality[] Sides = { Chirality.Left, Chirality.Right };

    private const float Epsilon = 1e-6f;

    public static BodyNode NodeAt(Chirality side, int index)
        => (BodyNode)((int)side.FirstFingerNode() + index);

    public static int IndexOf(FingerType finger, FingerSegmentType segment)
        => finger.ComposeFinger(segment, Chirality.Left).GetFingerSegmentIndex();

    public static float3[] NewPositions() => new float3[NodesPerHand];

    public static floatQ[] NewRotations()
    {
        var rotations = new floatQ[NodesPerHand];
        Array.Fill(rotations, floatQ.Identity);
        return rotations;
    }

    // Rotation whose +Z lands on forward and whose +Y lands as close to up as forward allows. Built
    // from two swings instead of a basis matrix because floatQ.LookRotation hands back the transpose.
    public static floatQ FrameRotation(float3 forward, float3 up)
    {
        if (forward.LengthSquared < Epsilon)
            return floatQ.Identity;
        forward = forward.Normalized;

        floatQ swing = FromToRotation(float3.Forward, forward);
        float3 currentUp = swing * float3.Up;
        float3 upInPlane = up - forward * float3.Dot(up, forward);
        if (upInPlane.LengthSquared < Epsilon)
            return swing;

        floatQ roll = FromToRotation(currentUp, upInPlane.Normalized, forward);
        return (roll * swing).Normalized;
    }

    public static floatQ FromToRotation(float3 from, float3 to)
        => FromToRotation(from, to, float3.Zero);

    // The fallback axis only matters when the vectors oppose each other and any perpendicular would do.
    public static floatQ FromToRotation(float3 from, float3 to, float3 fallbackAxis)
    {
        if (from.LengthSquared < Epsilon || to.LengthSquared < Epsilon)
            return floatQ.Identity;
        float3 f = from.Normalized;
        float3 t = to.Normalized;
        float d = float3.Dot(f, t);

        if (d >= 1f - Epsilon)
            return floatQ.Identity;

        if (d <= -1f + Epsilon)
        {
            float3 axis = fallbackAxis - f * float3.Dot(fallbackAxis, f);
            if (axis.LengthSquared < Epsilon)
            {
                axis = float3.Cross(float3.Up, f);
                if (axis.LengthSquared < Epsilon)
                    axis = float3.Cross(float3.Right, f);
            }
            return floatQ.AxisAngle(axis.Normalized, MathF.PI);
        }

        float3 c = float3.Cross(f, t);
        if (c.LengthSquared < Epsilon)
            return floatQ.Identity;
        return floatQ.AxisAngle(c.Normalized, MathF.Acos(System.Math.Clamp(d, -1f, 1f)));
    }

    // Reads one side out of a source. Every non-metacarpal rotation has to be there or the read fails;
    // metacarpals a source does not track come back as identity at the origin. Positions are taken only
    // when the whole hand has them, so a rotation-only source blends rotation-only downstream.
    public static bool Read(IHandPoseSource source, Chirality side, floatQ[] rotations, float3[] positions, out bool hasPositions)
    {
        hasPositions = true;
        bool metacarpals = source.TracksMetacarpals;
        for (int i = 0; i < NodesPerHand; i++)
        {
            var node = NodeAt(side, i);
            bool metacarpal = node.GetFingerSegmentType() == FingerSegmentType.Metacarpal;

            if (!source.TryGetFingerRotation(node, out var rotation))
            {
                if (!metacarpal || metacarpals)
                    return false;
                rotation = floatQ.Identity;
            }
            rotations[i] = rotation;

            if (hasPositions && !source.TryGetFingerPosition(node, out positions[i]))
            {
                if (metacarpal && !metacarpals)
                    positions[i] = float3.Zero;
                else
                    hasPositions = false;
            }
        }
        return true;
    }

    // Flat (every node relative to the hand) to hierarchical (every node relative to its parent segment).
    public static void ToHierarchy(float3[]? positions, floatQ[] rotations)
    {
        foreach (var finger in Fingers)
        {
            for (var segment = FingerSegmentType.Metacarpal; segment < FingerSegmentType.Tip; segment++)
            {
                if (!finger.IsValidFinger(segment))
                    continue;
                int parent = IndexOf(finger, segment);
                floatQ inverse = rotations[parent].Inverse;
                for (var child = segment + 1; child <= FingerSegmentType.Tip; child++)
                {
                    if (!finger.IsValidFinger(child))
                        continue;
                    int index = IndexOf(finger, child);
                    if (positions != null)
                        positions[index] = inverse * (positions[index] - positions[parent]);
                    rotations[index] = inverse * rotations[index];
                }
            }
        }
    }

    public static void ToFlat(float3[]? positions, floatQ[] rotations)
    {
        foreach (var finger in Fingers)
        {
            for (var segment = FingerSegmentType.Distal; segment >= FingerSegmentType.Metacarpal; segment--)
            {
                if (!finger.IsValidFinger(segment))
                    continue;
                int parent = IndexOf(finger, segment);
                floatQ q = rotations[parent];
                for (var child = segment + 1; child <= FingerSegmentType.Tip; child++)
                {
                    if (!finger.IsValidFinger(child))
                        continue;
                    int index = IndexOf(finger, child);
                    if (positions != null)
                        positions[index] = q * positions[index] + positions[parent];
                    rotations[index] = q * rotations[index];
                }
            }
        }
    }

    // Positions lerp when both sides have them (pass null otherwise), rotations always slerp.
    public static void Interpolate(float3[]? fromPositions, floatQ[] fromRotations,
        float3[]? toPositions, floatQ[] toRotations, float lerp,
        float3[]? positions, floatQ[] rotations)
    {
        for (int i = 0; i < NodesPerHand; i++)
        {
            if (positions != null && fromPositions != null && toPositions != null)
                positions[i] = fromPositions[i] + (toPositions[i] - fromPositions[i]) * lerp;
            rotations[i] = floatQ.Slerp(fromRotations[i], toRotations[i], lerp);
        }
    }

    // Hierarchical poses only. Curl is a turn about the parent's X, which takes a segment from along
    // the hand toward the palm; splay is a turn of the proximal about the parent's Y. Radians.
    public static void ModifyPose(floatQ[] rotations,
        float thumbCurl, float indexCurl, float middleCurl, float ringCurl, float pinkyCurl,
        float thumbSplay, float indexSplay, float middleSplay, float ringSplay, float pinkySplay)
    {
        Span<floatQ> curl = stackalloc floatQ[5];
        curl[0] = floatQ.AxisAngle(float3.Right, thumbCurl);
        curl[1] = floatQ.AxisAngle(float3.Right, indexCurl);
        curl[2] = floatQ.AxisAngle(float3.Right, middleCurl);
        curl[3] = floatQ.AxisAngle(float3.Right, ringCurl);
        curl[4] = floatQ.AxisAngle(float3.Right, pinkyCurl);
        Span<float> splay = stackalloc float[5];
        splay[0] = thumbSplay;
        splay[1] = indexSplay;
        splay[2] = middleSplay;
        splay[3] = ringSplay;
        splay[4] = pinkySplay;

        for (int i = 0; i < NodesPerHand; i++)
        {
            var node = NodeAt(Chirality.Left, i);
            var segment = node.GetFingerSegmentType();
            if (segment == FingerSegmentType.Metacarpal)
                continue;
            int finger = (int)node.GetFingerType();
            rotations[i] = curl[finger] * rotations[i];
            if (segment == FingerSegmentType.Proximal && splay[finger] != 0f)
                rotations[i] = floatQ.AxisAngle(float3.Up, splay[finger]) * rotations[i];
        }
    }

    // Hierarchical poses only. One magnitude fans the whole hand: index and pinky most, middle least,
    // thumb out of the palm plane. The per-side sign puts the fan outward in a right-handed hand
    // space, where the right hand's +X is the thumb side and the left hand's is the pinky side. -xlinka
    public static void ModifySplay(Chirality side, floatQ[] rotations, float splayOffset)
    {
        float outward = side == Chirality.Left ? 1f : -1f;
        foreach (var finger in Fingers)
        {
            int index = IndexOf(finger, FingerSegmentType.Proximal);
            float factor = finger switch
            {
                FingerType.Thumb => -1f,
                FingerType.Index => -0.7f * outward,
                FingerType.Middle => 0.25f * outward,
                FingerType.Ring => 0.7f * outward,
                FingerType.Pinky => outward,
                _ => 0f,
            };
            float3 axis = finger == FingerType.Thumb ? float3.Right : float3.Up;
            rotations[index] = floatQ.AxisAngle(axis, factor * splayOffset) * rotations[index];
        }
    }

    // Flat rotations from flat positions: each segment looks down the chain at the next joint (the tip
    // looks away from the joint before it), keeping the up carried along from the segment before it so
    // twist stays continuous down a finger. The first segment starts from the back of the hand, or
    // from the thumbnail side for the thumb.
    public static void RotationsFromPositions(Chirality side, float3[] positions, floatQ[] rotations)
    {
        float3 thumbUp = side == Chirality.Right ? float3.Right : float3.Left;
        foreach (var finger in Fingers)
        {
            int previous = -1;
            for (var segment = FingerSegmentType.Metacarpal; segment <= FingerSegmentType.Tip; segment++)
            {
                if (!finger.IsValidFinger(segment))
                    continue;
                int index = IndexOf(finger, segment);
                int next = NextIndex(finger, segment);

                float3 forward = next >= 0
                    ? positions[next] - positions[index]
                    : positions[index] - positions[previous];
                float3 up = finger == FingerType.Thumb ? thumbUp : float3.Up;
                if (previous >= 0)
                {
                    up = rotations[previous] * float3.Up;
                    up = FromToRotation(rotations[previous] * float3.Forward, forward) * up;
                }
                rotations[index] = FrameRotation(forward, up);
                previous = index;
            }
        }
    }

    private static int NextIndex(FingerType finger, FingerSegmentType segment)
    {
        for (var next = segment + 1; next <= FingerSegmentType.Tip; next++)
            if (finger.IsValidFinger(next))
                return IndexOf(finger, next);
        return -1;
    }
}
