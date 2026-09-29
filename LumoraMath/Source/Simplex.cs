// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Math;

// Simplex noise in one to four dimensions, each about -1 to 1 and smooth everywhere. One fixed
// permutation for all of them, so the same position gives the same value on every machine and every
// run: a noise cubemap generates the same sky on every peer, and a script sampling noise agrees with
// itself across a session. -xlinka
public static class Simplex
{
    private const float F2 = 0.36602540378f;
    private const float G2 = 0.21132486540f;
    private const float F3 = 1f / 3f;
    private const float G3 = 1f / 6f;
    private const float F4 = 0.30901699437f;
    private const float G4 = 0.13819660113f;

    private static readonly int[] Gradients3 =
    {
        1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1, 0,
        1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, -1,
        0, 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1,
    };

    // Every mix of one zero and three signed ones.
    private static readonly int[] Gradients4 =
    {
        0, 1, 1, 1, 0, 1, 1, -1, 0, 1, -1, 1, 0, 1, -1, -1,
        0, -1, 1, 1, 0, -1, 1, -1, 0, -1, -1, 1, 0, -1, -1, -1,
        1, 0, 1, 1, 1, 0, 1, -1, 1, 0, -1, 1, 1, 0, -1, -1,
        -1, 0, 1, 1, -1, 0, 1, -1, -1, 0, -1, 1, -1, 0, -1, -1,
        1, 1, 0, 1, 1, 1, 0, -1, 1, -1, 0, 1, 1, -1, 0, -1,
        -1, 1, 0, 1, -1, 1, 0, -1, -1, -1, 0, 1, -1, -1, 0, -1,
        1, 1, 1, 0, 1, 1, -1, 0, 1, -1, 1, 0, 1, -1, -1, 0,
        -1, 1, 1, 0, -1, 1, -1, 0, -1, -1, 1, 0, -1, -1, -1, 0,
    };

    private static readonly int[] Permutation = BuildPermutation();

    // Fixed table, doubled so the lookups never need a modulo. The seed is a constant on purpose.
    private static int[] BuildPermutation()
    {
        var source = new int[256];
        for (int i = 0; i < 256; i++)
            source[i] = i;

        var random = new Random(1337);
        for (int i = 255; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (source[i], source[j]) = (source[j], source[i]);
        }

        var table = new int[512];
        for (int i = 0; i < 512; i++)
            table[i] = source[i & 255];
        return table;
    }

    public static float Noise1D(float x)
    {
        int i0 = FastFloor(x);
        float x0 = x - i0;
        float x1 = x0 - 1f;
        float n = Corner(Permutation[i0 & 255], x0) + Corner(Permutation[(i0 + 1) & 255], x1);
        return 0.395f * n;
    }

    public static float Noise2D(float x, float y)
    {
        float s = (x + y) * F2;
        int i = FastFloor(x + s), j = FastFloor(y + s);

        float t = (i + j) * G2;
        float x0 = x - (i - t), y0 = y - (j - t);

        int i1 = x0 > y0 ? 1 : 0;
        int j1 = 1 - i1;

        float x1 = x0 - i1 + G2, y1 = y0 - j1 + G2;
        float x2 = x0 - 1f + 2f * G2, y2 = y0 - 1f + 2f * G2;

        int ii = i & 255, jj = j & 255;
        int g0 = Permutation[ii + Permutation[jj]] % 12;
        int g1 = Permutation[ii + i1 + Permutation[jj + j1]] % 12;
        int g2 = Permutation[ii + 1 + Permutation[jj + 1]] % 12;

        float n = Corner(g0, x0, y0) + Corner(g1, x1, y1) + Corner(g2, x2, y2);
        return 70f * n;
    }

