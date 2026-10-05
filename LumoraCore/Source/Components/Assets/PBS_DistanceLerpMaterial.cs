// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Assets;

// PBS pushed out and lit by how close it is to the Points. Each point adds a displacement that runs from
// DisplaceMagnitudeFrom at DisplaceFrom metres to DisplaceMagnitudeTo at DisplaceTo, and an emission from
// EmissionColorFrom to EmissionColorTo over EmissionFrom to EmissionTo, tinted by the point. A non-zero
// GridSize measures from cell centres instead, so the surface moves in blocks. Up to sixteen points.
//
// OverrideDisplacementDirection pushes along that direction, in the mesh's own space, instead of the vertex
// normal. A package stores it as an optional vector; here zero stands for unset, the only direction that
// could not mean anything. -xlinka
public abstract class PBS_DistanceLerpMaterial : MaterialProvider, ICommonMaterial
{
    public const int MaxPoints = 16;

    public readonly Sync<float2> TextureScale;
    public readonly Sync<float2> TextureOffset;

    public readonly Sync<colorHDR> AlbedoColor;
    public readonly AssetRef<TextureAsset> AlbedoTexture;

    public readonly Sync<colorHDR> EmissiveColor;
    public readonly AssetRef<TextureAsset> EmissiveMap;

    public readonly AssetRef<TextureAsset> NormalMap;
    public readonly Sync<float> NormalScale;

    public readonly AssetRef<TextureAsset> OcclusionMap;

    public readonly Sync<float3> GridSize;
    public readonly Sync<float3> GridOffset;

    public readonly Sync<float> DisplaceFrom;
    public readonly Sync<float> DisplaceTo;
    public readonly Sync<float> DisplaceMagnitudeFrom;
    public readonly Sync<float> DisplaceMagnitudeTo;

    public readonly Sync<float> EmissionFrom;
    public readonly Sync<float> EmissionTo;
    public readonly Sync<colorHDR> EmissionColorFrom;
    public readonly Sync<colorHDR> EmissionColorTo;

    public readonly Sync<float3> OverrideDisplacementDirection;

    public readonly Sync<bool> LocalSpace;
    public readonly SyncList<DistanceLerpPoint> Points;

    public readonly Sync<Culling> Culling;
    public readonly Sync<bool> Transparent;

    // Kept for what a package carries; the shader has no depth bias to give them.
    public readonly Sync<float> OffsetFactor;
    public readonly Sync<float> OffsetUnits;

    public readonly Sync<int> RenderQueue;

    private static readonly string[] Variants = PBSEffectShaders.Paths("DistanceLerp");
    private static readonly string[] PointNames = Names("Point");
    private static readonly string[] TintNames = Names("PointTint");

    protected override MaterialType MaterialType => MaterialType.PBS_DistanceLerp;

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

    protected PBS_DistanceLerpMaterial()
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

        GridSize = new Sync<float3>(this, float3.Zero);
        GridOffset = new Sync<float3>(this, float3.Zero);

        DisplaceFrom = new Sync<float>(this, 1f);
        DisplaceTo = new Sync<float>(this, 0f);
        DisplaceMagnitudeFrom = new Sync<float>(this, 0f);
        DisplaceMagnitudeTo = new Sync<float>(this, 0.25f);

        EmissionFrom = new Sync<float>(this, 1f);
        EmissionTo = new Sync<float>(this, 0f);
        EmissionColorFrom = new Sync<colorHDR>(this, colorHDR.Transparent);
        EmissionColorTo = new Sync<colorHDR>(this, new colorHDR(1.5f, 1.5f, 1.5f, 0f));

        OverrideDisplacementDirection = new Sync<float3>(this, float3.Zero);

        LocalSpace = new Sync<bool>(this, false);
        Points = new SyncList<DistanceLerpPoint>();

        Culling = new Sync<Culling>(this, Assets.Culling.Back);
        Transparent = new Sync<bool>(this, false);

        OffsetFactor = new Sync<float>(this, 0f);
        OffsetUnits = new Sync<float>(this, 0f);
        RenderQueue = new Sync<int>(this, -1);

        Points.ElementsAdded += OnPointsAdded;
        Points.ElementsRemoving += OnPointsRemoving;
        Points.ElementsRemoved += OnPointsRemoved;
    }

    private static string[] Names(string prefix)
    {
        var names = new string[MaxPoints];
        for (int i = 0; i < MaxPoints; i++)
            names[i] = prefix + i;
        return names;
    }

    // A list does not raise a change pass on the component that holds it, so the points report here. Through
    // the same once-a-frame change pass as the material's own fields: a driven point moves every frame.
    internal void PointsChanged() => NotifyChanged();

    private void OnPointsAdded(SyncElementList<DistanceLerpPoint> list, int index, int count)
    {
        for (int i = index; i < index + count && i < list.Count; i++)
        {
            var point = list[i];
            if (point != null)
                point.Owner = this;
        }
        PointsChanged();
    }

    private void OnPointsRemoving(SyncElementList<DistanceLerpPoint> list, int index, int count)
    {
        for (int i = index; i < index + count && i < list.Count; i++)
        {
            var point = list[i];
            if (point != null)
                point.Owner = null;
        }
    }

    private void OnPointsRemoved(SyncElementList<DistanceLerpPoint> list, int index, int count) => PointsChanged();

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetCustomShader(PBSEffectShaders.Pick(Variants, Specular, Transparent.Value, Culling.Value));
        asset.SetBlendMode(Transparent.Value ? BlendMode.Transparent : BlendMode.Opaque);
        asset.SetCulling(Culling.Value);
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

        asset.SetFloat3("GridSize", GridSize.Value);
        asset.SetFloat3("GridOffset", GridOffset.Value);

        asset.SetFloat("DisplaceFrom", DisplaceFrom.Value);
        asset.SetFloat("DisplaceTo", DisplaceTo.Value);
        asset.SetFloat("DisplaceMagnitudeFrom", DisplaceMagnitudeFrom.Value);
        asset.SetFloat("DisplaceMagnitudeTo", DisplaceMagnitudeTo.Value);

        asset.SetFloat("EmissionFrom", EmissionFrom.Value);
        asset.SetFloat("EmissionTo", EmissionTo.Value);
        asset.SetColor("EmissionColorFrom", EmissionColorFrom.Value);
        asset.SetColor("EmissionColorTo", EmissionColorTo.Value);

        asset.SetFloat3("OverrideDisplacementDirection", OverrideDisplacementDirection.Value);
        asset.SetBool("LocalSpace", LocalSpace.Value);

        int count = System.Math.Min(Points.Count, MaxPoints);
        asset.SetInt("PointCount", count);
        for (int i = 0; i < MaxPoints; i++)
        {
            var point = i < count ? Points[i] : null;
            asset.SetFloat3(PointNames[i], point != null ? point.Position.Value : float3.Zero);
            asset.SetColor(TintNames[i], point != null ? point.Tint.Value : colorHDR.Black);
        }

        UpdateWorkflow(asset);
    }

    protected abstract void UpdateWorkflow(MaterialAsset asset);
}
