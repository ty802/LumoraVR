// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Picks one of several sources and eases the output toward it, so a hand switching between live
// tracking and a preset does not snap. Index selects a source outright; left at -1 the first source
// tracking the side wins, which is what a hand wants by default: real finger tracking when it is
// there, the shaped fallback when it is not. The ease runs in hierarchy space at InterpolationSpeed
// per second and starts from the target itself the first frame a side comes in. -xlinka
[ComponentCategory("Users/Avatar/Hands")]
public sealed class HandPoseMultiplexer : HandPoseProcessor
{
    public readonly Sync<int> Index = new(-1);

    public readonly Sync<float> InterpolationSpeed = new(10f);

    public readonly SyncRefList<IHandPoseSourceComponent> Sources = new();

    private readonly float3[] _targetPositions = HandPoseHelper.NewPositions();
    private readonly floatQ[] _targetRotations = HandPoseHelper.NewRotations();
    private IHandPoseSourceComponent? _activeLeft;
    private IHandPoseSourceComponent? _activeRight;

    public IHandPoseSourceComponent? ActiveSource(Chirality side)
        => side == Chirality.Left ? _activeLeft : side == Chirality.Right ? _activeRight : null;

    protected override void UpdatePoses()
    {
        bool metacarpals = true;
        bool any = false;
        foreach (var side in HandPoseHelper.Sides)
        {
            var source = Select(side);
            var data = Data(side);
            SetActive(side, source);
            if (source == null || !source.IsHandTracked(side)
                || !HandPoseHelper.Read(source, side, _targetRotations, _targetPositions, out bool targetPositions))
            {
                data.Tracking = false;
                continue;
            }

            bool wasTracking = data.Tracking;

            any = true;
            metacarpals &= source.TracksMetacarpals;
            HandPoseHelper.ToHierarchy(targetPositions ? _targetPositions : null, _targetRotations);

            if (!wasTracking)
            {
                Array.Copy(_targetRotations, data.Rotations, HandPoseHelper.NodesPerHand);
                if (targetPositions)
                    Array.Copy(_targetPositions, data.Positions, HandPoseHelper.NodesPerHand);
                data.HasPositions = targetPositions;
            }
            else
            {
                float delta = World?.Time.Delta ?? 0f;
                float lerp = System.Math.Clamp(delta * InterpolationSpeed.Value, 0f, 1f);
                bool positions = data.HasPositions && targetPositions;
                HandPoseHelper.ToHierarchy(data.HasPositions ? data.Positions : null, data.Rotations);
                HandPoseHelper.Interpolate(
                    positions ? data.Positions : null, data.Rotations,
                    positions ? _targetPositions : null, _targetRotations,
                    lerp, positions ? data.Positions : null, data.Rotations);
                data.HasPositions = positions;
            }

            HandPoseHelper.ToFlat(data.HasPositions ? data.Positions : null, data.Rotations);
            for (int i = 0; i < HandPoseHelper.NodesPerHand; i++)
                data.Rotations[i] = data.Rotations[i].Normalized;
            data.Tracking = true;
        }
        SetTracksMetacarpals(any && metacarpals);
    }

    private IHandPoseSourceComponent? Select(Chirality side)
    {
        if (Sources == null)
            return null;
        int index = Index.Value;
        if (index >= 0)
            return index < Sources.Count ? Sources[index] : null;

        for (int i = 0; i < Sources.Count; i++)
        {
            var source = Sources[i];
            if (source != null && !ReferenceEquals(source, this) && source.IsHandTracked(side))
                return source;
        }
        return null;
    }

    private void SetActive(Chirality side, IHandPoseSourceComponent? source)
    {
        if (side == Chirality.Left)
            _activeLeft = source;
        else
            _activeRight = source;
    }
}
