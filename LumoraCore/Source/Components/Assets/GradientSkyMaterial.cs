// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// A three-band sky with a sun: top, horizon and bottom colours blended by height, a sun disc and its glow.
// On a Skybox it is the world's sky; on a mesh, an orb say, each point shows the sky in the direction the
// viewer looks through it. The same look a GradientSkybox draws, as a material, so it can be carried,
// sampled off the sky and put back on another. -xlinka
[ComponentCategory("Assets/Materials")]
public class GradientSkyMaterial : MaterialProvider, ISkyboxMaterial
{
    [Group("Gradient")]
    public readonly Sync<color> TopColor;
    public readonly Sync<color> HorizonColor;
    public readonly Sync<color> BottomColor;

    // The direction the sunlight comes FROM, need not be normalized.
    [Group("Sun")]
    public readonly Sync<color> SunColor;
    public readonly Sync<float3> SunDirection;
    [Range(0f, 0.5f, "0.000")]
    public readonly Sync<float> SunSize;
    [Range(0f, 16f, "0.00")]
    public readonly Sync<float> SunIntensity;
    [Range(1f, 512f, "0")]
    public readonly Sync<float> SunGlowPower;

    protected override MaterialType MaterialType => MaterialType.GradientSky;

    public GradientSkyMaterial()
    {
        TopColor = new Sync<color>(this, new color(0.06f, 0.16f, 0.32f, 1f));
        HorizonColor = new Sync<color>(this, new color(0.38f, 0.74f, 0.90f, 1f));
        BottomColor = new Sync<color>(this, new color(0.015f, 0.045f, 0.08f, 1f));
        SunColor = new Sync<color>(this, new color(1.0f, 0.82f, 0.48f, 1f));
        SunDirection = new Sync<float3>(this, new float3(-0.38f, 0.58f, -0.72f));
        SunSize = new Sync<float>(this, 0.032f);
        SunIntensity = new Sync<float>(this, 1.8f);
        SunGlowPower = new Sync<float>(this, 96f);
    }

    // Takes the look of a world's gradient sky, for picking that sky up as a material.
    public void CopyFrom(GradientSkybox sky)
    {
        TopColor.Value = sky.TopColor.Value;
        HorizonColor.Value = sky.HorizonColor.Value;
        BottomColor.Value = sky.BottomColor.Value;
        SunColor.Value = sky.SunColor.Value;
        SunDirection.Value = sky.SunDirection.Value;
        SunSize.Value = sky.SunSize.Value;
        SunIntensity.Value = sky.SunIntensity.Value;
        SunGlowPower.Value = sky.SunGlowPower.Value;
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetColor("TopColor", (colorHDR)TopColor.Value);
        asset.SetColor("HorizonColor", (colorHDR)HorizonColor.Value);
        asset.SetColor("BottomColor", (colorHDR)BottomColor.Value);
        asset.SetColor("SunColor", (colorHDR)SunColor.Value);
        asset.SetFloat3("SunDirection", SunDirection.Value);
        asset.SetFloat("SunSize", SunSize.Value);
        asset.SetFloat("SunIntensity", SunIntensity.Value);
        asset.SetFloat("SunGlowPower", SunGlowPower.Value);
    }
}
