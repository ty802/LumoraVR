// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// PBS moved by maps, each with its own scale, offset, magnitude and bias. VertexDisplaceMap pushes vertices
// along their normals by its red channel. UVDisplaceMap shifts where every texture of the surface is read by
// its red and green. WorldspaceVertexOffsetMap shifts where the vertex map is read, from the world position
// of the object's origin, or of each vertex with WorldspaceOffsetPerVertex, so copies of one mesh ripple
// differently depending on where they stand. A bias with no map still offsets.
//
// The metallic and specular members differ only in how the surface reflects; see PBS_DisplaceMetallic and
// PBS_DisplaceSpecular. Member names are the ones a package stores, so an imported one keeps its values.
// -xlinka
public abstract class PBS_DisplaceMaterial : MaterialProvider, ICommonMaterial
{
    public readonly Sync<float2> TextureScale;
    public readonly Sync<float2> TextureOffset;

    public readonly Sync<colorHDR> AlbedoColor;
    public readonly AssetRef<TextureAsset> AlbedoTexture;

    public readonly Sync<colorHDR> EmissiveColor;
    public readonly AssetRef<TextureAsset> EmissiveMap;

    public readonly AssetRef<TextureAsset> NormalMap;
    public readonly Sync<float> NormalScale;

    public readonly AssetRef<TextureAsset> OcclusionMap;

    public readonly AssetRef<TextureAsset> VertexDisplaceMap;
    public readonly Sync<float> VertexDisplaceMagnitude;
    public readonly Sync<float> VertexDisplaceBias;
    public readonly Sync<float2> VertexDisplaceMapScale;
    public readonly Sync<float2> VertexDisplaceMapOffset;

    public readonly AssetRef<TextureAsset> UVDisplaceMap;
    public readonly Sync<float> UVDisplaceMagnitude;
    public readonly Sync<float> UVDisplaceBias;
    public readonly Sync<float2> UVDisplaceMapScale;
    public readonly Sync<float2> UVDisplaceMapOffset;

    public readonly AssetRef<TextureAsset> WorldspaceVertexOffsetMap;
    public readonly Sync<float2> WorldspaceOffsetMagnitude;
    public readonly Sync<float2> WorldspaceVertexOffsetMapScale;
    public readonly Sync<float2> WorldspaceVertexOffsetMapOffset;
    public readonly Sync<bool> WorldspaceOffsetPerVertex;

    public readonly Sync<Culling> Culling;
    public readonly Sync<AlphaHandling> AlphaHandling;
    public readonly Sync<float> AlphaClip;

    // Kept for what a package carries; the shader has no depth bias to give them.
    public readonly Sync<float> OffsetFactor;
    public readonly Sync<float> OffsetUnits;

    public readonly Sync<int> RenderQueue;

    private static readonly string[] Variants = PBSEffectShaders.Paths("Displace");

    protected override MaterialType MaterialType => MaterialType.PBS_Displace;

    protected abstract bool Specular { get; }

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

    protected PBS_DisplaceMaterial()
    {
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);

        AlbedoColor = new Sync<colorHDR>(this, colorHDR.White);
        AlbedoTexture = new AssetRef<TextureAsset>(this);

        EmissiveColor = new Sync<colorHDR>(this, colorHDR.Black);
        EmissiveMap = new AssetRef<TextureAsset>(this);

        NormalMap = new AssetRef<TextureAsset>(this);
        NormalScale = new Sync<float>(this, 1f);

        OcclusionMap = new AssetRef<TextureAsset>(this);

        VertexDisplaceMap = new AssetRef<TextureAsset>(this);
        VertexDisplaceMagnitude = new Sync<float>(this, 1f);
        VertexDisplaceBias = new Sync<float>(this, 0f);
        VertexDisplaceMapScale = new Sync<float2>(this, float2.One);
        VertexDisplaceMapOffset = new Sync<float2>(this, float2.Zero);

