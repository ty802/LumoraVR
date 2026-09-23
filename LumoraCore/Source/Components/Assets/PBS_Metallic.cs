// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

/// <summary>
/// Physically-Based Rendering material using the metallic workflow.
/// Maps to StandardMaterial3D in Godot.
/// </summary>
[ComponentCategory("Assets/Materials")]
public class PBS_Metallic : MaterialProvider, ICommonMaterial
{
    // TEXTURE TRANSFORM

    /// <summary>
    /// UV texture scale.
    /// </summary>
    public readonly Sync<float2> TextureScale;

    /// <summary>
    /// UV texture offset.
    /// </summary>
    public readonly Sync<float2> TextureOffset;

    // ALBEDO

    /// <summary>
    /// Base color (albedo) of the material.
    /// </summary>
    public readonly Sync<colorHDR> AlbedoColor;

    /// <summary>
    /// Base color texture (albedo map).
    /// </summary>
    public readonly AssetRef<TextureAsset> AlbedoTexture;

    // METALLIC/ROUGHNESS

    /// <summary>
    /// Metallic value (0 = dielectric, 1 = metal).
    /// </summary>
    public readonly Sync<float> Metallic;

    /// <summary>
    /// Smoothness value (0 = rough, 1 = smooth). Converted to roughness for Godot.
    /// </summary>
    public readonly Sync<float> Smoothness;

    /// <summary>
    /// Metallic/roughness map. R channel = metallic, A channel = smoothness.
    /// </summary>
    public readonly AssetRef<TextureAsset> MetallicMap;

    // NORMAL MAP

    /// <summary>
    /// Normal map texture for surface detail.
    /// </summary>
    public readonly AssetRef<TextureAsset> NormalMap;

    /// <summary>
    /// Normal map strength/scale.
    /// </summary>
    public readonly Sync<float> NormalScale;

    // EMISSION

    /// <summary>
    /// Emissive color (self-illumination).
    /// </summary>
    public readonly Sync<colorHDR> EmissiveColor;

    /// <summary>
    /// Emissive map texture.
    /// </summary>
    public readonly AssetRef<TextureAsset> EmissiveMap;

    // OCCLUSION

    /// <summary>
    /// Ambient occlusion map.
    /// </summary>
    public readonly AssetRef<TextureAsset> OcclusionMap;

    // BLEND SETTINGS

    /// <summary>
    /// Blend mode (Opaque, Cutout, Transparent, Additive).
    /// </summary>
    public readonly Sync<BlendMode> BlendMode;

    /// <summary>
    /// Alpha cutoff threshold (for Cutout blend mode).
    /// </summary>
    public readonly Sync<float> AlphaCutoff;

    /// <summary>
    /// Face culling mode.
    /// </summary>
    public readonly Sync<Culling> Culling;

    /// <summary>
    /// Render queue priority (-1 = default).
    /// </summary>
    public readonly Sync<int> RenderQueue;

    // DETAIL LAYER
    //
    // A second albedo and normal pair sampled at their own tiling, multiplied over the base. It is
    // what puts fabric weave or skin pores on a surface whose main texture cannot afford the
    // resolution, and every import that used one arrived flat without it.
    public readonly Sync<float2> DetailTextureScale;
    public readonly Sync<float2> DetailTextureOffset;
    public readonly AssetRef<TextureAsset> DetailAlbedoTexture;
    public readonly AssetRef<TextureAsset> DetailNormalMap;
    public readonly Sync<float> DetailNormalScale;

    // PARALLAX
    //
    // A height map that offsets the UV by the view angle, so bricks and panel gaps keep their depth
    // when you look across them instead of sliding flat.
    public readonly AssetRef<TextureAsset> HeightMap;
    public readonly Sync<float> HeightScale;

    // DEPTH BIAS
    //
    // Pushes this surface toward or away from the camera in depth only. Decals and layered clothing
    // use it to sit on another surface without z-fighting.
    public readonly Sync<float> OffsetFactor;
    public readonly Sync<float> OffsetUnits;

