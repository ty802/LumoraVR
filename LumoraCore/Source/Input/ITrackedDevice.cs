// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Input;

/// <summary>
/// Interface for tracked VR devices (headset, controllers, trackers).
/// </summary>
public interface ITrackedDevice : IInputDevice
{
    /// <summary>
    /// The body node this device corresponds to.
    /// </summary>
    BodyNode CorrespondingBodyNode { get; }

    /// <summary>
    /// Whether this device is currently being tracked.
    /// </summary>
    bool IsTracking { get; }

    /// <summary>
    /// Priority of this device (higher priority wins when multiple devices track the same body node).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// The tracking space this device operates in.
    /// </summary>
    TrackingSpace TrackingSpace { get; }

    /// <summary>
    /// Raw position before tracking space transformation.
    /// </summary>
    float3 RawPosition { get; }

    /// <summary>
    /// Raw rotation before tracking space transformation.
    /// </summary>
    floatQ RawRotation { get; }

    /// <summary>
    /// Transformed position in world space.
    /// </summary>
    float3 Position { get; }

    /// <summary>
    /// Transformed rotation in world space.
    /// </summary>
    floatQ Rotation { get; }

    /// <summary>
    /// Offset from device position to body node position.
    /// </summary>
    float3 BodyNodePositionOffset { get; }

    /// <summary>
    /// Offset from device rotation to body node rotation.
    /// </summary>
    floatQ BodyNodeRotationOffset { get; }
}

// The space raw tracked poses are expressed THROUGH, on its way to world space: the local user's root.
//
// Bound to the root SLOT, not snapshotted off it. The root moves inside a frame - a turn rotates it about
// the head partway through the update, walking moves it, a teleport jumps it - and anything that asks a
// device for its world pose after that has to get it out of the space the rig is actually standing in. A
// snapshot taken at the frame boundary answers with the pre-turn heading for the rest of the frame, which
// is how a hand ends up drawn behind the body it belongs to. Slot globals are cached, so reading live
// costs a dirty-flag check. -xlinka
public class TrackingSpace
{
    private Slot? _space;
    private float3 _position = float3.Zero;
    private floatQ _rotation = floatQ.Identity;
    private float _scale = 1f;

    public Slot? Space
    {
        get => _space != null && !_space.IsDestroyed ? _space : null;
        set => _space = value;
    }

    // Each live read leaves its answer behind, so losing the root (world switch, teardown) parks devices
    // where they last were instead of snapping them to the world origin.
    public float3 Position
    {
        get
        {
            var space = Space;
            if (space != null) _position = space.GlobalPosition;
            return _position;
        }
        set { _space = null; _position = value; }
    }

    public floatQ Rotation
    {
        get
        {
            var space = Space;
            if (space != null) _rotation = space.GlobalRotation;
            return _rotation;
        }
        set { _space = null; _rotation = value; }
    }

    public float Scale
    {
        get
        {
            var space = Space;
            if (space != null) _scale = space.GlobalScale.x;
            return _scale;
        }
        set { _space = null; _scale = value; }
    }

    public float3 Transform(float3 position)
    {
        return Position + Rotation * (position * Scale);
    }

    public floatQ Transform(floatQ rotation)
    {
        return Rotation * rotation;
    }
}
