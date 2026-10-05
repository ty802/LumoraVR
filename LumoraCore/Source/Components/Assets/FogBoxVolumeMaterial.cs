// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

public enum FogBoxColorMode
{
    Constant,
    VerticalGradient
}

public enum FogBoxSaturation
{
    Unrestricted,
    SaturateAlpha,
    SaturateColor
}

public enum FogBoxAccumulation
{
    Linear,
    Exponential,
    ExponentialSquared
}

// Fog filling a box. Put it on a unit cube (a box mesh of size 1, scaled by its slot): the fog is
// thickest where the view ray crosses the most of the box, and stops at whatever stands inside it.
// It works with the eye inside the box too.
//
// The colour is BaseColor plus the distance travelled times AccumulationColor (or a bottom-to-top
// gradient of the box) times AccumulationRate. Linear counts the distance between FogStart and FogEnd;
// the two exponential modes saturate toward one by FogDensity. WorldSpace counts metres from the eye
// to the scene through the box instead of object units inside it, so the box becomes a window that
// fogs everything seen through it by depth.
//
// GammaCurve raises the final colour; 1 keeps it linear. Additive by default, which never darkens.
// -xlinka
[ComponentCategory("Assets/Materials")]
public class FogBoxVolumeMaterial : MaterialProvider
{
    public readonly Sync<colorHDR> BaseColor;
    public readonly Sync<colorHDR> AccumulationColor;
    public readonly Sync<colorHDR> AccumulationColorBottom;
    public readonly Sync<colorHDR> AccumulationColorTop;
    public readonly Sync<float> FogStart;
    public readonly Sync<float> FogEnd;
    public readonly Sync<float> FogDensity;
    public readonly Sync<float> GammaCurve;
    public readonly Sync<bool> WorldSpace;
    public readonly Sync<FogBoxColorMode> ColorMode;
    public readonly Sync<FogBoxSaturation> SaturationMode;
    public readonly Sync<FogBoxAccumulation> AccumulationMode;

    [Range(0f, 2f, "0.00")]
    public readonly Sync<float> AccumulationRate;

    public readonly Sync<BlendMode> BlendMode;

    // -1 draws at 2540, early in the transparent band: glass and particles in front stay unfogged.
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.FogBoxVolume;

    public override bool UseLoadingPlaceholder => false;

    public FogBoxVolumeMaterial()
    {
        BaseColor = new Sync<colorHDR>(this, colorHDR.Black);
        AccumulationColor = new Sync<colorHDR>(this, colorHDR.White);
        AccumulationColorBottom = new Sync<colorHDR>(this, colorHDR.Red);
        AccumulationColorTop = new Sync<colorHDR>(this, colorHDR.Green);
        FogStart = new Sync<float>(this, 0f);
        FogEnd = new Sync<float>(this, 1000000f);
        FogDensity = new Sync<float>(this, 0f);
        GammaCurve = new Sync<float>(this, 1f);
        WorldSpace = new Sync<bool>(this, false);
        ColorMode = new Sync<FogBoxColorMode>(this, FogBoxColorMode.Constant);
        SaturationMode = new Sync<FogBoxSaturation>(this, FogBoxSaturation.SaturateAlpha);
        AccumulationMode = new Sync<FogBoxAccumulation>(this, FogBoxAccumulation.Linear);
        AccumulationRate = new Sync<float>(this, 0.5f);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Additive);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetBlendMode(BlendMode.Value);
        asset.SetColor("BaseColor", BaseColor.Value);
        asset.SetColor("AccumulationColor", AccumulationColor.Value);
        asset.SetColor("AccumulationColorBottom", AccumulationColorBottom.Value);
        asset.SetColor("AccumulationColorTop", AccumulationColorTop.Value);
        asset.SetFloat("FogStart", FogStart.Value);
        asset.SetFloat("FogEnd", FogEnd.Value);
        asset.SetFloat("FogDensity", FogDensity.Value);
        asset.SetFloat("GammaCurve", GammaCurve.Value);
        asset.SetBool("WorldSpace", WorldSpace.Value);
        asset.SetInt("ColorMode", (int)ColorMode.Value);
        asset.SetInt("SaturationMode", (int)SaturationMode.Value);
        asset.SetInt("AccumulationMode", (int)AccumulationMode.Value);
        asset.SetFloat("AccumulationRate", AccumulationRate.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value < 0 ? 2540 : RenderQueue.Value);
    }
}
