// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// Fans a whole hand open or closed with one number per side, in degrees: positive spreads the
// fingers apart and lifts the thumb, negative pulls them together. -xlinka
[ComponentCategory("Users/Avatar/Hands")]
public sealed class HandSplayModifier : HandPoseProcessor
{
    public readonly SyncRef<IHandPoseSourceComponent> Source = null!;

    public readonly Sync<float> LeftSplay = new();

    public readonly Sync<float> RightSplay = new();

    protected override void UpdatePoses()
    {
        var source = Source?.Target;
        foreach (var side in HandPoseHelper.Sides)
            Data(side).Tracking = false;
        SetTracksMetacarpals(false);
        if (source == null)
            return;

        SetTracksMetacarpals(source.TracksMetacarpals);
        foreach (var side in HandPoseHelper.Sides)
        {
            if (!source.IsHandTracked(side))
                continue;
            var data = Data(side);
            if (!HandPoseHelper.Read(source, side, data.Rotations, data.Positions, out data.HasPositions))
                continue;

            float splay = (side == Chirality.Left ? LeftSplay.Value : RightSplay.Value) * (MathF.PI / 180f);
            var positions = data.HasPositions ? data.Positions : null;
            HandPoseHelper.ToHierarchy(positions, data.Rotations);
            HandPoseHelper.ModifySplay(side, data.Rotations, splay);
            HandPoseHelper.ToFlat(positions, data.Rotations);
            data.Tracking = true;
        }
    }
}
