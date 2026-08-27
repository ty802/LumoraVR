// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// the whole lighting response is read out of one texture indexed by the view-space normal, so the
// highlight follows the camera and the surface costs nothing per light; scene lights don't touch it
[ComponentCategory("Assets/Materials")]
public class MatcapMaterial : MaterialProvider, ICommonMaterial
{
    // without it, the surface is flat tint
    [Group("Matcap")]
    public readonly AssetRef<TextureAsset> Matcap;

    [Group("Matcap")]
    public readonly Sync<colorHDR> TintColor;

    // multiplied over the matcap result, for a base albedo or a decal sheet
    [Group("Base")]
    public readonly AssetRef<TextureAsset> BaseTexture;

    [Group("Base")]
    public readonly Sync<float2> TextureScale;

    [Group("Base")]
    public readonly Sync<float2> TextureOffset;

    [Group("Normal")]
    public readonly AssetRef<TextureAsset> NormalMap;

    [Group("Normal")]
    [Range(0f, 4f, "0.00")]
    public readonly Sync<float> NormalScale;

    [Group("Rendering")]
    public readonly Sync<bool> AlphaClip;

    [Group("Rendering")]
    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> AlphaCutoff;

    [Group("Rendering")]
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.Matcap;

    public colorHDR Color
    {
        get => TintColor.Value;
        set => TintColor.Value = value;
    }

    public IAssetProvider<TextureAsset> MainTexture
    {
        get => Matcap.Target;
        set => Matcap.Target = value;
    }

    public MatcapMaterial()
    {
        Matcap = new AssetRef<TextureAsset>(this);
        TintColor = new Sync<colorHDR>(this, colorHDR.White);
        BaseTexture = new AssetRef<TextureAsset>(this);
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);
        NormalMap = new AssetRef<TextureAsset>(this);
        NormalScale = new Sync<float>(this, 1f);
        AlphaClip = new Sync<bool>(this, false);
        AlphaCutoff = new Sync<float>(this, 0.5f);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetTexture("MatcapTexture", Matcap.Asset);
        asset.SetBool("UseMatcapTexture", Matcap.Asset != null);
        asset.SetColor("TintColor", TintColor.Value);

        asset.SetTexture("BaseTexture", BaseTexture.Asset);
        asset.SetBool("UseBaseTexture", BaseTexture.Asset != null);
        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);

        asset.SetTexture("NormalMap", NormalMap.Asset);
        asset.SetBool("UseNormalMap", NormalMap.Asset != null);
        asset.SetFloat("NormalScale", NormalScale.Value);

        asset.SetBool("AlphaClip", AlphaClip.Value);
        asset.SetFloat("AlphaCutoff", AlphaCutoff.Value);
        asset.SetInt("RenderQueue", RenderQueue.Value);
    }
}
