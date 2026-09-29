// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// Light by wavelength as linear sRGB, for Wavelength Color and Black Body Color. The eye's response is
// the CIE 1931 2 degree observer in the multi-lobe Gaussian fit of Wyman, Sloan and Shirley (2013), taken
// to linear sRGB with the D65 matrix of IEC 61966-2-1.
//
// A black body is Planck's law summed against that response every 5 nm from 360 to 830, in logs so a cold
// body does not underflow to nothing, then scaled so its brightest channel is 1. The fit's far red tail
// runs green, and once a body is cold enough that only the last few nanometres count, that green wins and
// a 200 K body would come out green. Anything under 500 K is worked out at 500 K, which is pure red already,
// as everything up to about 800 K is. -xlinka
internal static class VisibleSpectrum
{
    private const double FirstNm = 360.0;
    private const double StepNm = 5.0;
    private const int Samples = 95;
    // Planck's second radiation constant hc/k, in nm K.
    private const double C2 = 1.438777e7;
    private const double ColdestKelvin = 500.0;
    // Hotter looks no different, and it keeps hc/(nm k T) clear of 0.
    private const double HottestKelvin = 1e9;

    private static readonly double[] Nm = new double[Samples];
    private static readonly double[] LogScale = new double[Samples];
    private static readonly double[] R = new double[Samples];
    private static readonly double[] G = new double[Samples];
    private static readonly double[] B = new double[Samples];

    static VisibleSpectrum()
    {
        for (int i = 0; i < Samples; i++)
        {
            Nm[i] = FirstNm + i * StepNm;
            LogScale[i] = -5.0 * Math.Log(Nm[i]);
            Response(Nm[i], out R[i], out G[i], out B[i]);
        }
    }

    public static color Wavelength(float nm)
    {
        Response(nm, out double r, out double g, out double b);
        return new color(Unit(r), Unit(g), Unit(b), 1f);
    }

    public static color BlackBody(float kelvin)
    {
        if (!(kelvin > 0f))
            return color.Black;
        double t = Math.Clamp(kelvin, ColdestKelvin, HottestKelvin);

        Span<double> log = stackalloc double[Samples];
        double top = double.NegativeInfinity;
        for (int i = 0; i < Samples; i++)
        {
            double x = C2 / (Nm[i] * t);
            log[i] = LogScale[i] - (x < 30.0 ? Math.Log(Math.Exp(x) - 1.0) : x);
            top = Math.Max(top, log[i]);
        }

        double r = 0.0, g = 0.0, b = 0.0;
        for (int i = 0; i < Samples; i++)
        {
            double weight = Math.Exp(log[i] - top);
            r += weight * R[i];
            g += weight * G[i];
            b += weight * B[i];
        }
        double peak = Math.Max(r, Math.Max(g, b));
        return peak > 0.0 ? new color(Unit(r / peak), Unit(g / peak), Unit(b / peak), 1f) : color.Black;
    }

    private static void Response(double nm, out double r, out double g, out double b)
    {
        double x = 1.056 * Lobe(nm, 599.8, 37.9, 31.0) + 0.362 * Lobe(nm, 442.0, 16.0, 26.7)
            - 0.065 * Lobe(nm, 501.1, 20.4, 26.2);
        double y = 0.821 * Lobe(nm, 568.8, 46.9, 40.5) + 0.286 * Lobe(nm, 530.9, 16.3, 31.1);
        double z = 1.217 * Lobe(nm, 437.0, 11.8, 36.0) + 0.681 * Lobe(nm, 459.0, 26.0, 13.8);
        r = 3.2406 * x - 1.5372 * y - 0.4986 * z;
        g = -0.9689 * x + 1.8758 * y + 0.0415 * z;
        b = 0.0557 * x - 0.2040 * y + 1.0570 * z;
    }

    // A bell with a different width either side of its peak.
    private static double Lobe(double nm, double peak, double widthBelow, double widthAbove)
    {
        double t = (nm - peak) / (nm < peak ? widthBelow : widthAbove);
        return Math.Exp(-0.5 * t * t);
    }

    private static float Unit(double value) => value > 0.0 ? (float)Math.Min(value, 1.0) : 0f;
}
