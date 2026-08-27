// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Unlit crossfade between two complete fresnel sets. Each set has a near colour and texture (facing
// the viewer), a far colour and texture (at a grazing angle), and its own exponent; Lerp slides
// between the two sets, so one field animates a whole look change instead of eight.
//
// Near/far is the fresnel term, not distance: near is where the surface faces you, far is the
// silhouette. -xlinka
[ComponentCategory("Assets/Materials")]
public class FresnelLerpMaterial : MaterialProvider, ICommonMaterial
{
    [Group("Lerp")]
    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Lerp;

    // read off the red channel and multiplied in on top of the scalar Lerp
    [Group("Lerp")]
    public readonly AssetRef<TextureAsset> LerpTexture;

    [Group("Lerp")]
    public readonly Sync<float2> LerpTextureScale;

    [Group("Lerp")]
    public readonly Sync<float2> LerpTextureOffset;

    [Group("Set 0")]
    public readonly Sync<colorHDR> NearColor0;

    [Group("Set 0")]
    public readonly Sync<colorHDR> FarColor0;

    [Group("Set 0")]
    public readonly AssetRef<TextureAsset> NearTexture0;

    [Group("Set 0")]
    public readonly AssetRef<TextureAsset> FarTexture0;

    [Group("Set 0")]
    [Range(0.1f, 16f, "0.00")]
    public readonly Sync<float> Exponent0;

    [Group("Set 1")]
    public readonly Sync<colorHDR> NearColor1;

    [Group("Set 1")]
    public readonly Sync<colorHDR> FarColor1;

    [Group("Set 1")]
    public readonly AssetRef<TextureAsset> NearTexture1;

    [Group("Set 1")]
    public readonly AssetRef<TextureAsset> FarTexture1;

    [Group("Set 1")]
    [Range(0.1f, 16f, "0.00")]
    public readonly Sync<float> Exponent1;

    [Group("Rendering")]
    public readonly Sync<float2> TextureScale;

    [Group("Rendering")]
    public readonly Sync<float2> TextureOffset;

    [Group("Rendering")]
    public readonly Sync<bool> UseVertexColor;

    [Group("Rendering")]
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.FresnelLerp;

    public colorHDR Color
    {
        get => NearColor0.Value;
        set => NearColor0.Value = value;
    }

    public IAssetProvider<TextureAsset> MainTexture
    {
        get => NearTexture0.Target;
        set => NearTexture0.Target = value;
    }

    public FresnelLerpMaterial()
    {
        Lerp = new Sync<float>(this, 0f);
        LerpTexture = new AssetRef<TextureAsset>(this);
        LerpTextureScale = new Sync<float2>(this, float2.One);
        LerpTextureOffset = new Sync<float2>(this, float2.Zero);

        NearColor0 = new Sync<colorHDR>(this, colorHDR.White);
        FarColor0 = new Sync<colorHDR>(this, colorHDR.Black);
        NearTexture0 = new AssetRef<TextureAsset>(this);
        FarTexture0 = new AssetRef<TextureAsset>(this);
        Exponent0 = new Sync<float>(this, 1f);

        NearColor1 = new Sync<colorHDR>(this, new colorHDR(0.8f, 0.8f, 0.8f, 1f));
        FarColor1 = new Sync<colorHDR>(this, new colorHDR(0.2f, 0.2f, 0.2f, 1f));
        NearTexture1 = new AssetRef<TextureAsset>(this);
        FarTexture1 = new AssetRef<TextureAsset>(this);
        Exponent1 = new Sync<float>(this, 1f);

        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);
        UseVertexColor = new Sync<bool>(this, false);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetFloat("Lerp", Lerp.Value);
        asset.SetTexture("LerpTexture", LerpTexture.Asset);
        asset.SetBool("UseLerpTexture", LerpTexture.Asset != null);
        asset.SetFloat2("LerpTextureScale", LerpTextureScale.Value);
        asset.SetFloat2("LerpTextureOffset", LerpTextureOffset.Value);

        asset.SetColor("NearColor0", NearColor0.Value);
        asset.SetColor("FarColor0", FarColor0.Value);
        asset.SetTexture("NearTexture0", NearTexture0.Asset);
        asset.SetBool("UseNearTexture0", NearTexture0.Asset != null);
        asset.SetTexture("FarTexture0", FarTexture0.Asset);
        asset.SetBool("UseFarTexture0", FarTexture0.Asset != null);
        asset.SetFloat("Exponent0", Exponent0.Value);

        asset.SetColor("NearColor1", NearColor1.Value);
        asset.SetColor("FarColor1", FarColor1.Value);
        asset.SetTexture("NearTexture1", NearTexture1.Asset);
        asset.SetBool("UseNearTexture1", NearTexture1.Asset != null);
        asset.SetTexture("FarTexture1", FarTexture1.Asset);
        asset.SetBool("UseFarTexture1", FarTexture1.Asset != null);
        asset.SetFloat("Exponent1", Exponent1.Value);

        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);
        asset.SetBool("UseVertexColor", UseVertexColor.Value);
        asset.SetInt("RenderQueue", RenderQueue.Value);
    }
}
