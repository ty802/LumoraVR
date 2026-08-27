// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Fresnel outline drawn in the overlay band: additive, never depth-written, never depth-tested, both
// sides drawn, sitting in the same late render queue as the overlay unlit material so it stays
// visible through the scene. Ordering inside the band is the render queue's job.
//
// Front and Behind are a real occlusion answer, not a face-orientation split: with the depth test
// off the rasteriser cannot tell, so the shader reads the opaque pass's depth back and compares
// view-space distances. Surfaces that never wrote depth (other overlays, the sky) count as not
// occluding. -xlinka
[ComponentCategory("Assets/Materials")]
public class OverlayFresnelMaterial : MaterialProvider, ICommonMaterial
{
    // 1 is linear, higher pins the effect to the silhouette
    [Group("Fresnel")]
    [Range(0.1f, 16f, "0.00")]
    public readonly Sync<float> Exponent;

    [Group("Front")]
    public readonly Sync<colorHDR> FrontNearColor;

    [Group("Front")]
    public readonly Sync<colorHDR> FrontFarColor;

    [Group("Front")]
    public readonly Sync<float2> FrontTextureScale;

    [Group("Front")]
    public readonly Sync<float2> FrontTextureOffset;

    [Group("Behind")]
    public readonly Sync<colorHDR> BehindNearColor;

    [Group("Behind")]
    public readonly Sync<colorHDR> BehindFarColor;

    [Group("Behind")]
    public readonly Sync<float2> BehindTextureScale;

    [Group("Behind")]
    public readonly Sync<float2> BehindTextureOffset;

    // shared by both states; the front and behind UV sets pick different parts of it
    [Group("Rendering")]
    public readonly AssetRef<TextureAsset> Texture;

    [Group("Rendering")]
    public readonly Sync<bool> UseVertexColor;

    // metres of slack on the occlusion test, against depth-precision flicker
    [Group("Rendering")]
    [Range(0f, 0.5f, "0.000")]
    public readonly Sync<float> DepthBias;

    [Group("Rendering")]
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.OverlayFresnel;

    public colorHDR Color
    {
        get => FrontFarColor.Value;
        set => FrontFarColor.Value = value;
    }

    public IAssetProvider<TextureAsset> MainTexture
    {
        get => Texture.Target;
        set => Texture.Target = value;
    }

    public OverlayFresnelMaterial()
    {
        Exponent = new Sync<float>(this, 3f);
        FrontNearColor = new Sync<colorHDR>(this, colorHDR.Black);
        FrontFarColor = new Sync<colorHDR>(this, colorHDR.White);
        FrontTextureScale = new Sync<float2>(this, float2.One);
        FrontTextureOffset = new Sync<float2>(this, float2.Zero);
        BehindNearColor = new Sync<colorHDR>(this, colorHDR.Black);
        BehindFarColor = new Sync<colorHDR>(this, new colorHDR(0.25f, 0.25f, 0.25f, 1f));
        BehindTextureScale = new Sync<float2>(this, float2.One);
        BehindTextureOffset = new Sync<float2>(this, float2.Zero);
        Texture = new AssetRef<TextureAsset>(this);
        UseVertexColor = new Sync<bool>(this, false);
        DepthBias = new Sync<float>(this, 0.005f);
        // Same late band the overlay unlit material defaults to, so the two stack predictably.
        RenderQueue = new Sync<int>(this, 4010);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetFloat("Exponent", Exponent.Value);

        asset.SetColor("FrontNearColor", FrontNearColor.Value);
        asset.SetColor("FrontFarColor", FrontFarColor.Value);
        asset.SetFloat2("FrontTextureScale", FrontTextureScale.Value);
        asset.SetFloat2("FrontTextureOffset", FrontTextureOffset.Value);

        asset.SetColor("BehindNearColor", BehindNearColor.Value);
        asset.SetColor("BehindFarColor", BehindFarColor.Value);
        asset.SetFloat2("BehindTextureScale", BehindTextureScale.Value);
        asset.SetFloat2("BehindTextureOffset", BehindTextureOffset.Value);

        asset.SetTexture("Texture", Texture.Asset);
        asset.SetBool("UseVertexColor", UseVertexColor.Value);
        asset.SetFloat("DepthBias", DepthBias.Value);
        asset.SetInt("RenderQueue", RenderQueue.Value);
    }
}
