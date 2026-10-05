// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Shell fur. The mesh draws once as a Blinn-Phong skin and twenty more times as shells pushed out along
// its normals, each cut down to the strands the Noise texture leaves standing, so the stack reads as
// fur. Base's alpha says how long the fur grows where; the forces bend the tips. See Fur_Core.gdshaderinc.
//
// The members carry the names fur materials are stored under, Color and Base included, so an imported
// one binds without aliasing. That is also why this is not an ICommonMaterial: its Color would collide.
// -xlinka
[ComponentCategory("Assets/Materials")]
public class FurMaterial : MaterialProvider
{
    public readonly Sync<colorHDR> Color;

    // BLINN-PHONG. The lobe's power is Shininess * 128 and Gloss scales it, so the default 8 is a pinpoint
    // sheen. Each strand's gloss is scaled again by the noise under it.
    public readonly Sync<colorHDR> SpecularColor;
    [Range(0.01f, 10f, "0.##")]
    public readonly Sync<float> Shininess;
    [Range(0f, 1f, "0.##")]
    public readonly Sync<float> Gloss;

    // A glow toward the silhouette, on the shells only. Black is none.
    public readonly Sync<colorHDR> RimColor;
    public readonly Sync<float> RimPower;

    // FUR. Length is in the mesh's own units, so the fur scales with the object; Hardness multiplies how
    // far the shells actually reach. Thinness tiles the noise, finer as it rises. Shading darkens the
    // inner shells toward the skin, Coloring lets the noise tint strands lighter and darker.
    [Range(0f, 0.25f, "0.###")]
    public readonly Sync<float> FurLength;
    [Range(0f, 5f, "0.##")]
    public readonly Sync<float> FurHardness;
    [Range(0f, 5f, "0.##")]
    public readonly Sync<float> FurThinness;
    [Range(0f, 1f, "0.##")]
    public readonly Sync<float> FurShading;
    [Range(0f, 1f, "0.##")]
    public readonly Sync<float> FurColoring;

    // RGB the colour, ALPHA the length mask: past half the length a shell only grows where alpha reaches
    // its layer.
    public readonly AssetRef<TextureAsset> Base;
    // The skin's normal map. The shells keep the mesh normal.
    public readonly AssetRef<TextureAsset> NormalMap;
    // Red is where strands stand: a texel below AlphaCutoff is a gap. Unbound, every shell is solid.
    public readonly AssetRef<TextureAsset> Noise;
    public readonly Sync<float2> TextureScale;
    public readonly Sync<float2> TextureOffset;
    public readonly Sync<float> AlphaCutoff;

    // Bends the tips, growing with the square of the layer. Global is a world direction (gravity, wind),
    // Local goes through the camera's projection and follows the view. Each component is clamped to -1..1.
    public readonly Sync<float4> ForceGlobal;
    public readonly Sync<float4> ForceLocal;

    // -1 = default
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.Fur;

    public FurMaterial()
    {
        Color = new Sync<colorHDR>(this, colorHDR.White);
        SpecularColor = new Sync<colorHDR>(this, colorHDR.White);
        Shininess = new Sync<float>(this, 8f);
        Gloss = new Sync<float>(this, 1f);
        RimColor = new Sync<colorHDR>(this, colorHDR.Black);
        RimPower = new Sync<float>(this, 4f);

        FurLength = new Sync<float>(this, 0.05f);
        FurHardness = new Sync<float>(this, 1f);
        FurThinness = new Sync<float>(this, 2f);
        FurShading = new Sync<float>(this, 0.25f);
        FurColoring = new Sync<float>(this, 0.1f);

        Base = new AssetRef<TextureAsset>(this);
        NormalMap = new AssetRef<TextureAsset>(this);
        Noise = new AssetRef<TextureAsset>(this);
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);
        AlphaCutoff = new Sync<float>(this, 0.2f);

        ForceGlobal = new Sync<float4>(this, float4.Zero);
        ForceLocal = new Sync<float4>(this, float4.Zero);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetFloat("RenderQueue", RenderQueue.Value);

        asset.SetColor("AlbedoColor", Color.Value);
        asset.SetTexture("AlbedoTexture", Base.Asset);
        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);

        asset.SetColor("SpecularColor", SpecularColor.Value);
        asset.SetFloat("Shininess", Shininess.Value);
        asset.SetFloat("Gloss", Gloss.Value);
        asset.SetColor("RimColor", RimColor.Value);
        asset.SetFloat("RimPower", RimPower.Value);

        asset.SetFloat("FurLength", FurLength.Value);
        asset.SetFloat("FurHardness", FurHardness.Value);
        asset.SetFloat("FurThinness", FurThinness.Value);
        asset.SetFloat("FurShading", FurShading.Value);
        asset.SetFloat("FurColoring", FurColoring.Value);

        asset.SetTexture("NormalMap", NormalMap.Asset);
        asset.SetTexture("NoiseTexture", Noise.Asset);
        asset.SetFloat("AlphaCutoff", AlphaCutoff.Value);

        asset.SetFloat4("ForceGlobal", ForceGlobal.Value);
        asset.SetFloat4("ForceLocal", ForceLocal.Value);
    }

    public override bool PrimaryColorIsTextured => Base.Target != null;

    public override bool TryGetPrimaryColor(out colorHDR color)
    {
        color = Color.Value;
        return true;
    }
}
