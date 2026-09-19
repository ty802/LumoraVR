// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Lerp between two sources: 0 is all A, 1 is all B. The blend runs in hierarchy space so each joint
// slerps its own bend and a half-way fist is half-curled at every knuckle instead of a straight line
// between two tips. A side tracks only where both inputs track it. -xlinka
[ComponentCategory("Users/Avatar/Hands")]
public sealed class HandPoseBlend : HandPoseProcessor
{
    public readonly SyncRef<IHandPoseSourceComponent> A = null!;

    public readonly SyncRef<IHandPoseSourceComponent> B = null!;

    public readonly Sync<float> Weight = new();

    private readonly float3[] _aPositions = HandPoseHelper.NewPositions();
    private readonly floatQ[] _aRotations = HandPoseHelper.NewRotations();
    private readonly float3[] _bPositions = HandPoseHelper.NewPositions();
    private readonly floatQ[] _bRotations = HandPoseHelper.NewRotations();

    protected override void UpdatePoses()
    {
        var a = A?.Target;
        var b = B?.Target;
        foreach (var side in HandPoseHelper.Sides)
            Data(side).Tracking = false;
        SetTracksMetacarpals(false);
        if (a == null || b == null)
            return;

        SetTracksMetacarpals(a.TracksMetacarpals && b.TracksMetacarpals);
        foreach (var side in HandPoseHelper.Sides)
        {
            if (!a.IsHandTracked(side) || !b.IsHandTracked(side))
                continue;
            if (!HandPoseHelper.Read(a, side, _aRotations, _aPositions, out bool aPositions)
                || !HandPoseHelper.Read(b, side, _bRotations, _bPositions, out bool bPositions))
                continue;

            var data = Data(side);
            data.HasPositions = aPositions && bPositions;
            HandPoseHelper.ToHierarchy(aPositions ? _aPositions : null, _aRotations);
            HandPoseHelper.ToHierarchy(bPositions ? _bPositions : null, _bRotations);
            HandPoseHelper.Interpolate(
                data.HasPositions ? _aPositions : null, _aRotations,
                data.HasPositions ? _bPositions : null, _bRotations,
                Weight.Value,
                data.HasPositions ? data.Positions : null, data.Rotations);
            HandPoseHelper.ToFlat(data.HasPositions ? data.Positions : null, data.Rotations);
            data.Tracking = true;
        }
    }
}
