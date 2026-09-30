// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// One second-order section as a trapezoidal state-variable filter: two integrators with corner G = tan(pi f / fs)
// and damping K, and an output mix of the input, the band state and the low state.
//
// Every RBJ cookbook response is one such mix. Trapezoidal integration is the bilinear transform, and G carries
// the cookbook's own prewarp at the corner, so each magnitude curve is the cookbook's exactly: the shelves put
// their corner at f / sqrt(A) or f * sqrt(A) and the bell folds A into the damping, and the transfer function
// that comes out is the cookbook one term for term. What the section adds is behaviour under motion: its states
// are integrator memories rather than past outputs, so a corner or a Q that moves every sample does not throw
// the transients a direct-form biquad throws under the same sweep, and it cannot go unstable while G and K stay
// positive. -xlinka
internal struct SvfDesign : IEquatable<SvfDesign>
{
    public float G;
    public float K;
    public float M0;
    public float M1;
    public float M2;

    // Passes the input untouched with the integrators frozen: what a section not in use is set to.
    public static SvfDesign Identity => new() { G = 0f, K = 2f, M0 = 1f };

    public readonly bool IsIdentity => G == 0f && M0 == 1f && M1 == 0f && M2 == 0f;

    public static SvfDesign Make(BiquadFilterType type, double frequency, double q, double gainDb, double sampleRate)
    {
        double g = Math.Tan(Math.PI * frequency / sampleRate);
        double k = 1.0 / q;
        double a = Math.Pow(10.0, gainDb / 40.0);
        switch (type)
        {
            case BiquadFilterType.LowPass:
                return Of(g, k, 0.0, 0.0, 1.0);
            case BiquadFilterType.HighPass:
                return Of(g, k, 1.0, -k, -1.0);
            case BiquadFilterType.BandPass:
                return Of(g, k, 0.0, k, 0.0);
            case BiquadFilterType.Notch:
                return Of(g, k, 1.0, -k, 0.0);
            case BiquadFilterType.Peaking:
            {
                double bell = 1.0 / (q * a);
                return Of(g, bell, 1.0, bell * (a * a - 1.0), 0.0);
            }
            case BiquadFilterType.LowShelf:
                return Of(g / Math.Sqrt(a), k, 1.0, k * (a - 1.0), a * a - 1.0);
            case BiquadFilterType.HighShelf:
                return Of(g * Math.Sqrt(a), k, a * a, k * (1.0 - a) * a, 1.0 - a * a);
            default:
                return Of(g, k, 1.0, -2.0 * k, 0.0);
        }
    }

    public static SvfDesign Lerp(in SvfDesign from, in SvfDesign to, float t) => new()
    {
        G = from.G + (to.G - from.G) * t,
        K = from.K + (to.K - from.K) * t,
        M0 = from.M0 + (to.M0 - from.M0) * t,
        M1 = from.M1 + (to.M1 - from.M1) * t,
        M2 = from.M2 + (to.M2 - from.M2) * t,
    };

    public readonly bool Equals(SvfDesign other)
        => G == other.G && K == other.K && M0 == other.M0 && M1 == other.M1 && M2 == other.M2;

    public override readonly bool Equals(object? obj) => obj is SvfDesign other && Equals(other);

    public override readonly int GetHashCode() => HashCode.Combine(G, K, M0, M1, M2);

    private static SvfDesign Of(double g, double k, double m0, double m1, double m2) => new()
    {
        G = (float)g,
        K = (float)k,
        M0 = (float)m0,
        M1 = (float)m1,
        M2 = (float)m2,
    };
}
