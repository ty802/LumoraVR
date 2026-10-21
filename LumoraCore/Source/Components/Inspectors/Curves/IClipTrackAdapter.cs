// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Assets.Animation;

namespace Lumora.Core.Components;

// The typed half of reading and writing a clip track, behind a face the editor can hold without
// knowing the value type.
public interface IClipTrackAdapter
{
    CurveTrackInfo Describe(AnimationTrack track);

    List<CurveKeyRow> Read(AnimationTrack track);

    // A new track carrying the template's node, property and family, with these keys.
    AnimationTrack Build(AnimationTrack template, IReadOnlyList<CurveKeyRow> rows);

    ICurveSampler Sampler(AnimationTrack track);
}