    public static float Noise3D(float x, float y, float z)
    {
        float s = (x + y + z) * F3;
        int i = FastFloor(x + s), j = FastFloor(y + s), k = FastFloor(z + s);

        float t = (i + j + k) * G3;
        float x0 = x - (i - t), y0 = y - (j - t), z0 = z - (k - t);

        int i1, j1, k1, i2, j2, k2;
        if (x0 >= y0)
        {
            if (y0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
            else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
            else { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
        }
        else
        {
            if (y0 < z0) { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
            else if (x0 < z0) { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
            else { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
        }

        float x1 = x0 - i1 + G3, y1 = y0 - j1 + G3, z1 = z0 - k1 + G3;
        float x2 = x0 - i2 + 2f * G3, y2 = y0 - j2 + 2f * G3, z2 = z0 - k2 + 2f * G3;
        float x3 = x0 - 1f + 3f * G3, y3 = y0 - 1f + 3f * G3, z3 = z0 - 1f + 3f * G3;

        int ii = i & 255, jj = j & 255, kk = k & 255;
        int g0 = Permutation[ii + Permutation[jj + Permutation[kk]]] % 12;
        int g1 = Permutation[ii + i1 + Permutation[jj + j1 + Permutation[kk + k1]]] % 12;
        int g2 = Permutation[ii + i2 + Permutation[jj + j2 + Permutation[kk + k2]]] % 12;
        int g3 = Permutation[ii + 1 + Permutation[jj + 1 + Permutation[kk + 1]]] % 12;

        float n = Corner(g0, x0, y0, z0)
                + Corner(g1, x1, y1, z1)
                + Corner(g2, x2, y2, z2)
                + Corner(g3, x3, y3, z3);
        return 32f * n;
    }

    // The corner order comes from ranking the four offsets against each other.
    public static float Noise4D(float x, float y, float z, float w)
    {
        float s = (x + y + z + w) * F4;
        int i = FastFloor(x + s), j = FastFloor(y + s), k = FastFloor(z + s), l = FastFloor(w + s);

        float t = (i + j + k + l) * G4;
        float x0 = x - (i - t), y0 = y - (j - t), z0 = z - (k - t), w0 = w - (l - t);

        int rankX = 0, rankY = 0, rankZ = 0, rankW = 0;
        if (x0 > y0) rankX++; else rankY++;
        if (x0 > z0) rankX++; else rankZ++;
        if (x0 > w0) rankX++; else rankW++;
        if (y0 > z0) rankY++; else rankZ++;
        if (y0 > w0) rankY++; else rankW++;
        if (z0 > w0) rankZ++; else rankW++;

        int i1 = rankX >= 3 ? 1 : 0, j1 = rankY >= 3 ? 1 : 0, k1 = rankZ >= 3 ? 1 : 0, l1 = rankW >= 3 ? 1 : 0;
        int i2 = rankX >= 2 ? 1 : 0, j2 = rankY >= 2 ? 1 : 0, k2 = rankZ >= 2 ? 1 : 0, l2 = rankW >= 2 ? 1 : 0;
        int i3 = rankX >= 1 ? 1 : 0, j3 = rankY >= 1 ? 1 : 0, k3 = rankZ >= 1 ? 1 : 0, l3 = rankW >= 1 ? 1 : 0;

        float x1 = x0 - i1 + G4, y1 = y0 - j1 + G4, z1 = z0 - k1 + G4, w1 = w0 - l1 + G4;
        float x2 = x0 - i2 + 2f * G4, y2 = y0 - j2 + 2f * G4, z2 = z0 - k2 + 2f * G4, w2 = w0 - l2 + 2f * G4;
        float x3 = x0 - i3 + 3f * G4, y3 = y0 - j3 + 3f * G4, z3 = z0 - k3 + 3f * G4, w3 = w0 - l3 + 3f * G4;
        float x4 = x0 - 1f + 4f * G4, y4 = y0 - 1f + 4f * G4, z4 = z0 - 1f + 4f * G4, w4 = w0 - 1f + 4f * G4;

        int ii = i & 255, jj = j & 255, kk = k & 255, ll = l & 255;
        int g0 = Permutation[ii + Permutation[jj + Permutation[kk + Permutation[ll]]]] & 31;
        int g1 = Permutation[ii + i1 + Permutation[jj + j1 + Permutation[kk + k1 + Permutation[ll + l1]]]] & 31;
        int g2 = Permutation[ii + i2 + Permutation[jj + j2 + Permutation[kk + k2 + Permutation[ll + l2]]]] & 31;
        int g3 = Permutation[ii + i3 + Permutation[jj + j3 + Permutation[kk + k3 + Permutation[ll + l3]]]] & 31;
        int g4 = Permutation[ii + 1 + Permutation[jj + 1 + Permutation[kk + 1 + Permutation[ll + 1]]]] & 31;

        float n = Corner(g0, x0, y0, z0, w0)
                + Corner(g1, x1, y1, z1, w1)
                + Corner(g2, x2, y2, z2, w2)
                + Corner(g3, x3, y3, z3, w3)
                + Corner(g4, x4, y4, z4, w4);
        return 27f * n;
    }

    // Past the corner's reach it adds nothing, which also keeps a position past the ends of int finite.
    private static float Corner(int hash, float x)
    {
        float t = 1f - x * x;
        if (t < 0f)
            return 0f;
        t *= t;
        int h = hash & 15;
        float gradient = 1f + (h & 7);
        return t * t * ((h & 8) != 0 ? -gradient * x : gradient * x);
    }

    private static float Corner(int gradient, float x, float y)
    {
        float t = 0.5f - x * x - y * y;
        if (t < 0f)
            return 0f;
        t *= t;
        int g = gradient * 3;
        return t * t * (Gradients3[g] * x + Gradients3[g + 1] * y);
    }

    private static float Corner(int gradient, float x, float y, float z)
    {
        float t = 0.6f - x * x - y * y - z * z;
        if (t < 0f)
            return 0f;
        t *= t;
        int g = gradient * 3;
        return t * t * (Gradients3[g] * x + Gradients3[g + 1] * y + Gradients3[g + 2] * z);
    }

    private static float Corner(int gradient, float x, float y, float z, float w)
    {
        float t = 0.6f - x * x - y * y - z * z - w * w;
        if (t < 0f)
            return 0f;
        t *= t;
        int g = gradient * 4;
        return t * t * (Gradients4[g] * x + Gradients4[g + 1] * y + Gradients4[g + 2] * z + Gradients4[g + 3] * w);
    }

    // Past the ends of int a coordinate holds at the end, where a cast would give whatever the machine
    // answers; NaN is 0.
    private static int FastFloor(float value)
    {
        if (!(value > int.MinValue))
            return float.IsNaN(value) ? 0 : int.MinValue;
        if (value >= int.MaxValue)
            return int.MaxValue;
        int i = (int)value;
        return value < i ? i - 1 : i;
    }
}
