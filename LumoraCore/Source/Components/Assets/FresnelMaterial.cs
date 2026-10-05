// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Fresnel: the colour swings with the viewing angle. Tinted visors, rim glows, debug rig visualisation.
//
// NEAR AND FAR ARE ABOUT THE NORMAL, NOT DISTANCE. Head on, the surface reads as NearColor; toward the
// silhouette it swings to FarColor. The defaults are white near and BLACK far, so a fresh one is a
// white surface darkening off to its edges.
//
// This used to declare itself MaterialType.PBS_Metallic purely to reach a StandardMaterial3D, and
// approximated the whole effect with an albedo and an emission colour. It had no textures, no mask and
// no gamma curve, so a package that authored any of those had them dropped on the floor with nothing
// logged. It also meant the material broke outright the moment PBS_Metallic grew a shader of its
// own. -xlinka
[ComponentCategory("Assets/Materials")]
public class FresnelMaterial : MaterialProvider
{
    public readonly Sync<float> Exponent;

    // Applied to the fresnel term AFTER the exponent, which is what lets an author bend the falloff
    // without moving where the rim sits.
    public readonly Sync<float> GammaCurve;

    public readonly Sync<colorHDR> NearColor;
    public readonly Sync<colorHDR> FarColor;
    public readonly Sync<bool> UseVertexColor;

    public readonly AssetRef<TextureAsset> NearTexture;
    public readonly Sync<float2> NearTextureScale;
    public readonly Sync<float2> NearTextureOffset;

    public readonly AssetRef<TextureAsset> FarTexture;
    public readonly Sync<float2> FarTextureScale;
    public readonly Sync<float2> FarTextureOffset;

    public readonly AssetRef<TextureAsset> NormalMap;
    public readonly Sync<float> NormalScale;

    // Folds the mask's RGB to one factor and multiplies by its alpha, then either scales this
    // material's alpha by it or clips against the cutoff.
    public readonly AssetRef<TextureAsset> MaskTexture;
    public readonly Sync<float2> MaskScale;
    public readonly Sync<float2> MaskOffset;
    public readonly Sync<MaskTextureMode> MaskMode;

    public readonly Sync<float> AlphaCutoff;
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;

    // How vertex colours blend across a triangle: Linear as always, or sRGB for gradients that step
    // evenly to the eye.
    public readonly Sync<ColorProfile> VertexColorInterpolationSpace;

    // Auto keeps what the blend mode implies. ZTest picks when the surface draws against what is already
    // there: Greater and GreaterOrEqual draw it only where it is hidden, Always through everything, Never
    // not at all. Equal and NotEqual have no counterpart here and test normally.
    public readonly Sync<ZWrite> ZWrite;
    public readonly Sync<ZTest> ZTest;

    // DEPTH BIAS: toward the camera (negative) or away from it, in depth only.
    public readonly Sync<float> OffsetFactor;
    public readonly Sync<float> OffsetUnits;

    // -1 = default
    public readonly Sync<int> RenderQueue;

    // Wraps both colour textures around the centre of the UV square, angle across U and radius along V.
    // The power bends the radius.
    public readonly Sync<bool> PolarUVmapping;
    public readonly Sync<float> PolarPower;

    // Whether the last update put a generated shader on in place of the shipped file.
    private bool _generatedVariant;

    protected override MaterialType MaterialType => MaterialType.Fresnel;

