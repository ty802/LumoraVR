// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Math;

namespace Lumora.Core.Input;

// The two hand devices as a finger pose source, in the hand space IHandPoseSource specifies: origin at
// the wrist, +Z along the extended fingers, +Y out of the back of the hand, segment frames +Z along the
// bone. The device keeps that view refreshed on every feed, so this is a plain read.
public sealed class HandDevicePoseSource : IHandPoseSource
{
    public HandDevice? Left { get; private set; }
    public HandDevice? Right { get; private set; }

    public HandDevicePoseSource(HandDevice? left = null, HandDevice? right = null)
    {
        if (left != null && left.Side != Chirality.Left)
            throw new ArgumentException("Left hand is not left");
        if (right != null && right.Side != Chirality.Right)
            throw new ArgumentException("Right hand is not right");
        Left = left;
        Right = right;
    }

    public void Attach(HandDevice hand)
    {
        if (hand.Side == Chirality.Left)
            Left = hand;
        else
            Right = hand;
    }

    public HandDevice? GetHand(Chirality side)
        => side == Chirality.Left ? Left : side == Chirality.Right ? Right : null;

    public bool TracksMetacarpals
    {
        get
        {
            if (Left != null && !Left.TracksMetacarpals)
                return false;
            if (Right != null && !Right.TracksMetacarpals)
                return false;
            return true;
        }
    }

    public bool IsHandTracked(Chirality side)
        => GetHand(side)?.IsTracking ?? false;

    public bool TryGetFingerPosition(BodyNode node, out float3 handLocalPosition)
    {
        if (TryGetFingerData(node, out handLocalPosition, out _))
            return true;
        handLocalPosition = float3.Zero;
        return false;
    }

    public bool TryGetFingerRotation(BodyNode node, out floatQ handLocalRotation)
    {
        if (TryGetFingerData(node, out _, out handLocalRotation))
            return true;
        handLocalRotation = floatQ.Identity;
        return false;
    }

    public bool TryGetFingerData(BodyNode node, out float3 position, out floatQ rotation)
    {
        var hand = GetHand(node.GetChirality());
        if (hand == null || !hand.IsTracking || !hand.TryGetSegment(node, out var segment))
        {
            position = float3.Zero;
            rotation = floatQ.Identity;
            return false;
        }
        position = segment.HandSpacePosition;
        rotation = segment.HandSpaceRotation;
        return true;
    }

    // The 24 finger segments of one side in body node order.
    public bool TryGetFingerData(Chirality side, Span<float3> positions, Span<floatQ> rotations)
    {
        var hand = GetHand(side);
        if (hand == null || !hand.IsTracking)
            return false;
        if (positions.Length < HandPoseSynth.SegmentCount || rotations.Length < HandPoseSynth.SegmentCount)
            throw new ArgumentException("Pose spans must hold 24 segments");
        int i = 0;
        foreach (var finger in hand)
        {
            foreach (var segment in finger)
            {
                positions[i] = segment.HandSpacePosition;
                rotations[i] = segment.HandSpaceRotation;
                i++;
            }
        }
        return true;
    }
}
