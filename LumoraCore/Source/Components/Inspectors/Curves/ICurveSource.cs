// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Localization;

namespace Lumora.Core.Components;

// Anything the curve editor can open: an animation clip (one track per animated property), or a curve
// field on a component (a bone chain's falloff, a particle size over lifetime, gradient stops). Every
// machine looking at the panel builds its own source from the same target and reads the same data;
// only the machine whose user is dragging writes, and the write is what replicates.
public interface ICurveSource : IDisposable
{
    string Title { get; }

    // The target still exists.
    bool IsAlive { get; }

    // The data has arrived (a clip may still be loading).
    bool IsReady { get; }

    // Moves whenever the underlying keys change, from here or from anywhere else.
    long Version { get; }

    bool CanEdit { get; }

    string ReadOnlyReason { get; }

    IReadOnlyList<CurveTrackInfo> Tracks { get; }

    // How far the time axis naturally runs: a clip's length, or 1 for a curve over a 0..1 input.
    float Duration { get; }

    // Keys live on 0..1 and may not leave it.
    bool FixedTimeRange { get; }

    // One frame of a clip, or a twentieth of a 0..1 curve.
    float SnapStep { get; }

    List<CurveKeyRow> ReadKeys(int track);

    ICurveSampler CreateSampler(int track, IReadOnlyList<CurveKeyRow> rows);

    // Mid-drag. A list writes straight through so everyone watches the key move; a clip only updates the
    // copy this machine samples from, since every save is a new asset.
    void Preview(int track, IReadOnlyList<CurveKeyRow> rows);

    // The end of one edit: lands it for everyone and makes it one undo step. Report gets a short status
    // line on this machine (saving, saved, why not).
    void Commit(int track, IReadOnlyList<CurveKeyRow> before, IReadOnlyList<CurveKeyRow> after, LocaleText step, Action<string> report);
}
