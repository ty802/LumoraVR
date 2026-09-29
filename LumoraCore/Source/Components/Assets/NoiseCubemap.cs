// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

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
}
