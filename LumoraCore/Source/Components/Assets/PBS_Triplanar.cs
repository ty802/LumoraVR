// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Metallic PBR projected on three planes instead of UVs. Nothing here reads the mesh's UV channel,
// so it works on geometry that was never unwrapped: terrain, CSG offcuts, imported level chunks.
//
// RoughnessMap reads green and MetallicMap reads blue, which is the glTF occlusion/roughness/metallic
// packing the rest of the engine already assumes. One packed texture in both slots covers the normal
// case, and a plain greyscale map works in either slot because its channels are equal. -xlinka
[ComponentCategory("Assets/Materials")]
public class PBS_Triplanar : MaterialProvider, ICommonMaterial
{
    [Group("Projection")]
    public readonly Sync<float3> Tiling;

    [Group("Projection")]
    public readonly Sync<float3> Offset;

    // 1 is a wide mush, 8 is a hard seam
    [Group("Projection")]
    [Range(1f, 16f, "0.0")]
    public readonly Sync<float> BlendSharpness;

    // on: texture stays put in the world as the object moves through it; off: rides along in object space
    [Group("Projection")]
    public readonly Sync<bool> WorldSpace;

    [Group("Albedo")]
    public readonly Sync<colorHDR> AlbedoColor;

    [Group("Albedo")]
    public readonly AssetRef<TextureAsset> AlbedoTexture;

    [Group("Surface")]
    public readonly AssetRef<TextureAsset> NormalMap;

    [Group("Surface")]
    [Range(0f, 4f, "0.00")]
    public readonly Sync<float> NormalScale;

    [Group("Surface")]
    public readonly AssetRef<TextureAsset> RoughnessMap;

    [Group("Surface")]
    public readonly AssetRef<TextureAsset> MetallicMap;

    [Group("Surface")]
    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Metallic;

    [Group("Surface")]
    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Smoothness;

    [Group("Emission")]
    public readonly Sync<colorHDR> EmissiveColor;

    [Group("Rendering")]
    public readonly Sync<bool> AlphaClip;

    [Group("Rendering")]
    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> AlphaCutoff;

    [Group("Rendering")]
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.PBS_Triplanar;

    public colorHDR Color
    {
        get => AlbedoColor.Value;
        set => AlbedoColor.Value = value;
    }

    public IAssetProvider<TextureAsset> MainTexture
    {
        get => AlbedoTexture.Target;
        set => AlbedoTexture.Target = value;
    }

    public PBS_Triplanar()
    {
        Tiling = new Sync<float3>(this, float3.One);
        Offset = new Sync<float3>(this, float3.Zero);
        BlendSharpness = new Sync<float>(this, 4f);
        WorldSpace = new Sync<bool>(this, true);
        AlbedoColor = new Sync<colorHDR>(this, colorHDR.White);
        AlbedoTexture = new AssetRef<TextureAsset>(this);
        NormalMap = new AssetRef<TextureAsset>(this);
        NormalScale = new Sync<float>(this, 1f);
        RoughnessMap = new AssetRef<TextureAsset>(this);
        MetallicMap = new AssetRef<TextureAsset>(this);
        Metallic = new Sync<float>(this, 0f);
        Smoothness = new Sync<float>(this, 0.25f);
        EmissiveColor = new Sync<colorHDR>(this, colorHDR.Black);
        AlphaClip = new Sync<bool>(this, false);
        AlphaCutoff = new Sync<float>(this, 0.5f);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetFloat3("Tiling", Tiling.Value);
        asset.SetFloat3("Offset", Offset.Value);
        asset.SetFloat("BlendSharpness", BlendSharpness.Value);
        asset.SetBool("WorldSpace", WorldSpace.Value);

        asset.SetColor("AlbedoColor", AlbedoColor.Value);
        asset.SetTexture("AlbedoTexture", AlbedoTexture.Asset);

        asset.SetTexture("NormalMap", NormalMap.Asset);
        asset.SetBool("UseNormalMap", NormalMap.Asset != null);
        asset.SetFloat("NormalScale", NormalScale.Value);

        asset.SetTexture("RoughnessMap", RoughnessMap.Asset);
        asset.SetBool("UseRoughnessMap", RoughnessMap.Asset != null);
        asset.SetTexture("MetallicMap", MetallicMap.Asset);
        asset.SetBool("UseMetallicMap", MetallicMap.Asset != null);
        asset.SetFloat("Metallic", Metallic.Value);
        asset.SetFloat("Smoothness", Smoothness.Value);

        asset.SetColor("EmissiveColor", EmissiveColor.Value);

        asset.SetBool("AlphaClip", AlphaClip.Value);
        asset.SetFloat("AlphaCutoff", AlphaCutoff.Value);
        asset.SetInt("RenderQueue", RenderQueue.Value);
    }
}
