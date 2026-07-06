// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>The hand shapes the preset can select.</summary>
public enum HandPoseShape
{
    /// <summary>Relaxed, slightly-curled neutral hand.</summary>
    Idle = 0,
    /// <summary>Closed fist.</summary>
    Fist = 1,
    /// <summary>Index extended, the rest curled (a point).</summary>
    Point = 2,
}

/// <summary>
/// A finger-pose source that serves one of a few authored hand shapes, generated
/// from the parametric <see cref="HandPoseModel"/> (no baked numbers). Selecting
/// a <see cref="Shape"/> regenerates both hands' wrist-local node positions.
/// </summary>
// Poses are synthesized per-peer from the curl model, not replicated - only the
// chosen Shape enum needs to sync. The generated positions are placeholders tuned
// to read as idle/fist/point; they are not measured hand data. -xlinka
[ComponentCategory("Users/Avatar/Hands")]
public sealed class HandPosePreset : UserRootComponent, IHandPoseSourceComponent
{
    /// <summary>Which hand shape to serve.</summary>
    public readonly Sync<HandPoseShape> Shape = new();

    // Per-finger curl 0..1 for the current shape (Thumb, Index, Middle, Ring, Pinky).
    private static readonly float[] IdleCurl = { 0.18f, 0.20f, 0.22f, 0.24f, 0.28f };
    private static readonly float[] FistCurl = { 0.75f, 1.00f, 1.00f, 1.00f, 1.00f };
    private static readonly float[] PointCurl = { 0.85f, 0.00f, 1.00f, 1.00f, 1.00f };

    private HandPoseShape _builtShape = (HandPoseShape)(-1);
    private readonly System.Collections.Generic.Dictionary<BodyNode, float3> _positions = new();

    public bool TracksMetacarpals => true; // the model emits metacarpal nodes

    public bool IsHandTracked(Chirality side)
    {
        Build();
        return side == Chirality.Left || side == Chirality.Right;
    }

    public bool TryGetFingerPosition(BodyNode node, out float3 wristLocalPosition)
    {
        Build();
        return _positions.TryGetValue(node, out wristLocalPosition);
    }

    // Regenerate only when the selected shape changes.
    private void Build()
    {
        if (_builtShape == Shape.Value)
            return;
        _builtShape = Shape.Value;
        _positions.Clear();

        var curl = Shape.Value switch
        {
            HandPoseShape.Fist => FistCurl,
            HandPoseShape.Point => PointCurl,
            _ => IdleCurl,
        };

        foreach (var side in new[] { Chirality.Left, Chirality.Right })
        {
            for (int f = 0; f < HandPoseNodes.Fingers.Length; f++)
            {
                var finger = HandPoseNodes.Fingers[f];
                HandPoseModel.GenerateFinger(finger, side, curl[f], 0f,
                    (node, pos) => _positions[node] = pos);
            }
        }
    }
}
