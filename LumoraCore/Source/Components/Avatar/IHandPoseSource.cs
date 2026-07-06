// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// A source of per-finger pose data for a hand, expressed in that hand's local
/// (wrist) frame.
/// </summary>
// Finger shape only ever comes from a real source (VR tracking). There is no
// procedural fallback in here: when IsHandTracked is false the consumer
// (HandPoseDriver) parks the hand in its authored rest pose, so untracked hands -
// desktop users, remote users without a finger stream - simply stay relaxed
// instead of getting fake curl. - xlinka
public interface IHandPoseSource
{
    /// <summary>Whether real finger data is available for the given side right now.</summary>
    bool IsHandTracked(Chirality side);

    /// <summary>
    /// Whether this source carries data for the metacarpal nodes. Most tracking
    /// hardware reports proximal-and-outward only; the metacarpals are part of the
    /// palm and barely move, so they're left at rest. When false the consumer must
    /// not drive the metacarpal bone from this source (it would feed it absent or
    /// stale data); the bone keeps its authored rest pose instead.
    /// </summary>
    bool TracksMetacarpals { get; }

    /// <summary>
    /// Position of a finger node relative to its wrist, in wrist-local space.
    /// Consumers use only the direction between consecutive nodes, so the
    /// absolute scale of the source hand does not need to match the avatar.
    /// </summary>
    bool TryGetFingerPosition(BodyNode node, out float3 wristLocalPosition);
}

/// <summary>
/// An <see cref="IHandPoseSource"/> that is also a world element, so it can be
/// referenced through a SyncRef (the per-user published source).
/// </summary>
public interface IHandPoseSourceComponent : IHandPoseSource, IWorldElement
{
}