    protected override MaterialType MaterialType => MaterialType.PBS_Metallic;

    // ICommonMaterial IMPLEMENTATION

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

    public PBS_Metallic()
    {
        // Texture transform
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);

        // Albedo
        AlbedoColor = new Sync<colorHDR>(this, colorHDR.White);
        AlbedoTexture = new AssetRef<TextureAsset>(this);

        // Metallic/Roughness. Smoothness default 0.25 (roughness 0.75); 0.5 made untextured imports
        // noticeably glossier than intended. -xlinka
        Metallic = new Sync<float>(this, 0.0f);
        Smoothness = new Sync<float>(this, 0.25f);
        MetallicMap = new AssetRef<TextureAsset>(this);

        // Normal
        NormalMap = new AssetRef<TextureAsset>(this);
        NormalScale = new Sync<float>(this, 1.0f);

        // Emission
        EmissiveColor = new Sync<colorHDR>(this, colorHDR.Black);
        EmissiveMap = new AssetRef<TextureAsset>(this);

        // Occlusion
        OcclusionMap = new AssetRef<TextureAsset>(this);

        // Blend settings
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Opaque);
        AlphaCutoff = new Sync<float>(this, 0.5f);
        Culling = new Sync<Culling>(this, Assets.Culling.Back);
        RenderQueue = new Sync<int>(this, -1);

        // Detail. Scale 1 and no texture is the same as no detail layer at all.
        DetailTextureScale = new Sync<float2>(this, float2.One);
        DetailTextureOffset = new Sync<float2>(this, float2.Zero);
        DetailAlbedoTexture = new AssetRef<TextureAsset>(this);
        DetailNormalMap = new AssetRef<TextureAsset>(this);
        DetailNormalScale = new Sync<float>(this, 1.0f);

        // Parallax
        HeightMap = new AssetRef<TextureAsset>(this);
        HeightScale = new Sync<float>(this, 0.02f);

        // Depth bias. Zero is no bias.
        OffsetFactor = new Sync<float>(this, 0f);
        OffsetUnits = new Sync<float>(this, 0f);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        // Blend settings
        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        asset.SetFloat("AlphaCutoff", AlphaCutoff.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value);

        // Texture transform
        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);

        // Albedo
        asset.SetColor("AlbedoColor", AlbedoColor.Value);
        asset.SetTexture("AlbedoTexture", AlbedoTexture.Asset);

        // Metallic/Roughness
        asset.SetFloat("Metallic", Metallic.Value);
        asset.SetFloat("Smoothness", Smoothness.Value);
        asset.SetTexture("MetallicMap", MetallicMap.Asset);

        // Normal
        asset.SetTexture("NormalMap", NormalMap.Asset);
        asset.SetFloat("NormalScale", NormalScale.Value);

        // Emission
        asset.SetColor("EmissiveColor", EmissiveColor.Value);
        asset.SetTexture("EmissiveMap", EmissiveMap.Asset);

        // Occlusion
        asset.SetTexture("OcclusionMap", OcclusionMap.Asset);

        // Detail
        asset.SetFloat2("DetailTextureScale", DetailTextureScale.Value);
        asset.SetFloat2("DetailTextureOffset", DetailTextureOffset.Value);
        asset.SetTexture("DetailAlbedoTexture", DetailAlbedoTexture.Asset);
        asset.SetTexture("DetailNormalMap", DetailNormalMap.Asset);
        asset.SetFloat("DetailNormalScale", DetailNormalScale.Value);

        // Parallax
        asset.SetTexture("HeightMap", HeightMap.Asset);
        asset.SetFloat("HeightScale", HeightScale.Value);

        // Depth bias
        asset.SetFloat("OffsetFactor", OffsetFactor.Value);
        asset.SetFloat("OffsetUnits", OffsetUnits.Value);
    }
}
