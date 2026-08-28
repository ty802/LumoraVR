// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Input;

public class TrackedObject : InputDevice, ITrackedDevice
{
    private float3 _rawPosition = float3.Zero;
    private floatQ _rawRotation = floatQ.Identity;

    public BodyNode CorrespondingBodyNode { get; set; } = BodyNode.NONE;

    public TrackingSpace TrackingSpace { get; set; } = null!;

    public bool IsTracking { get; set; }

    public int Priority { get; set; } = 0;

    // Before tracking-space transformation.
    public float3 RawPosition
    {
        get => _rawPosition;
        set => _rawPosition = value;
    }

    // Before tracking-space transformation.
    public floatQ RawRotation
    {
        get => _rawRotation;
        set => _rawRotation = value;
    }

    public float3 Position => TrackingSpace?.Transform(RawPosition) ?? RawPosition;

    public floatQ Rotation => TrackingSpace?.Transform(RawRotation) ?? RawRotation;

    public float3 BodyNodePositionOffset { get; set; } = float3.Zero;

    public floatQ BodyNodeRotationOffset { get; set; } = floatQ.Identity;
}
