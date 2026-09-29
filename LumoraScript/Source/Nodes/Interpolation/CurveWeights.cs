// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// How much of each of its four inputs a cubic curve takes at t, and how fast that changes with t. The
// curve nodes weigh the inputs with these, so every value type shares one set of sums. -xlinka
internal static class CurveWeights
{
    // From, fromControl, toControl, to.
    public static float4 Bezier(float t)
    {
        float u = 1f - t;
        return new float4(u * u * u, 3f * u * u * t, 3f * u * t * t, t * t * t);
    }

    public static float4 BezierSlope(float t)
    {
        float u = 1f - t;
        return new float4(-3f * u * u, 3f * u * u - 6f * u * t, 6f * u * t - 3f * t * t, 3f * t * t);
    }

    // From, fromTangent, to, toTangent.
    public static float4 Hermite(float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return new float4(2f * t3 - 3f * t2 + 1f, t3 - 2f * t2 + t, 3f * t2 - 2f * t3, t3 - t2);
    }

    public static float4 HermiteSlope(float t)
    {
        float t2 = t * t;
        return new float4(6f * t2 - 6f * t, 3f * t2 - 4f * t + 1f, 6f * t - 6f * t2, 3f * t2 - 2f * t);
    }

    public static (double, double, double, double) Bezier(double t)
    {
        double u = 1d - t;
        return (u * u * u, 3d * u * u * t, 3d * u * t * t, t * t * t);
    }

    public static (double, double, double, double) BezierSlope(double t)
    {
        double u = 1d - t;
        return (-3d * u * u, 3d * u * u - 6d * u * t, 6d * u * t - 3d * t * t, 3d * t * t);
    }

    public static (double, double, double, double) Hermite(double t)
    {
        double t2 = t * t, t3 = t2 * t;
        return (2d * t3 - 3d * t2 + 1d, t3 - 2d * t2 + t, 3d * t2 - 2d * t3, t3 - t2);
    }

    public static (double, double, double, double) HermiteSlope(double t)
    {
        double t2 = t * t;
        return (6d * t2 - 6d * t, 3d * t2 - 4d * t + 1d, 6d * t - 6d * t2, 3d * t2 - 2d * t);
    }

    // Half a cosine wave from 0 to 1: flat at both ends. t is held to 0 to 1, since past the ends the
    // wave would turn back.
    public static float Cosine(float t) => (1f - MathF.Cos(MathF.PI * Math.Clamp(t, 0f, 1f))) * 0.5f;

    public static double Cosine(double t) => (1d - Math.Cos(Math.PI * Math.Clamp(t, 0d, 1d))) * 0.5d;
}