        UVDisplaceMap = new AssetRef<TextureAsset>(this);
        UVDisplaceMagnitude = new Sync<float>(this, 1f);
        UVDisplaceBias = new Sync<float>(this, 0f);
        UVDisplaceMapScale = new Sync<float2>(this, float2.One);
        UVDisplaceMapOffset = new Sync<float2>(this, float2.Zero);

        WorldspaceVertexOffsetMap = new AssetRef<TextureAsset>(this);
        WorldspaceOffsetMagnitude = new Sync<float2>(this, float2.One);
        WorldspaceVertexOffsetMapScale = new Sync<float2>(this, float2.One);
        WorldspaceVertexOffsetMapOffset = new Sync<float2>(this, float2.Zero);
        WorldspaceOffsetPerVertex = new Sync<bool>(this, false);

        Culling = new Sync<Culling>(this, Assets.Culling.Back);
        AlphaHandling = new Sync<AlphaHandling>(this, Assets.AlphaHandling.Opaque);
        AlphaClip = new Sync<float>(this, 0f);

        OffsetFactor = new Sync<float>(this, 0f);
        OffsetUnits = new Sync<float>(this, 0f);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        var blend = AlphaHandling.Value switch
        {
            Assets.AlphaHandling.AlphaClip => BlendMode.Cutout,
            Assets.AlphaHandling.AlphaBlend => BlendMode.Transparent,
            _ => BlendMode.Opaque
        };
        asset.SetCustomShader(PBSEffectShaders.Pick(Variants, Specular, blend == BlendMode.Transparent, Culling.Value));
        asset.SetBlendMode(blend);
        asset.SetCulling(Culling.Value);
        asset.SetFloat("AlphaClip", AlphaClip.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value);

        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);

        asset.SetColor("AlbedoColor", AlbedoColor.Value);
        asset.SetTexture("AlbedoTexture", AlbedoTexture.Asset);

        asset.SetColor("EmissiveColor", EmissiveColor.Value);
        asset.SetTexture("EmissiveMap", EmissiveMap.Asset);

        asset.SetTexture("NormalMap", NormalMap.Asset);
        asset.SetFloat("NormalScale", NormalScale.Value);

        asset.SetTexture("OcclusionMap", OcclusionMap.Asset);

        asset.SetTexture("VertexDisplaceMap", VertexDisplaceMap.Asset);
        asset.SetFloat("VertexDisplaceMagnitude", VertexDisplaceMagnitude.Value);
        asset.SetFloat("VertexDisplaceBias", VertexDisplaceBias.Value);
        asset.SetFloat2("VertexDisplaceMapScale", VertexDisplaceMapScale.Value);
        asset.SetFloat2("VertexDisplaceMapOffset", VertexDisplaceMapOffset.Value);

        // "Uv" rather than the member's "UV": the property name becomes the uniform by snake case, and two
        // capitals would split into u_v.
        asset.SetTexture("UvDisplaceMap", UVDisplaceMap.Asset);
        asset.SetFloat("UvDisplaceMagnitude", UVDisplaceMagnitude.Value);
        asset.SetFloat("UvDisplaceBias", UVDisplaceBias.Value);
        asset.SetFloat2("UvDisplaceMapScale", UVDisplaceMapScale.Value);
        asset.SetFloat2("UvDisplaceMapOffset", UVDisplaceMapOffset.Value);

        asset.SetTexture("WorldspaceVertexOffsetMap", WorldspaceVertexOffsetMap.Asset);
        asset.SetFloat2("WorldspaceOffsetMagnitude", WorldspaceOffsetMagnitude.Value);
        asset.SetFloat2("WorldspaceVertexOffsetMapScale", WorldspaceVertexOffsetMapScale.Value);
        asset.SetFloat2("WorldspaceVertexOffsetMapOffset", WorldspaceVertexOffsetMapOffset.Value);
        asset.SetBool("WorldspaceOffsetPerVertex", WorldspaceOffsetPerVertex.Value);

        UpdateWorkflow(asset);
    }

    protected abstract void UpdateWorkflow(MaterialAsset asset);
}
