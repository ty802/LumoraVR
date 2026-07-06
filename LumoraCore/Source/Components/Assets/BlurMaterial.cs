// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

/// <summary>How the backdrop processes the screen content behind it.</summary>
public enum BlurMode
{
    /// <summary>Smooth gaussian blur (frosted glass).</summary>
    Gaussian,
    /// <summary>Block quantization (mosaic).</summary>
    Pixelate
}

/// <summary>
/// Frosted-glass backdrop material for modal overlays. Samples the screen behind the surface
/// and blurs (Gaussian) or quantizes (Pixelate) it, then dims toward <see cref="TintColor"/>.
/// Drives res://Shaders/Blur.gdshader.
/// </summary>
[ComponentCategory("Assets/Materials")]
public class BlurMaterial : MaterialProvider
{
    /// <summary>Gaussian (frosted) or Pixelate (mosaic).</summary>
    public readonly Sync<BlurMode> Mode;
    /// <summary>Gaussian: per-tap sample spread in px. Pixelate: block size in px.</summary>
    public readonly Sync<float> Radius;
    /// <summary>Dim/wash over the blurred result; alpha = how far to mix toward the color.</summary>
    public readonly Sync<colorHDR> TintColor;
    /// <summary>Overall opacity. < 1 lets the content behind show through (dimmed).</summary>
    public readonly Sync<float> Opacity;
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;
    public readonly Sync<int> RenderQueue;
    public readonly Sync<Rect> Rect;
    public readonly Sync<bool> RectClip;

    protected override MaterialType MaterialType => MaterialType.Blur;

    public BlurMaterial()
    {
        Mode = new Sync<BlurMode>(this, BlurMode.Gaussian);
        Radius = new Sync<float>(this, 6f);
        TintColor = new Sync<colorHDR>(this, new colorHDR(0.03f, 0.03f, 0.06f, 0.55f));
        Opacity = new Sync<float>(this, 0.85f);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Alpha);
        Culling = new Sync<Culling>(this, Assets.Culling.None);
        RenderQueue = new Sync<int>(this, 4050);
        Rect = new Sync<Rect>(this, default);
        RectClip = new Sync<bool>(this, false);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        var rect = Rect.Value;
        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        asset.SetInt("BlurMode", (int)Mode.Value);
        asset.SetFloat("BlurRadius", Radius.Value);
        asset.SetColor("TintColor", TintColor.Value);
        asset.SetFloat("Opacity", Opacity.Value);
        asset.SetFloat4("Rect", new float4(rect.xMin, rect.yMin, rect.xMax, rect.yMax));
        asset.SetBool("RectClip", RectClip.Value);
        asset.SetInt("RenderQueue", RenderQueue.Value);
    }
}
