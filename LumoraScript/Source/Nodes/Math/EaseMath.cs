// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// What the easing nodes and Tween Field share. Each curve is written once, as an ease-in; the direction decides
// where that curve is read and how its answer is placed, so every curve runs every way alike. Time at or past
// an end answers with that end exactly, NaN reads as the start, and the work is done in double so the float
// forms round once. -xlinka
internal static class EaseMath
{
    public const double Overshoot = 1.70158d;
    public const double Amplitude = 1d;
    public const double Period = 0.3d;

    // What is left of a spring's swing when the time runs out, taken off so the last swing ends on the line.
    private const double Residue = 1d / 1024d;

    public static bool AtEnd(double time, out double end)
    {
        end = time >= 1d ? 1d : 0d;
        return !(time > 0d) || time >= 1d;
    }

    // Where the in-curve is read for a time, and how its answer is placed: offset + scale * curve.
    public static (double At, double Offset, double Scale) Place(double time, EaseDirection direction) => direction switch
    {
        EaseDirection.Out => (1d - time, 1d, -1d),
        EaseDirection.InOut => time < 0.5d ? (2d * time, 0d, 0.5d) : (2d - 2d * time, 1d, -0.5d),
        EaseDirection.OutIn => time < 0.5d ? (1d - 2d * time, 0.5d, -0.5d) : (2d * time - 1d, 0.5d, 0.5d),
        _ => (time, 0d, 1d),
    };

    // Pulled back below 0 by overshoot first, then on to 1. An overshoot of 0 is the cubic.
    public static double BackIn(double t, double overshoot) => t * t * ((overshoot + 1d) * t - overshoot);

    // A spring settling on 1: it shoots past and wobbles down. amplitude sizes the swings, 1 at the least;
    // period is one swing's share of the whole, Period when it is 0 or below.
    public static double ElasticOut(double t, double amplitude, double period)
    {
        if (!(period > 0d))
            period = Period;
        amplitude = Math.Max(amplitude, 1d);
        double shift = period / (2d * Math.PI) * Math.Asin(1d / amplitude);
        double fade = (Math.Pow(2d, -10d * t) - Residue) / (1d - Residue);
        return amplitude * fade * Math.Sin((t - shift) * (2d * Math.PI) / period) + 1d;
    }

    // Four falling bounces, each one a parabola, the last landing on 1.
    public static double BounceOut(double t)
    {
        const double Scale = 7.5625d;
        const double Span = 2.75d;
        if (t < 1d / Span)
            return Scale * t * t;
        if (t < 2d / Span)
        {
            t -= 1.5d / Span;
            return Scale * t * t + 0.75d;
        }
        if (t < 2.5d / Span)
        {
            t -= 2.25d / Span;
            return Scale * t * t + 0.9375d;
        }
        t -= 2.625d / Span;
        return Scale * t * t + 0.984375d;
    }
}
