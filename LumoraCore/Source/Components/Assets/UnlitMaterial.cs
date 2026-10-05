// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Assets;

[ComponentCategory("Assets/Materials")]
public class UnlitMaterial : MaterialProvider, ICommonMaterial
{
    // TEXTURE TRANSFORM

    public readonly Sync<float2> TextureScale;

    public readonly Sync<float2> TextureOffset;

    // STEREO TEXTURE TRANSFORM

    // A stereo frame is one picture with an eye in each half, so each eye needs its own window into it.
    // While this is on the per-eye pair REPLACES TextureScale/TextureOffset instead of stacking with it,
    // so there is one transform in play rather than two that quietly multiply. On a flat screen the view
    // index is always 0, which means a desktop user sees the left eye. -xlinka
    public readonly Sync<bool> StereoTextureTransform;

    public readonly Sync<float2> LeftEyeTextureScale;

    public readonly Sync<float2> LeftEyeTextureOffset;

    public readonly Sync<float2> RightEyeTextureScale;

    public readonly Sync<float2> RightEyeTextureOffset;

    // COLOR AND TEXTURE

    public readonly Sync<colorHDR> TintColor;

    public readonly AssetRef<TextureAsset> Texture;

    public readonly Sync<bool> UseVertexColor;

    // BLEND SETTINGS

    public readonly Sync<BlendMode> BlendMode;

    // used by Cutout blend mode
    public readonly Sync<float> AlphaCutoff;

    public readonly Sync<Culling> Culling;

    // -1 = default
    public readonly Sync<int> RenderQueue;

    // MASK
    //
    // Folds the mask's RGB to one factor times its alpha, then scales this surface's alpha by it or cuts
    // away wherever it does not clear AlphaCutoff. Read at the mesh UV under its own tiling.
    public readonly AssetRef<TextureAsset> MaskTexture;
    public readonly Sync<float2> MaskScale;
    public readonly Sync<float2> MaskOffset;
    public readonly Sync<MaskTextureMode> MaskMode;

    // How vertex colours blend across a triangle: Linear as always, or sRGB for gradients that step
    // evenly to the eye.
    public readonly Sync<ColorProfile> VertexColorInterpolationSpace;

    // Auto keeps what the blend mode implies: the plain unlit lays depth down where it is opaque enough,
    // the additive one never does.
    public readonly Sync<ZWrite> ZWrite;

    // OFFSET TEXTURE
    //
    // Shifts where the main texture is read by this texture's red and green times the magnitude: heat
    // haze, flowing water, wobble.
    public readonly AssetRef<TextureAsset> OffsetTexture;
    public readonly Sync<float2> OffsetMagnitude;
    public readonly Sync<float2> OffsetTextureScale;
    public readonly Sync<float2> OffsetTextureOffset;

    // POLAR MAPPING
    //
    // Wraps the texture around the centre of the UV square, angle across U and radius along V. The power
    // bends the radius.
    public readonly Sync<bool> PolarUVmapping;
    public readonly Sync<float> PolarPower;

    // Shows a normal map as the colour it encodes rather than its raw texels.
    public readonly Sync<bool> DecodeAsNormalMap;

    // BILLBOARDS
    //
    // On a point mesh, every point grows into a quad facing the camera, BillboardSize half wide and half
    // tall before the object's scale. Per billboard, a point can scale that size, roll the quad and pick
    // its own atlas cell. Only a point mesh has anything to grow; triangles draw nothing while this is on.
    public readonly Sync<bool> UseBillboardGeometry;
    public readonly Sync<bool> UsePerBillboardScale;
    public readonly Sync<bool> UsePerBillboardRotation;
    public readonly Sync<bool> UsePerBillboardUV;
    public readonly Sync<float2> BillboardSize;

    // DEPTH BIAS
    //
    // Pushes the surface toward (negative) or away from the camera in depth only, so a decal sits on what
    // it decorates without fighting it.
    public readonly Sync<float> OffsetFactor;
    public readonly Sync<float> OffsetUnits;

    // Whether the last update put a generated shader on in place of the shipped file.
    private bool _generatedVariant;

    protected override MaterialType MaterialType => MaterialType.Unlit;

    // ICommonMaterial IMPLEMENTATION

    public colorHDR Color
    {
        get => TintColor.Value;
        set => TintColor.Value = value;
    }

    public IAssetProvider<TextureAsset> MainTexture
    {
        get => Texture.Target;
        set => Texture.Target = value;
    }

    public UnlitMaterial()
    {
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);

        StereoTextureTransform = new Sync<bool>(this, false);
        LeftEyeTextureScale = new Sync<float2>(this, float2.One);
        LeftEyeTextureOffset = new Sync<float2>(this, float2.Zero);
        RightEyeTextureScale = new Sync<float2>(this, float2.One);
        RightEyeTextureOffset = new Sync<float2>(this, float2.Zero);

