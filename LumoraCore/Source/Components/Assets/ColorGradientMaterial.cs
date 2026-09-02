// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

public enum ColorGradientMode
{
    // saturation across X, value down Y, for a fixed Hue
    SaturationValue,
    // full HSV rainbow along the axis picked by Vertical
    HueStrip,
    // linear lerp ColorA -> ColorB
    TwoColor,
    // ColorA alpha 0->1 over a procedural checkerboard (transparency preview)
    AlphaRamp
}

// same UI clip conventions as UIUnlitMaterial so it composes inside Helio panels; drives
// res://Shaders/UI_ColorGradient.gdshader
[ComponentCategory("Assets/Materials/UI")]
public class ColorGradientMaterial : MaterialProvider
{
    public readonly Sync<ColorGradientMode> Mode;
    // degrees [0,360) for the SaturationValue square
    public readonly Sync<float> Hue;
    // false = horizontal, true = vertical
    public readonly Sync<bool> Vertical;
    // TwoColor start, and the ramped color for AlphaRamp
    public readonly Sync<colorHDR> ColorA;
    public readonly Sync<colorHDR> ColorB;
    // canvas pixels; used by AlphaRamp
    public readonly Sync<float> CheckerPx;

    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;
    public readonly Sync<int> RenderQueue;
    public readonly Sync<Rect> Rect;
    public readonly Sync<bool> RectClip;
    public readonly Sync<float2> ClipOffset;
    public readonly Sync<ColorMask> ColorMask;

    protected override MaterialType MaterialType => MaterialType.UI_ColorGradient;

    public ColorGradientMaterial()
    {
        Mode = new Sync<ColorGradientMode>(this, ColorGradientMode.SaturationValue);
        Hue = new Sync<float>(this, 0f);
        Vertical = new Sync<bool>(this, false);
        ColorA = new Sync<colorHDR>(this, new colorHDR(0f, 0f, 0f, 1f));
        ColorB = new Sync<colorHDR>(this, colorHDR.White);
        CheckerPx = new Sync<float>(this, 8f);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Alpha);
        Culling = new Sync<Culling>(this, Assets.Culling.None);
        RenderQueue = new Sync<int>(this, -1);
        Rect = new Sync<Rect>(this, Lumora.Core.Math.Rect.Zero);
        RectClip = new Sync<bool>(this, false);
        ClipOffset = new Sync<float2>(this, float2.Zero);
        ColorMask = new Sync<ColorMask>(this, Assets.ColorMask.RGBA);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        var rect = Rect.Value;

        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        // Explicit property keys (not the field names) so the snake_case mapping lands the exact shader
        // uniforms: GradientMode -> gradient_mode, CheckerPx -> checker_px, ColorA -> color_a. - xlinka
        asset.SetInt("GradientMode", (int)Mode.Value);
        asset.SetFloat("Hue", Hue.Value);
        asset.SetBool("Vertical", Vertical.Value);
        asset.SetColor("ColorA", ColorA.Value);
        asset.SetColor("ColorB", ColorB.Value);
        asset.SetFloat("CheckerPx", CheckerPx.Value);
        asset.SetInt("RenderQueue", RenderQueue.Value);
        asset.SetFloat4("Rect", new float4(rect.xMin, rect.yMin, rect.xMax, rect.yMax));
        asset.SetBool("RectClip", RectClip.Value);
        asset.SetFloat2("ClipOffset", ClipOffset.Value);
        asset.SetInt("ColorMask", (int)ColorMask.Value);
    }

    // Only the two modes that actually start from a colour answer. The saturation/value square and the
    // hue strip are every colour there is, driven off Hue and the pixel you happen to be on, so there
    // is no "the colour of this material" to hand back. -xlinka
    public override bool TryGetPrimaryColor(out colorHDR color)
    {
        if (Mode.Value is ColorGradientMode.TwoColor or ColorGradientMode.AlphaRamp)
        {
            color = ColorA.Value;
            return true;
        }
        color = colorHDR.White;
        return false;
    }
}
