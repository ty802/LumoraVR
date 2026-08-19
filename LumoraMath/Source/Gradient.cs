// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Math;

public struct ColorGradientKey
{
    public float Time;
    public colorHDR Color;

    public ColorGradientKey(float time, colorHDR color)
    {
        Time = time;
        Color = color;
    }
}

public struct FloatCurveKey
{
    public float Time;
    public float Value;

    public FloatCurveKey(float time, float value)
    {
        Time = time;
        Value = value;
    }
}

// Piecewise-linear colour ramp sampled by position. Keys are kept sorted on write so sampling is a
// straight scan with no per-sample sorting; with the handful of stops a gradient normally carries, a
// linear scan beats a binary search. Sampling outside the key range clamps to the end stops rather
// than extrapolating, so a gradient can never produce colours nobody authored. -xlinka
public sealed class ColorGradient
{
    private ColorGradientKey[] _keys = Array.Empty<ColorGradientKey>();

    public int KeyCount => _keys.Length;

    public ColorGradient() { }

    public ColorGradient(params ColorGradientKey[] keys) => SetKeys(keys);

    public ColorGradientKey GetKey(int index) => _keys[index];

    // input is copied and sorted; the caller's array is not retained
    public void SetKeys(ReadOnlySpan<ColorGradientKey> keys)
    {
        var copy = new ColorGradientKey[keys.Length];
        keys.CopyTo(copy);
        Array.Sort(copy, static (a, b) => a.Time.CompareTo(b.Time));
        _keys = copy;
    }

    public void Clear() => _keys = Array.Empty<ColorGradientKey>();

    // an empty gradient reads as white, so an unconfigured one tints nothing
    public colorHDR Sample(float time)
    {
        var keys = _keys;
        if (keys.Length == 0)
            return colorHDR.White;
        if (keys.Length == 1 || time <= keys[0].Time)
            return keys[0].Color;
        if (time >= keys[keys.Length - 1].Time)
            return keys[keys.Length - 1].Color;

        for (int i = 1; i < keys.Length; i++)
        {
            ref var hi = ref keys[i];
            if (time > hi.Time)
                continue;
            ref var lo = ref keys[i - 1];
            float span = hi.Time - lo.Time;
            float t = span > 1e-6f ? (time - lo.Time) / span : 0f;
            return colorHDR.Lerp(lo.Color, hi.Color, t);
        }
        return keys[keys.Length - 1].Color;
    }
}

// Piecewise-linear scalar curve, same shape and clamping rules as ColorGradient. Used for alpha and
// size envelopes over a particle's lifetime.
public sealed class FloatCurve
{
    private FloatCurveKey[] _keys = Array.Empty<FloatCurveKey>();

    public int KeyCount => _keys.Length;

    public FloatCurve() { }

    public FloatCurve(params FloatCurveKey[] keys) => SetKeys(keys);

    public FloatCurveKey GetKey(int index) => _keys[index];

    public void SetKeys(ReadOnlySpan<FloatCurveKey> keys)
    {
        var copy = new FloatCurveKey[keys.Length];
        keys.CopyTo(copy);
        Array.Sort(copy, static (a, b) => a.Time.CompareTo(b.Time));
        _keys = copy;
    }

    public void Clear() => _keys = Array.Empty<FloatCurveKey>();

    // an empty curve reads as 1, so an unconfigured one scales nothing
    public float Sample(float time)
    {
        var keys = _keys;
        if (keys.Length == 0)
            return 1f;
        if (keys.Length == 1 || time <= keys[0].Time)
            return keys[0].Value;
        if (time >= keys[keys.Length - 1].Time)
            return keys[keys.Length - 1].Value;

        for (int i = 1; i < keys.Length; i++)
        {
            ref var hi = ref keys[i];
            if (time > hi.Time)
                continue;
            ref var lo = ref keys[i - 1];
            float span = hi.Time - lo.Time;
            float t = span > 1e-6f ? (time - lo.Time) / span : 0f;
            return lo.Value + (hi.Value - lo.Value) * t;
        }
        return keys[keys.Length - 1].Value;
    }
}