        TintColor = new Sync<colorHDR>(this, colorHDR.White);
        Texture = new AssetRef<TextureAsset>(this);
        UseVertexColor = new Sync<bool>(this, false);

        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Opaque);
        AlphaCutoff = new Sync<float>(this, 0.5f);
        Culling = new Sync<Culling>(this, Assets.Culling.Back);
        RenderQueue = new Sync<int>(this, -1);

        // Every addition below starts off or neutral, which leaves existing unlit content as it was.
        MaskTexture = new AssetRef<TextureAsset>(this);
        MaskScale = new Sync<float2>(this, float2.One);
        MaskOffset = new Sync<float2>(this, float2.Zero);
        MaskMode = new Sync<MaskTextureMode>(this, MaskTextureMode.MultiplyAlpha);
        VertexColorInterpolationSpace = new Sync<ColorProfile>(this, ColorProfile.Linear);
        ZWrite = new Sync<ZWrite>(this, Assets.ZWrite.Auto);
        OffsetTexture = new AssetRef<TextureAsset>(this);
        OffsetMagnitude = new Sync<float2>(this, float2.Zero);
        OffsetTextureScale = new Sync<float2>(this, float2.One);
        OffsetTextureOffset = new Sync<float2>(this, float2.Zero);
        PolarUVmapping = new Sync<bool>(this, false);
        PolarPower = new Sync<float>(this, 1f);
        DecodeAsNormalMap = new Sync<bool>(this, false);
        UseBillboardGeometry = new Sync<bool>(this, false);
        UsePerBillboardScale = new Sync<bool>(this, false);
        UsePerBillboardRotation = new Sync<bool>(this, false);
        UsePerBillboardUV = new Sync<bool>(this, false);
        BillboardSize = new Sync<float2>(this, float2.One * 0.005f);
        OffsetFactor = new Sync<float>(this, 0f);
        OffsetUnits = new Sync<float>(this, 0f);
        TextureUV = new Sync<TextureUVChannel>(this, TextureUVChannel.UV1);
    }

    public readonly Sync<TextureUVChannel> TextureUV;

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        var textureAsset = Texture.Asset;
        LumoraLogger.Debug($"UnlitMaterial.UpdateMaterial: Texture.Target={Texture.Target?.GetType().Name}, Texture.Asset={textureAsset?.GetType().Name}, HasHook={textureAsset?.Hook != null}");

        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        bool depthOffset = OffsetFactor.Value != 0f || OffsetUnits.Value != 0f;
        _generatedVariant = MaterialVariantSource.Apply(asset,
            MaterialVariantSource.Unlit(BlendMode.Value, ZWrite.Value, depthOffset, UseBillboardGeometry.Value),
            MaterialVariantSource.UnlitFile(BlendMode.Value), _generatedVariant);
        asset.SetFloat("AlphaCutoff", AlphaCutoff.Value);
        asset.SetBool("AlphaClip", BlendMode.Value == Assets.BlendMode.Cutout);
        asset.SetBool("UseVertexColor", UseVertexColor.Value);
        asset.SetInt("texture_uv", (int)TextureUV.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value);

        asset.SetFloat2("TextureScale", TextureScale.Value);
        asset.SetFloat2("TextureOffset", TextureOffset.Value);

        asset.SetBool("StereoTextureTransform", StereoTextureTransform.Value);
        asset.SetFloat2("LeftEyeTextureScale", LeftEyeTextureScale.Value);
        asset.SetFloat2("LeftEyeTextureOffset", LeftEyeTextureOffset.Value);
        asset.SetFloat2("RightEyeTextureScale", RightEyeTextureScale.Value);
        asset.SetFloat2("RightEyeTextureOffset", RightEyeTextureOffset.Value);

        asset.SetColor("TintColor", TintColor.Value);
        asset.SetTexture("Texture", textureAsset!);

        asset.SetTexture("MaskTexture", MaskTexture.Asset);
        asset.SetFloat2("MaskScale", MaskScale.Value);
        asset.SetFloat2("MaskOffset", MaskOffset.Value);
        asset.SetInt("MaskMode", (int)MaskMode.Value);
        asset.SetInt("VertexColorInterpolationSpace", (int)VertexColorInterpolationSpace.Value);

        asset.SetTexture("OffsetTexture", OffsetTexture.Asset);
        asset.SetFloat2("OffsetMagnitude", OffsetMagnitude.Value);
        asset.SetFloat2("OffsetTextureScale", OffsetTextureScale.Value);
        asset.SetFloat2("OffsetTextureOffset", OffsetTextureOffset.Value);

        asset.SetBool("PolarMapping", PolarUVmapping.Value);
        asset.SetFloat("PolarPower", PolarPower.Value);
        asset.SetBool("DecodeAsNormalMap", DecodeAsNormalMap.Value);

        asset.SetBool("UsePerBillboardScale", UsePerBillboardScale.Value);
        asset.SetBool("UsePerBillboardRotation", UsePerBillboardRotation.Value);
        asset.SetBool("UsePerBillboardUv", UsePerBillboardUV.Value);
        asset.SetFloat2("BillboardSize", BillboardSize.Value);

        asset.SetFloat("OffsetFactor", OffsetFactor.Value);
        asset.SetFloat("OffsetUnits", OffsetUnits.Value);
    }
}

