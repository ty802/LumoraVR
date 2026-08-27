// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Simplex noise over the sphere, blended between two colors. Sampling 3D noise along the view
// direction rather than per-face 2D noise is what keeps the pattern continuous across the cube
// edges; a per-face generator would show every seam. -xlinka
[ComponentCategory("Assets/Cubemaps")]
public class NoiseCubemap : ProceduralCubemap
{
    public readonly Sync<color> Background;
    public readonly Sync<color> Foreground;

    // higher packs more detail into the same sphere
    [Range(0.1f, 32f, "0.00")]
    public readonly Sync<float> Scale;

    // each octave costs another noise evaluation per pixel
    [Range(1, 6, "F0")]
    public readonly Sync<int> Octaves;

    [Range(0.1f, 0.9f, "0.00")]
    public readonly Sync<float> Persistence;

    // two cubemaps with different offsets never match
    public readonly Sync<float3> Offset;

    private color _background, _foreground;
    private float _scale, _persistence;
    private float3 _offset;
    private int _octaves;

    public NoiseCubemap()
    {
        Background = new Sync<color>(this, new color(0.02f, 0.03f, 0.06f, 1f));
        Foreground = new Sync<color>(this, new color(0.85f, 0.88f, 1.0f, 1f));
        Scale = new Sync<float>(this, 4f);
        Octaves = new Sync<int>(this, 3);
        Persistence = new Sync<float>(this, 0.5f);
        Offset = new Sync<float3>(this, float3.Zero);
    }

    protected override void PrepareSample()
    {
        _background = Background.Value;
        _foreground = Foreground.Value;
        _scale = System.Math.Max(0.001f, Scale.Value);
        _octaves = System.Math.Clamp(Octaves.Value, 1, 6);
        _persistence = System.Math.Clamp(Persistence.Value, 0.05f, 0.95f);
        _offset = Offset.Value;
    }

    protected override color Sample(CubemapFace face, in float3 direction)
    {
        float3 p = direction * _scale + _offset;

        float amplitude = 1f;
        float total = 0f;
        float normalizer = 0f;
        for (int i = 0; i < _octaves; i++)
        {
            total += Simplex.Noise3D(p.x, p.y, p.z) * amplitude;
            normalizer += amplitude;
            amplitude *= _persistence;
            p *= 2f;
        }

        float t = System.Math.Clamp(total / normalizer * 0.5f + 0.5f, 0f, 1f);
        return new color(
            _background.r + (_foreground.r - _background.r) * t,
            _background.g + (_foreground.g - _background.g) * t,
            _background.b + (_foreground.b - _background.b) * t,
            1f);
    }

    // output roughly -1..1. Lives here rather than in the shared math library because this is currently
    // its only caller; move it out if a second caller shows up, do not copy it.
    private static class Simplex
    {
        private const float F3 = 1f / 3f;
        private const float G3 = 1f / 6f;

        private static readonly int[] Gradients =
        {
            1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1, 0,
            1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, -1,
            0, 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1,
        };

        private static readonly int[] Permutation = BuildPermutation();

        private static int[] BuildPermutation()
        {
            // Fixed table, doubled so the lookups never need a modulo. The seed is a constant on
            // purpose: the same NoiseCubemap must generate the same sky on every peer.
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

        private static float Corner(int gradient, float x, float y, float z)
        {
            float t = 0.6f - x * x - y * y - z * z;
            if (t < 0f)
                return 0f;
            t *= t;
            int g = gradient * 3;
            return t * t * (Gradients[g] * x + Gradients[g + 1] * y + Gradients[g + 2] * z);
        }

        private static int FastFloor(float value)
        {
            int i = (int)value;
            return value < i ? i - 1 : i;
        }
    }
}
