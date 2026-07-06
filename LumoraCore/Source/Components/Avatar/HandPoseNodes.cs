// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Input;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Shared layout helpers for the wrist-local finger-node set the composition layer
/// stores and exchanges. The set is the 24 finger nodes per hand
/// (Thumb_Metacarpal .. Pinky_Tip), matching <see cref="HandPoseStreamManager"/>.
/// </summary>
internal static class HandPoseNodes
{
    /// <summary>Finger nodes per hand (Metacarpal .. Tip across all five fingers).</summary>
    public const int NodesPerHand = 24;

    /// <summary>The five fingers in storage order.</summary>
    public static readonly FingerType[] Fingers =
    {
        FingerType.Thumb, FingerType.Index, FingerType.Middle, FingerType.Ring, FingerType.Pinky,
    };

    // Thumb has no intermediate; every other finger has all five segments.
    public static readonly FingerSegmentType[] ThumbSegments =
    {
        FingerSegmentType.Metacarpal, FingerSegmentType.Proximal,
        FingerSegmentType.Distal, FingerSegmentType.Tip,
    };

    public static readonly FingerSegmentType[] FullSegments =
    {
        FingerSegmentType.Metacarpal, FingerSegmentType.Proximal, FingerSegmentType.Intermediate,
        FingerSegmentType.Distal, FingerSegmentType.Tip,
    };

    /// <summary>Segments that exist for the given finger (thumb skips the intermediate).</summary>
    public static FingerSegmentType[] SegmentsFor(FingerType finger)
        => finger == FingerType.Thumb ? ThumbSegments : FullSegments;

    /// <summary>
    /// Index of a finger node within a single hand's 0..23 range, or -1 if the node
    /// is not a finger node for the given side.
    /// </summary>
    public static int IndexInHand(BodyNode node)
    {
        int n = (int)node;
        if (n >= (int)BodyNode.LeftThumb_Metacarpal && n <= (int)BodyNode.LeftPinky_Tip)
            return n - (int)BodyNode.LeftThumb_Metacarpal;
        if (n >= (int)BodyNode.RightThumb_Metacarpal && n <= (int)BodyNode.RightPinky_Tip)
            return n - (int)BodyNode.RightThumb_Metacarpal;
        return -1;
    }

    /// <summary>Enumerate every finger node of one side, in storage order.</summary>
    public static IEnumerable<BodyNode> NodesOf(Chirality side)
    {
        foreach (var finger in Fingers)
            foreach (var seg in SegmentsFor(finger))
                yield return finger.ComposeFinger(seg, side);
    }
}
