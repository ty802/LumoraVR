// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Three-band sky gradient with an optional sun disc, baked into a cubemap.
//
// This is the same look the gradient sky shader draws, but as an asset rather than a shader, which
// is the whole point of it existing: a shader sky is only a background, while a cubemap can also be
// what a reflection probe falls back to and what a material samples. If all you want is a
// background, the shader version is cheaper - it costs no memory and no generation pass. -xlinka
[ComponentCategory("Assets/Cubemaps")]
public class GradientCubemap : ProceduralCubemap
{
    public readonly Sync<color> TopColor;
    public readonly Sync<color> HorizonColor;
    public readonly Sync<color> BottomColor;

    // 1 is a plain linear ramp
    [Range(0.2f, 8f, "0.00")]
    public readonly Sync<float> HorizonFalloff;

    public readonly Sync<bool> DrawSun;
    public readonly Sync<color> SunColor;
    public readonly Sync<float3> SunDirection;

    // radians
    [Range(0.004f, 0.4f, "0.000")]
    public readonly Sync<float> SunSize;

    // multiple of the sun disc's radius
    [Range(1f, 40f, "0.0")]
    public readonly Sync<float> SunGlow;

    // Snapshot read by the sampler threads.
    private color _top, _horizon, _bottom, _sun;
    private float3 _sunDir;
    private float _falloff, _sunCos, _glowCos;
    private bool _drawSun;

    public GradientCubemap()
    {
        TopColor = new Sync<color>(this, new color(0.06f, 0.16f, 0.32f, 1f));
        HorizonColor = new Sync<color>(this, new color(0.38f, 0.74f, 0.90f, 1f));
        BottomColor = new Sync<color>(this, new color(0.015f, 0.045f, 0.08f, 1f));
        HorizonFalloff = new Sync<float>(this, 1.4f);
        DrawSun = new Sync<bool>(this, false);
        SunColor = new Sync<color>(this, new color(1.0f, 0.82f, 0.48f, 1f));
        SunDirection = new Sync<float3>(this, new float3(-0.38f, 0.58f, -0.72f));
        SunSize = new Sync<float>(this, 0.032f);
        SunGlow = new Sync<float>(this, 8f);
    }

    protected override void PrepareSample()
    {
        _top = TopColor.Value;
        _horizon = HorizonColor.Value;
        _bottom = BottomColor.Value;
        _falloff = System.Math.Max(0.05f, HorizonFalloff.Value);

        _drawSun = DrawSun.Value;
        _sun = SunColor.Value;
        _sunDir = SunDirection.Value.Normalized;

        // Compare cosines, not angles: the sampler already has a dot product and an acos per pixel
        // across six faces is real time for a value that never leaves the 0..1 band anyway.
        float radius = System.Math.Clamp(SunSize.Value, 0.001f, 1.5f);
        _sunCos = MathF.Cos(radius);
        _glowCos = MathF.Cos(System.Math.Min(radius * System.Math.Max(1f, SunGlow.Value), MathF.PI));
    }

    protected override color Sample(CubemapFace face, in float3 direction)
    {
        float height = direction.y;
        color sky;
        if (height >= 0f)
            sky = Blend(_horizon, _top, MathF.Pow(height, _falloff));
        else
            sky = Blend(_horizon, _bottom, MathF.Pow(-height, _falloff));

        if (!_drawSun)
            return sky;

        float d = float3.Dot(direction, _sunDir);
        if (d <= _glowCos)
            return sky;

        // Disc first, then the wider glow ramp underneath it, so the two do not double up at the rim.
        float disc = d >= _sunCos ? 1f : 0f;
        float glow = _sunCos > _glowCos ? Saturate((d - _glowCos) / (_sunCos - _glowCos)) : 0f;
        float amount = Saturate(disc + glow * glow * 0.35f);

        return new color(
            sky.r + _sun.r * amount,
            sky.g + _sun.g * amount,
            sky.b + _sun.b * amount,
            1f);
    }

    private static color Blend(in color a, in color b, float t)
    {
        t = Saturate(t);
        return new color(
            a.r + (b.r - a.r) * t,
            a.g + (b.g - a.g) * t,
            a.b + (b.b - a.b) * t,
            1f);
    }

    private static float Saturate(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
}