    public FresnelMaterial()
    {
        Exponent = new Sync<float>(this, 1f);
        GammaCurve = new Sync<float>(this, 1f);

        NearColor = new Sync<colorHDR>(this, colorHDR.White);
        FarColor = new Sync<colorHDR>(this, colorHDR.Black);
        UseVertexColor = new Sync<bool>(this, false);

        NearTexture = new AssetRef<TextureAsset>(this);
        NearTextureScale = new Sync<float2>(this, float2.One);
        NearTextureOffset = new Sync<float2>(this, float2.Zero);

        FarTexture = new AssetRef<TextureAsset>(this);
        FarTextureScale = new Sync<float2>(this, float2.One);
        FarTextureOffset = new Sync<float2>(this, float2.Zero);

        NormalMap = new AssetRef<TextureAsset>(this);
        NormalScale = new Sync<float>(this, 1f);

        MaskTexture = new AssetRef<TextureAsset>(this);
        MaskScale = new Sync<float2>(this, float2.One);
        MaskOffset = new Sync<float2>(this, float2.Zero);
        MaskMode = new Sync<MaskTextureMode>(this, MaskTextureMode.MultiplyAlpha);

        AlphaCutoff = new Sync<float>(this, 0.5f);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Opaque);
        Culling = new Sync<Culling>(this, Assets.Culling.Back);

        // Every addition below starts off or neutral, which leaves existing fresnel content as it was.
        VertexColorInterpolationSpace = new Sync<ColorProfile>(this, ColorProfile.Linear);
        ZWrite = new Sync<ZWrite>(this, Assets.ZWrite.Auto);
        ZTest = new Sync<ZTest>(this, Assets.ZTest.LessOrEqual);
        OffsetFactor = new Sync<float>(this, 0f);
        OffsetUnits = new Sync<float>(this, 0f);
        RenderQueue = new Sync<int>(this, -1);
        PolarUVmapping = new Sync<bool>(this, false);
        PolarPower = new Sync<float>(this, 1f);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        bool depthOffset = OffsetFactor.Value != 0f || OffsetUnits.Value != 0f;
        _generatedVariant = MaterialVariantSource.Apply(asset,
            MaterialVariantSource.Fresnel(BlendMode.Value, Culling.Value, ZWrite.Value, ZTest.Value, depthOffset),
            MaterialVariantSource.FresnelFile(BlendMode.Value, Culling.Value), _generatedVariant);
        asset.SetFloat("RenderQueue", RenderQueue.Value);

        asset.SetFloat("Exponent", Exponent.Value);
        asset.SetFloat("GammaCurve", GammaCurve.Value);

        asset.SetColor("NearColor", NearColor.Value);
        asset.SetColor("FarColor", FarColor.Value);
        asset.SetBool("UseVertexColor", UseVertexColor.Value);

        asset.SetTexture("NearTexture", NearTexture.Target?.Asset!);
        asset.SetFloat2("NearTextureScale", NearTextureScale.Value);
        asset.SetFloat2("NearTextureOffset", NearTextureOffset.Value);

        asset.SetTexture("FarTexture", FarTexture.Target?.Asset!);
        asset.SetFloat2("FarTextureScale", FarTextureScale.Value);
        asset.SetFloat2("FarTextureOffset", FarTextureOffset.Value);

        asset.SetTexture("NormalMap", NormalMap.Target?.Asset!);
        asset.SetFloat("NormalScale", NormalScale.Value);

        asset.SetTexture("MaskTexture", MaskTexture.Target?.Asset!);
        asset.SetFloat2("MaskScale", MaskScale.Value);
        asset.SetFloat2("MaskOffset", MaskOffset.Value);
        asset.SetInt("MaskMode", (int)MaskMode.Value);

        asset.SetFloat("AlphaCutoff", AlphaCutoff.Value);

        asset.SetInt("VertexColorInterpolationSpace", (int)VertexColorInterpolationSpace.Value);
        asset.SetBool("PolarMapping", PolarUVmapping.Value);
        asset.SetFloat("PolarPower", PolarPower.Value);
        asset.SetFloat("OffsetFactor", OffsetFactor.Value);
        asset.SetFloat("OffsetUnits", OffsetUnits.Value);
    }

    // NearColor is what the surface reads as head on, so it is the one it looks like.
    public override bool TryGetPrimaryColor(out colorHDR color)
    {
        color = NearColor.Value;
        return true;
    }
}
