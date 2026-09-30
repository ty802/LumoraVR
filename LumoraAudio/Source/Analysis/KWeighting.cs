// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// The K-weighting curve of ITU-R BS.1770: a high shelf of about +4 dB from 2 kHz up, for what the head does
// to sound on its way to the ear, then a second-order high-pass near 38 Hz, the RLB curve. Both come from
// analog prototypes through the bilinear transform with the corner pre-warped, so every sample rate
// gets the same curve; at 48 kHz the coefficients land on the ones the recommendation tabulates.
//
// Double precision on purpose: the high-pass poles sit a hair inside the unit circle and single precision
// state around them drifts enough to move a reading. -xlinka
public sealed class KWeighting
{
    private const double ShelfFrequency = 1681.974450955533;
    private const double ShelfGainDb = 3.999843853973347;
    private const double ShelfQ = 0.7071752369554196;
    private const double HighPassFrequency = 38.13547087602444;
    private const double HighPassQ = 0.5003270373238773;

    public readonly double ShelfB0;
    public readonly double ShelfB1;
    public readonly double ShelfB2;
    public readonly double ShelfA1;
    public readonly double ShelfA2;
    public readonly double HighPassB0;
    public readonly double HighPassB1;
    public readonly double HighPassB2;
    public readonly double HighPassA1;
    public readonly double HighPassA2;

    public KWeighting(int sampleRate)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        SampleRate = sampleRate;

        double k = Math.Tan(Math.PI * ShelfFrequency / sampleRate);
        double high = Math.Pow(10.0, ShelfGainDb / 20.0);
        double band = Math.Pow(high, 0.4996667741545416);
        double a0 = 1.0 + k / ShelfQ + k * k;
        ShelfB0 = (high + band * k / ShelfQ + k * k) / a0;
        ShelfB1 = 2.0 * (k * k - high) / a0;
        ShelfB2 = (high - band * k / ShelfQ + k * k) / a0;
        ShelfA1 = 2.0 * (k * k - 1.0) / a0;
        ShelfA2 = (1.0 - k / ShelfQ + k * k) / a0;

        k = Math.Tan(Math.PI * HighPassFrequency / sampleRate);
        a0 = 1.0 + k / HighPassQ + k * k;
        HighPassB0 = 1.0;
        HighPassB1 = -2.0;
        HighPassB2 = 1.0;
        HighPassA1 = 2.0 * (k * k - 1.0) / a0;
        HighPassA2 = (1.0 - k / HighPassQ + k * k) / a0;
    }

    public int SampleRate { get; }

    // One sample through both sections; state holds four values per channel.
    public double Process(double x, ref State state)
    {
        double y = ShelfB0 * x + state.S1;
        state.S1 = ShelfB1 * x - ShelfA1 * y + state.S2;
        state.S2 = ShelfB2 * x - ShelfA2 * y;
        double z = HighPassB0 * y + state.H1;
        state.H1 = HighPassB1 * y - HighPassA1 * z + state.H2;
        state.H2 = HighPassB2 * y - HighPassA2 * z;
        return z;
    }

    // Magnitude response in dB at a frequency, from the coefficients.
    public double ResponseDb(double hz)
    {
        double w = 2.0 * Math.PI * hz / SampleRate;
        return 20.0 * Math.Log10(Section(ShelfB0, ShelfB1, ShelfB2, ShelfA1, ShelfA2, w) * Section(HighPassB0, HighPassB1, HighPassB2, HighPassA1, HighPassA2, w));
    }

    private static double Section(double b0, double b1, double b2, double a1, double a2, double w)
    {
        double c1 = Math.Cos(w), s1 = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
        double nr = b0 + b1 * c1 + b2 * c2, ni = -(b1 * s1 + b2 * s2);
        double dr = 1.0 + a1 * c1 + a2 * c2, di = -(a1 * s1 + a2 * s2);
        return Math.Sqrt((nr * nr + ni * ni) / (dr * dr + di * di));
    }

    public struct State
    {
        public double S1;
        public double S2;
        public double H1;
        public double H2;

        // Silence decays the state towards subnormals, which are slow on every CPU this runs on.
        public void Flush()
        {
            if (Math.Abs(S1) < 1e-25) S1 = 0.0;
            if (Math.Abs(S2) < 1e-25) S2 = 0.0;
            if (Math.Abs(H1) < 1e-25) H1 = 0.0;
            if (Math.Abs(H2) < 1e-25) H2 = 0.0;
        }
    }
}
