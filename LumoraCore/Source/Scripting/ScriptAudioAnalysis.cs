// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Scripting;

// The sums the audio nodes share: decibels of an amplitude, the nearest MIDI note to a frequency, and how many
// samples a list may bring to a transform.
internal static class ScriptAudioAnalysis
{
    public const int MaxSamples = 65536;

    // 0 is minus infinity, as Log has it; the sign of the amplitude is ignored.
    public static float Decibels(float amplitude) => 20f * MathF.Log10(MathF.Abs(amplitude));

    // A4 at tuning hertz is 69. Cents run from -50 to +50 around the note; a frequency that is not a
    // positive number is note -1 and 0 cents.
    public static int NearestNote(float hz, float tuning, out float cents)
    {
        cents = 0f;
        if (!(hz > 0f) || !float.IsFinite(hz) || !(tuning > 0f) || !float.IsFinite(tuning))
            return -1;
        double exact = 69.0 + 12.0 * System.Math.Log2((double)hz / tuning);
        double note = System.Math.Floor(exact + 0.5);
        cents = (float)((exact - note) * 100.0);
        return note > int.MaxValue || note < int.MinValue ? -1 : (int)note;
    }
}
