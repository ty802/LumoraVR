// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Math;

// The real spherical harmonic basis up to band 4, for a unit direction. Coefficient i of an SH value
// multiplies basis i; band b holds coefficients b*b through (b+1)*(b+1)-1. -xlinka
public static class SphericalHarmonics
{
    public const int MaxCoefficients = 25;

    public static void Basis(float3 direction, Span<float> basis)
    {
        var d = direction.Normalized;
        float x = d.x, y = d.y, z = d.z;
        float xx = x * x, yy = y * y, zz = z * z;
        int n = basis.Length;

        basis[0] = 0.2820948f;
        if (n <= 1) return;

        basis[1] = 0.4886025f * y;
        basis[2] = 0.4886025f * z;
        basis[3] = 0.4886025f * x;
        if (n <= 4) return;

        basis[4] = 1.0925484f * x * y;
        basis[5] = 1.0925484f * y * z;
        basis[6] = 0.3153916f * (3f * zz - 1f);
        basis[7] = 1.0925484f * x * z;
        basis[8] = 0.5462742f * (xx - yy);
        if (n <= 9) return;

        basis[9] = 0.5900436f * y * (3f * xx - yy);
        basis[10] = 2.8906114f * x * y * z;
        basis[11] = 0.4570458f * y * (5f * zz - 1f);
        basis[12] = 0.3731763f * z * (5f * zz - 3f);
        basis[13] = 0.4570458f * x * (5f * zz - 1f);
        basis[14] = 1.4453057f * z * (xx - yy);
        basis[15] = 0.5900436f * x * (xx - 3f * yy);
        if (n <= 16) return;

        basis[16] = 2.5033430f * x * y * (xx - yy);
        basis[17] = 1.7701308f * y * z * (3f * xx - yy);
        basis[18] = 0.9461747f * x * y * (7f * zz - 1f);
        basis[19] = 0.6690465f * y * z * (7f * zz - 3f);
        basis[20] = 0.1057855f * (35f * zz * zz - 30f * zz + 3f);
        basis[21] = 0.6690465f * x * z * (7f * zz - 3f);
        basis[22] = 0.4730874f * (xx - yy) * (7f * zz - 1f);
        basis[23] = 1.7701308f * x * z * (xx - 3f * yy);
        basis[24] = 0.6258357f * (xx * (xx - 3f * yy) - yy * (3f * xx - yy));
    }

    // The band a coefficient belongs to.
    public static int BandOf(int coefficient) => (int)MathF.Sqrt(coefficient);

    public static float3 Evaluate(ReadOnlySpan<float3> coefficients, float3 direction)
    {
        Span<float> basis = stackalloc float[coefficients.Length];
        Basis(direction, basis);
        var sum = float3.Zero;
        for (int i = 0; i < coefficients.Length; i++)
            sum += coefficients[i] * basis[i];
        return sum;
    }
}
