// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Input;

/// <summary>
/// A compact set of mouth/lip expression weights produced by a face tracker. A hardware hook fills these
/// each frame; <see cref="MouthStreamManager"/> replicates them and <c>MouthExpressionDriver</c> maps
/// them onto avatar blendshapes. Kept small and device-agnostic - a hook normalizes its own richer shape
/// set down to these.
/// </summary>
public enum MouthShape
{
    JawOpen,
    JawLeft,
    JawRight,
    JawForward,
    MouthPucker,
    MouthWide,
    SmileLeft,
    SmileRight,
    FrownLeft,
    FrownRight,
    UpperUp,
    LowerDown,
    CheekPuff,
    TongueOut
}

/// <summary>
/// Face/lip tracking device. A hardware hook sets <see cref="IsTracking"/> and the per-shape weights;
/// everything is 0 and untracked by default, so the avatar's mouth simply rests until a tracker feeds it.
/// </summary>
public class MouthDevice : InputDevice
{
    public static readonly int ShapeCount = Enum.GetValues(typeof(MouthShape)).Length;

    private readonly float[] _weights = new float[ShapeCount];

    /// <summary>True while a face tracker is providing live mouth data.</summary>
    public bool IsTracking { get; set; }

    /// <summary>True while the headset/tracker is worn.</summary>
    public bool IsWorn { get; set; } = true;

    public float TrackingConfidence { get; set; } = 1f;

    public IReadOnlyList<float> Weights => _weights;

    public float GetWeight(MouthShape shape)
    {
        int i = (int)shape;
        return (uint)i < (uint)_weights.Length ? _weights[i] : 0f;
    }

    public void SetWeight(MouthShape shape, float weight)
    {
        int i = (int)shape;
        if ((uint)i < (uint)_weights.Length)
            _weights[i] = weight;
    }

    public void ClearWeights() => Array.Clear(_weights, 0, _weights.Length);
}
