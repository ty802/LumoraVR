// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;

namespace Lumora.Audio;

internal static class DspMath
{
    // Adding this and taking it away again rounds anything under about 5e-26 to exactly zero and leaves every
    // audible value as it was, so a state decaying toward silence never lands in the subnormal range, where
    // each operation on it costs a hundred times more. No branch and no compare in the hot loop. -xlinka
    private const float FlushOffset = 1e-18f;

    public const float Ln10Over20 = 0.115129254f;
    public const float TwentyOverLn10 = 8.68588964f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Flush(float value) => value + FlushOffset - FlushOffset;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DbToGain(float db) => MathF.Exp(db * Ln10Over20);

    public static double DbToGain(double db) => Math.Pow(10.0, db / 20.0);

    // Pulls a setting into its range; a NaN takes the default.
    public static float Limit(float value, float min, float max, float fallback)
        => float.IsNaN(value) ? fallback : (value < min ? min : (value > max ? max : value));

    // Per-sample one-pole coefficient that closes 1 - 1/e of a gap in the given time.
    public static float OnePole(double seconds, double sampleRate)
        => seconds > 0.0 ? (float)(1.0 - Math.Exp(-1.0 / (seconds * sampleRate))) : 1f;

    // The same for a glide stepped once every `every` samples.
    public static double OnePole(double seconds, double sampleRate, int every)
        => seconds > 0.0 ? 1.0 - Math.Exp(-every / (seconds * sampleRate)) : 1.0;

    // A one-pole move that lands exactly: once a move changes nothing, or leaves less than the snap distance,
    // the target itself is taken, so a glide to 1 ends on bit-exact unity rather than a hair under it.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Approach(float value, float target, float coefficient, float snap)
    {
        float next = value + (target - value) * coefficient;
        return next == value || MathF.Abs(target - next) < snap ? target : next;
    }

    // Control-rate glide in whatever domain the caller keeps the value in (octaves, decibels). False once it
    // has landed.
    public static bool Glide(ref double value, double target, double coefficient, double tolerance)
    {
        double next = value + (target - value) * coefficient;
        if (next == value || Math.Abs(target - next) <= tolerance)
        {
            value = target;
            return false;
        }
        value = next;
        return true;
    }

    // tanh to within 1e-4 everywhere, as a [7/6] Pade fraction that reaches exactly 1 at 4.97 and is held there.
    // A real tanh per oversampled sample costs more than the rest of a distortion stage put together.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Tanh(float x)
    {
        if (x > 4.97f)
            return 1f;
        if (x < -4.97f)
            return -1f;
        float x2 = x * x;
        float numerator = x * (135135f + x2 * (17325f + x2 * (378f + x2)));
        float denominator = 135135f + x2 * (62370f + x2 * (3150f + x2 * 28f));
        return numerator / denominator;
    }

    public static float Peak(ReadOnlySpan<StereoSample> buffer)
    {
        float peak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float amplitude = buffer[i].AbsoluteAmplitude;
            if (amplitude > peak)
                peak = amplitude;
        }
        return peak;
    }

    // Mix laws. Wet-and-dry is the reverb's: 0 dry alone, 0.5 both at full level, 1 the effect alone, so an
    // echo or a room adds to the signal instead of taking from it. Crossfade is equal parts at 0.5, which is
    // where a modulated copy cancels the dry signal deepest.
    public static void WetAndDry(float mix, out float dry, out float wet)
    {
        dry = MathF.Min(1f, 2f * (1f - mix));
        wet = MathF.Min(1f, 2f * mix);
    }

    public static void Crossfade(float mix, out float dry, out float wet)
    {
        dry = 1f - mix;
        wet = mix;
    }

    public static int NextPowerOfTwo(int value)
    {
        int size = 1;
        while (size < value)
            size <<= 1;
        return size;
    }
}
