// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Metallic PBR whose albedo is multiplied by the mesh's vertex color. For baked-colour imports,
// point-cloud style geometry, and procedural meshes that carry their palette in the color channel.
//
// UseVertexAlpha swaps the shader rather than flipping a uniform. A Godot shader that writes ALPHA
// anywhere is treated as transparent for the whole material, so keeping the alpha write behind a
// runtime branch would cost every opaque user the blended pass. Two variants, one switch. -xlinka
[ComponentCategory("Assets/Materials")]
public class PBS_VertexColor : MaterialProvider, ICommonMaterial
{
    [Group("Albedo")]
    public readonly Sync<colorHDR> AlbedoColor;

    [Group("Albedo")]
    public readonly AssetRef<TextureAsset> AlbedoTexture;

    [Group("Albedo")]
    public readonly Sync<float2> TextureScale;

    [Group("Albedo")]
    public readonly Sync<float2> TextureOffset;

    // switches to the blended shader variant
    [Group("Albedo")]
    public readonly Sync<bool> UseVertexAlpha;

    [Group("Surface")]
    public readonly AssetRef<TextureAsset> NormalMap;

    [Group("Surface")]
    [Range(0f, 4f, "0.00")]
    public readonly Sync<float> NormalScale;

    // green read as roughness, blue as metallic (glTF packing)
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
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.PBS_VertexColor;

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

    public PBS_VertexColor()
    {
        AlbedoColor = new Sync<colorHDR>(this, colorHDR.White);
        AlbedoTexture = new AssetRef<TextureAsset>(this);
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);
        UseVertexAlpha = new Sync<bool>(this, false);
        NormalMap = new AssetRef<TextureAsset>(this);
        NormalScale = new Sync<float>(this, 1f);
        MetallicMap = new AssetRef<TextureAsset>(this);
        Metallic = new Sync<float>(this, 0f);
        Smoothness = new Sync<float>(this, 0.25f);
        EmissiveColor = new Sync<colorHDR>(this, colorHDR.Black);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        // Pushed first: the hook swaps the shader on this one, and uniform values survive the swap.
        asset.SetBool("UseVertexAlpha", UseVertexAlpha.Value);

        asset.SetColor("AlbedoColor", AlbedoColor.Value);
        asset.SetTexture("AlbedoTexture", AlbedoTexture.Asset);
        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);

        asset.SetTexture("NormalMap", NormalMap.Asset);
        asset.SetBool("UseNormalMap", NormalMap.Asset != null);
        asset.SetFloat("NormalScale", NormalScale.Value);

        asset.SetTexture("MetallicMap", MetallicMap.Asset);
        asset.SetBool("UseMetallicMap", MetallicMap.Asset != null);
        asset.SetFloat("Metallic", Metallic.Value);
        asset.SetFloat("Smoothness", Smoothness.Value);

        asset.SetColor("EmissiveColor", EmissiveColor.Value);
        asset.SetInt("RenderQueue", RenderQueue.Value);
    }
}
