// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Shared rebuild for the curve and gradient modules.
//
// Rebuilds only when a key actually changed, and detects that with a cheap hash of the two lists
// rather than a change event: a ramp is edited by hand a few times and then read every frame forever,
// so it is the READ path that has to be free. -xlinka
internal static class ParticleCurveHelper
{
    public static void Rebuild(SyncFieldList<float> times, SyncFieldList<float> values, FloatCurve curve, ref int lastRevision)
    {
        int count = times.Count < values.Count ? times.Count : values.Count;
        int revision = count;
        for (int i = 0; i < count; i++)
            revision = revision * 31 + times[i].GetHashCode() * 17 + values[i].GetHashCode();
        if (revision == lastRevision)
            return;
        lastRevision = revision;

        var keys = new FloatCurveKey[count];
        for (int i = 0; i < count; i++)
            keys[i] = new FloatCurveKey(times[i], values[i]);
        curve.SetKeys(keys);
    }

    public static void RebuildGradient(SyncFieldList<float> times, SyncFieldList<colorHDR> colors, ColorGradient gradient, ref int lastRevision)
    {
        int count = times.Count < colors.Count ? times.Count : colors.Count;
        int revision = count;
        for (int i = 0; i < count; i++)
            revision = revision * 31 + times[i].GetHashCode() * 17 + colors[i].GetHashCode();
        if (revision == lastRevision)
            return;
        lastRevision = revision;

        var keys = new ColorGradientKey[count];
        for (int i = 0; i < count; i++)
            keys[i] = new ColorGradientKey(times[i], colors[i]);
        gradient.SetKeys(keys);
    }
}
