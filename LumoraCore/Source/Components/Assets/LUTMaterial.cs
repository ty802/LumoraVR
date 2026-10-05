// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

// A colour grade over whatever is BEHIND the surface, through a lookup table: film looks, night
// vision, a sepia room. Lerp blends toward SecondaryLUT, so a grade can fade in.
//
// THE TABLE IS A 2D STRIP, N slices of N by N side by side (256 by 16, 1024 by 32): red across a
// slice, green down it, blue picking the slice. There are no 3D textures here, and the strip is the
// layout grading tools export for exactly that reason. With no table set the colour passes through.
//
// UseSRGB looks the colour up gamma encoded, which is what a table made by grading an image expects;
// off, the table is read as linear in and linear out.
//
// Drawn after the transparent band, over everything it filters. -xlinka
[ComponentCategory("Assets/Materials")]
public class LUTMaterial : MaterialProvider
{
    public readonly AssetRef<TextureAsset> LUT;
    public readonly AssetRef<TextureAsset> SecondaryLUT;
    public readonly Sync<bool> UseSRGB;

    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Lerp;

    // Additive and multiply combine the graded colour with the ungraded one behind it.
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;

    // -1 draws at 3500, after ordinary transparent surfaces.
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.LUT;

    public override bool UseLoadingPlaceholder => false;

    public LUTMaterial()
    {
        LUT = new AssetRef<TextureAsset>(this);
        SecondaryLUT = new AssetRef<TextureAsset>(this);
        UseSRGB = new Sync<bool>(this, true);
        Lerp = new Sync<float>(this, 0f);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Opaque);
        Culling = new Sync<Culling>(this, Assets.Culling.Back);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        asset.SetTexture("LutTexture", LUT.Asset);
        asset.SetTexture("SecondaryLutTexture", SecondaryLUT.Asset);
        asset.SetBool("UseSrgb", UseSRGB.Value);
        asset.SetFloat("LutLerp", Lerp.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value < 0 ? 3500 : RenderQueue.Value);
    }
}
