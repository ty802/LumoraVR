// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Finger data for one or both hands, 24 nodes per hand (Thumb_Metacarpal .. Pinky_Tip in BodyNode order).
//
// HAND SPACE. Everything is relative to the hand root and expressed in the hand's own frame, not the
// controller's: origin at the wrist, +Z along the extended fingers, +Y out of the back of the hand,
// X by the right-hand rule (so +X is the thumb side on the right hand and the pinky side on the left).
// A segment's rotation is the orientation of that segment's frame in this space: +Z along the segment
// toward the next joint, +Y toward the back of the hand for the fingers and toward the thumbnail for
// the thumb (that is +X on the right hand, -X on the left). Identity is a segment lying flat along the
// hand. The poser measures the same frame off the avatar's rig and maps the data onto whatever bone
// axes the rig happens to use, so a source never needs to know the rig.
//
// Rotations are what the poser drives bones from; positions are optional and only used by the
// processors for blending. A source with no positions returns false from TryGetFingerPosition. A
// source that does not track the metacarpals says so and may return false for the four non-thumb
// metacarpal nodes; the thumb metacarpal is a real joint and should always be there. -xlinka
public interface IHandPoseSource
{
    bool IsHandTracked(Chirality side);

    bool TracksMetacarpals { get; }

    bool TryGetFingerPosition(BodyNode node, out float3 handLocalPosition);

    bool TryGetFingerRotation(BodyNode node, out floatQ handLocalRotation);
}

// A source that is also a world element, so it can sit behind a SyncRef.
public interface IHandPoseSourceComponent : IHandPoseSource, IWorldElement
{
}
