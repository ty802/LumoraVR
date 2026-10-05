// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Assets;

// PBS cut away by planes. Everything on the far side of any plane in Slicers is discarded, and the surface
// near the nearest plane blends to EdgeColor and EdgeEmissiveColor across the band from EdgeTransitionStart to
// EdgeTransitionEnd, so a cut reads as a seam. Up to eight planes; HideSlicers turns the cut off without
// losing them, LocalSpace measures them in the mesh's own space instead of the world.
//
// The metallic and specular members differ only in how the surface reflects; see PBS_SliceMetallic and
// PBS_SliceSpecular. Member names are the ones a package stores, so an imported slice keeps its values,
// planes included. -xlinka
public abstract class PBS_Slice : MaterialProvider, ICommonMaterial
{
    public const int MaxSlicers = 8;

    public readonly Sync<float2> TextureScale;
    public readonly Sync<float2> TextureOffset;

    public readonly Sync<colorHDR> AlbedoColor;
    public readonly Sync<colorHDR> EdgeColor;
    public readonly AssetRef<TextureAsset> AlbedoTexture;

    public readonly Sync<colorHDR> EmissiveColor;
    public readonly Sync<colorHDR> EdgeEmissiveColor;
    public readonly AssetRef<TextureAsset> EmissiveMap;

    public readonly AssetRef<TextureAsset> NormalMap;
    public readonly Sync<float> NormalScale;

    public readonly AssetRef<TextureAsset> OcclusionMap;

    public readonly Sync<float2> DetailTextureScale;
    public readonly Sync<float2> DetailTextureOffset;
    public readonly AssetRef<TextureAsset> DetailAlbedoTexture;
    public readonly AssetRef<TextureAsset> DetailNormalMap;
    public readonly Sync<float> DetailNormalScale;

    public readonly Sync<Culling> Culling;

    public readonly Sync<bool> HideSlicers;
    public readonly SyncList<SlicePlane> Slicers;
    public readonly Sync<bool> LocalSpace;
    public readonly Sync<float> EdgeTransitionStart;
    public readonly Sync<float> EdgeTransitionEnd;

    public readonly Sync<AlphaHandling> AlphaHandling;
    public readonly Sync<float> AlphaClip;

    // Kept for what a package carries; the shader has no depth bias to give them.
    public readonly Sync<float> OffsetFactor;
    public readonly Sync<float> OffsetUnits;

    public readonly Sync<int> RenderQueue;

    private static readonly string[] Variants = PBSEffectShaders.Paths("Slice");
    private static readonly string[] SlicerNames = { "Slicer0", "Slicer1", "Slicer2", "Slicer3", "Slicer4", "Slicer5", "Slicer6", "Slicer7" };

    protected override MaterialType MaterialType => MaterialType.PBS_Slice;

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

    protected PBS_Slice()
    {
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);

        AlbedoColor = new Sync<colorHDR>(this, colorHDR.White);
        EdgeColor = new Sync<colorHDR>(this, colorHDR.White);
        AlbedoTexture = new AssetRef<TextureAsset>(this);

        EmissiveColor = new Sync<colorHDR>(this, colorHDR.Black);
        EdgeEmissiveColor = new Sync<colorHDR>(this, colorHDR.Black);
        EmissiveMap = new AssetRef<TextureAsset>(this);

        NormalMap = new AssetRef<TextureAsset>(this);
        NormalScale = new Sync<float>(this, 1f);

        OcclusionMap = new AssetRef<TextureAsset>(this);

        DetailTextureScale = new Sync<float2>(this, float2.One);
        DetailTextureOffset = new Sync<float2>(this, float2.Zero);
        DetailAlbedoTexture = new AssetRef<TextureAsset>(this);
        DetailNormalMap = new AssetRef<TextureAsset>(this);
        DetailNormalScale = new Sync<float>(this, 1f);

        // Both sides: a cut-open shell shows its inside.
        Culling = new Sync<Culling>(this, Assets.Culling.None);

        HideSlicers = new Sync<bool>(this, false);
        Slicers = new SyncList<SlicePlane>();
        LocalSpace = new Sync<bool>(this, false);
        EdgeTransitionStart = new Sync<float>(this, 0f);
        EdgeTransitionEnd = new Sync<float>(this, 0.01f);

        AlphaHandling = new Sync<AlphaHandling>(this, Assets.AlphaHandling.Opaque);
        AlphaClip = new Sync<float>(this, 0f);

        OffsetFactor = new Sync<float>(this, 0f);
        OffsetUnits = new Sync<float>(this, 0f);
        RenderQueue = new Sync<int>(this, -1);

        Slicers.ElementsAdded += OnSlicersAdded;
        Slicers.ElementsRemoving += OnSlicersRemoving;
        Slicers.ElementsRemoved += OnSlicersRemoved;
    }

    // A list does not raise a change pass on the component that holds it, so the planes report here. Through
    // the same once-a-frame change pass as the material's own fields: a driven plane moves every frame.
    internal void SlicersChanged() => NotifyChanged();

    private void OnSlicersAdded(SyncElementList<SlicePlane> list, int index, int count)
    {
        for (int i = index; i < index + count && i < list.Count; i++)
        {
            var plane = list[i];
            if (plane != null)
                plane.Owner = this;
        }
        SlicersChanged();
    }

    private void OnSlicersRemoving(SyncElementList<SlicePlane> list, int index, int count)
    {
        for (int i = index; i < index + count && i < list.Count; i++)
        {
            var plane = list[i];
            if (plane != null)
                plane.Owner = null;
        }
    }

    private void OnSlicersRemoved(SyncElementList<SlicePlane> list, int index, int count) => SlicersChanged();

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
        asset.SetColor("EdgeColor", EdgeColor.Value);
        asset.SetTexture("AlbedoTexture", AlbedoTexture.Asset);

        asset.SetColor("EmissiveColor", EmissiveColor.Value);
        asset.SetColor("EdgeEmissiveColor", EdgeEmissiveColor.Value);
        asset.SetTexture("EmissiveMap", EmissiveMap.Asset);

        asset.SetTexture("NormalMap", NormalMap.Asset);
        asset.SetFloat("NormalScale", NormalScale.Value);

        asset.SetTexture("OcclusionMap", OcclusionMap.Asset);

        asset.SetFloat2("DetailTextureScale", DetailTextureScale.Value);
        asset.SetFloat2("DetailTextureOffset", DetailTextureOffset.Value);
        asset.SetTexture("DetailAlbedoTexture", DetailAlbedoTexture.Asset);
        asset.SetTexture("DetailNormalMap", DetailNormalMap.Asset);
        asset.SetFloat("DetailNormalScale", DetailNormalScale.Value);

        asset.SetBool("LocalSpace", LocalSpace.Value);
        asset.SetFloat("EdgeTransitionStart", EdgeTransitionStart.Value);
        asset.SetFloat("EdgeTransitionEnd", EdgeTransitionEnd.Value);

        // Unused slots go to zero, which the shader reads as the end of the list.
        int count = HideSlicers.Value ? 0 : System.Math.Min(Slicers.Count, MaxSlicers);
        for (int i = 0; i < MaxSlicers; i++)
        {
            var plane = i < count ? Slicers[i] : null;
            asset.SetFloat4(SlicerNames[i], plane != null ? plane.Plane : float4.Zero);
        }

        UpdateWorkflow(asset);
    }

    protected abstract void UpdateWorkflow(MaterialAsset asset);
}
