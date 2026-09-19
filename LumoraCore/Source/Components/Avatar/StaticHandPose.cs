// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// A fixed pose held in memory: hand-space position and rotation per finger node. Not a component;
// the presets are built as these once per process and HandPosePreset serves them from a slot. A side
// tracks once every non-metacarpal node of that side is set, and the pose carries metacarpals only
// when both tracked sides have all of theirs. -xlinka
public sealed class StaticHandPose : IHandPoseSource
{
    private readonly Dictionary<BodyNode, (float3 position, floatQ rotation)> _poses = new();
    private bool _metadataValid;
    private bool _hasMetacarpals;
    private bool _hasLeft;
    private bool _hasRight;

    public bool TracksMetacarpals
    {
        get
        {
            EnsureMetadata();
            return _hasMetacarpals;
        }
    }

    public bool IsHandTracked(Chirality side)
    {
        EnsureMetadata();
        return side == Chirality.Left ? _hasLeft : side == Chirality.Right && _hasRight;
    }

    public bool TryGetFingerPosition(BodyNode node, out float3 handLocalPosition)
    {
        if (_poses.TryGetValue(node, out var pose))
        {
            handLocalPosition = pose.position;
            return true;
        }
        handLocalPosition = float3.Zero;
        return false;
    }

    public bool TryGetFingerRotation(BodyNode node, out floatQ handLocalRotation)
    {
        if (_poses.TryGetValue(node, out var pose))
        {
            handLocalRotation = pose.rotation;
            return true;
        }
        handLocalRotation = floatQ.Identity;
        return false;
    }

    public void SetPose(BodyNode node, float3 position, floatQ rotation)
    {
        _poses[node] = (position, rotation);
        _metadataValid = false;
    }

    public void SetPose(Chirality side, float3[] positions, floatQ[] rotations)
    {
        for (int i = 0; i < HandPoseHelper.NodesPerHand; i++)
            _poses[HandPoseHelper.NodeAt(side, i)] = (positions[i], rotations[i]);
        _metadataValid = false;
    }

    public void Clear()
    {
        _poses.Clear();
        _metadataValid = false;
    }

    private void EnsureMetadata()
    {
        if (_metadataValid)
            return;
        _hasMetacarpals = true;
        _hasLeft = HasSide(Chirality.Left, ref _hasMetacarpals);
        _hasRight = HasSide(Chirality.Right, ref _hasMetacarpals);
        _metadataValid = true;
    }

    private bool HasSide(Chirality side, ref bool metacarpals)
    {
        for (int i = 0; i < HandPoseHelper.NodesPerHand; i++)
        {
            var node = HandPoseHelper.NodeAt(side, i);
            if (_poses.ContainsKey(node))
                continue;
            if (node.GetFingerSegmentType() != FingerSegmentType.Metacarpal)
                return false;
            metacarpals = false;
        }
        return true;
    }
}
