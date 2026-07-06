// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// A fixed finger pose: wrist-local positions stored per finger node, served
/// through <see cref="IHandPoseSource"/>. The building block presets fill and
/// blends read from - it holds a pose, it doesn't compute one.
/// </summary>
// In-memory, local-only: a static pose is generated on each peer (e.g. by a
// preset) rather than replicated, matching the rest of the finger pipeline where
// every peer derives its own finger shape. A side reports tracking once any node
// has been set for it, so an unfilled side stays untracked and the consumer keeps
// the hand at rest instead of collapsing it toward the wrist. -xlinka
[ComponentCategory("Users/Avatar/Hands")]
public sealed class StaticHandPose : UserRootComponent, IHandPoseSourceComponent
{
    private readonly Dictionary<BodyNode, float3> _positions = new();
    private bool _leftFilled;
    private bool _rightFilled;
    private bool _tracksMetacarpals;

    public bool TracksMetacarpals => _tracksMetacarpals;

    public bool IsHandTracked(Chirality side)
        => side == Chirality.Left ? _leftFilled
         : side == Chirality.Right ? _rightFilled
         : false;

    public bool TryGetFingerPosition(BodyNode node, out float3 wristLocalPosition)
        => _positions.TryGetValue(node, out wristLocalPosition);

    /// <summary>Set one node's wrist-local position. Marks that node's side filled.</summary>
    public void SetPosition(BodyNode node, float3 wristLocalPosition)
    {
        _positions[node] = wristLocalPosition;
        var side = node.GetChirality();
        if (side == Chirality.Left) _leftFilled = true;
        else if (side == Chirality.Right) _rightFilled = true;

        if (node.IsFinger() && node.GetFingerSegmentType() == FingerSegmentType.Metacarpal)
            _tracksMetacarpals = true;
    }

    /// <summary>Clear all stored positions and tracking state.</summary>
    public void Clear()
    {
        _positions.Clear();
        _leftFilled = false;
        _rightFilled = false;
        _tracksMetacarpals = false;
    }
}
