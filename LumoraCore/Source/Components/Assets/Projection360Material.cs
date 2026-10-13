// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Where the lookup direction comes from. View and WorldspaceView aim from the surface at the eye, in
// object and world space; Normal uses the surface normal; Perspective lays a pinhole camera across
// the mesh UVs.
public enum Projection360Mode
{
    View,
    WorldspaceView,
    Normal,
    Perspective
}

public enum Projection360TintMode
{
    Direct,
    Lerp
}

// What a panorama narrower than the full sphere shows past its edge.
public enum Projection360Outside
{
    Clip,
    Color,
    Clamp
}

// A panorama or a cubemap wrapped around the viewer: 360 photos, video domes, a sky on a sphere. The
// picture is looked up by DIRECTION, not by the mesh's UVs, so any mesh will do and a sphere seen from
// inside is the usual one. A panorama narrower than 360 by 180 sits inside FieldOfView, turned by
// AngleOffset, and OutsideMode decides what shows past its edge.
//
// A cubemap, when one is set, wins over the panorama. TextureLerp blends toward the secondary picture
// or cubemap. Exposure and Gamma apply to the picture before Tint; MaxIntensity, when set, caps the
// brightest channel so an HDR panorama cannot blow out.
//
// Unlit. The picture is the light. On a Skybox it is the world's sky. -xlinka
[ComponentCategory("Assets/Materials")]
public class Projection360Material : MaterialProvider, ISkyboxMaterial
{
    [Group("Picture")]
    public readonly AssetRef<TextureAsset> Texture;
    public readonly AssetRef<TextureAsset> SecondaryTexture;
    public readonly AssetRef<CubemapAsset> Cubemap;
    public readonly AssetRef<CubemapAsset> SecondaryCubemap;
    // Unset samples the cubemap's mips by screen footprint; set pins one level.
    public readonly Sync<float?> CubemapLOD;
    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> TextureLerp;
    public readonly Sync<float2> TextureScale;
    public readonly Sync<float2> TextureOffset;

    // A stereo panorama: the right eye reads its own window of the same picture.
    public readonly Sync<bool> StereoTextureTransform;
    public readonly Sync<float2> RightEyeTextureScale;
    public readonly Sync<float2> RightEyeTextureOffset;

    // Angles in degrees.
    [Group("Projection")]
    public readonly Sync<Projection360Mode> Projection;
    public readonly Sync<float2> FieldOfView;
    public readonly Sync<float2> AngleOffset;
    public readonly Sync<float2> PerspectiveFieldOfView;
    public readonly Sync<float2> PerspectiveAngleOffset;
    public readonly Sync<Projection360Outside> OutsideMode;
    public readonly Sync<colorHDR> OutsideColor;

    [Group("Colour")]
    public readonly Sync<colorHDR> Tint;
    public readonly Sync<float> Exposure;
    public readonly Sync<float> Gamma;
    public readonly Sync<float?> MaxIntensity;
    public readonly AssetRef<TextureAsset> TintTexture;
    public readonly Sync<float2> TintTextureScale;
    public readonly Sync<float2> TintTextureOffset;
    public readonly Sync<Projection360TintMode> TintTextureMode;
    public readonly Sync<colorHDR> Tint0;
    public readonly Sync<colorHDR> Tint1;

    // Turns the lookup direction by a texture's red and green, in degrees of OffsetMagnitude at full
    // strength, OffsetMask scaling it per direction. Heat haze, wobbling portals.
    [Group("Distortion")]
    public readonly AssetRef<TextureAsset> OffsetTexture;
    public readonly AssetRef<TextureAsset> OffsetMask;
    public readonly Sync<float2> OffsetTextureScale;
    public readonly Sync<float2> OffsetTextureOffset;
    public readonly Sync<float2> OffsetMagnitude;

    [Group("Rendering")]
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;
    public readonly Sync<ZWrite> ZWrite;
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.Projection360;

    public Projection360Material()
    {
        Texture = new AssetRef<TextureAsset>(this);
        SecondaryTexture = new AssetRef<TextureAsset>(this);
        Cubemap = new AssetRef<CubemapAsset>(this);
        SecondaryCubemap = new AssetRef<CubemapAsset>(this);
        CubemapLOD = new Sync<float?>(this, null);
        TextureLerp = new Sync<float>(this, 0f);
        TextureScale = new Sync<float2>(this, float2.One);
        TextureOffset = new Sync<float2>(this, float2.Zero);

        StereoTextureTransform = new Sync<bool>(this, false);
        RightEyeTextureScale = new Sync<float2>(this, float2.One);
        RightEyeTextureOffset = new Sync<float2>(this, float2.Zero);

        Projection = new Sync<Projection360Mode>(this, Projection360Mode.View);
        FieldOfView = new Sync<float2>(this, new float2(360f, 180f));
        AngleOffset = new Sync<float2>(this, float2.Zero);
        PerspectiveFieldOfView = new Sync<float2>(this, new float2(45f, 45f));
        PerspectiveAngleOffset = new Sync<float2>(this, float2.Zero);
        OutsideMode = new Sync<Projection360Outside>(this, Projection360Outside.Clip);
        OutsideColor = new Sync<colorHDR>(this, colorHDR.Black);

        Tint = new Sync<colorHDR>(this, colorHDR.White);
        Exposure = new Sync<float>(this, 1f);
        Gamma = new Sync<float>(this, 1f);
        MaxIntensity = new Sync<float?>(this, null);
        TintTexture = new AssetRef<TextureAsset>(this);
        TintTextureScale = new Sync<float2>(this, float2.One);
        TintTextureOffset = new Sync<float2>(this, float2.Zero);
        TintTextureMode = new Sync<Projection360TintMode>(this, Projection360TintMode.Direct);
        Tint0 = new Sync<colorHDR>(this, colorHDR.Transparent);
        Tint1 = new Sync<colorHDR>(this, colorHDR.Transparent);

        OffsetTexture = new AssetRef<TextureAsset>(this);
        OffsetMask = new AssetRef<TextureAsset>(this);
        OffsetTextureScale = new Sync<float2>(this, float2.One);
        OffsetTextureOffset = new Sync<float2>(this, float2.Zero);
        OffsetMagnitude = new Sync<float2>(this, float2.One);

        // Seen from inside, so the outer faces are the ones culled.
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Alpha);
        Culling = new Sync<Culling>(this, Assets.Culling.Front);
        ZWrite = new Sync<ZWrite>(this, Assets.ZWrite.On);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        const float radians = MathF.PI / 180f;
        var blend = BlendMode.Value;

        asset.SetBlendMode(blend);
        asset.SetCulling(Culling.Value);
        asset.SetBool("DepthWrite", ZWrite.Value == Assets.ZWrite.On
            || (ZWrite.Value == Assets.ZWrite.Auto && blend is Assets.BlendMode.Opaque or Assets.BlendMode.Cutout));

        asset.SetInt("ProjectionMode", (int)Projection.Value);
        var fov = FieldOfView.Value;
        var angle = AngleOffset.Value;
        asset.SetFloat4("FieldOfView", new float4(fov.x, fov.y, angle.x, angle.y) * radians);
        var perspectiveFov = PerspectiveFieldOfView.Value;
        var perspectiveAngle = PerspectiveAngleOffset.Value;
        asset.SetFloat4("PerspectiveFov", new float4(perspectiveFov.x, perspectiveFov.y, perspectiveAngle.x, perspectiveAngle.y) * radians);
        asset.SetInt("OutsideMode", (int)OutsideMode.Value);
        asset.SetColor("OutsideColor", OutsideColor.Value);

        asset.SetTexture("MainTexture", Texture.Asset);
        asset.SetTexture("SecondTexture", SecondaryTexture.Asset);
        asset.SetBool("SecondaryBlend", SecondaryTexture.Target != null || TextureLerp.Value != 0f);
        asset.SetFloat("TextureLerp", TextureLerp.Value);

        asset.SetBool("UseCubemap", Cubemap.Target != null || SecondaryCubemap.Target != null);
        asset.SetBool("UseSecondCube", SecondaryCubemap.Target != null);
        asset.SetCubemap("MainCube", Cubemap.Asset);
        asset.SetCubemap("SecondCube", SecondaryCubemap.Asset);
        asset.SetBool("UseCubeLod", CubemapLOD.Value.HasValue);
        asset.SetFloat("CubeLod", CubemapLOD.Value ?? 0f);

        asset.SetFloat4("MainTextureTransform", Transform(TextureScale.Value, TextureOffset.Value));
        asset.SetBool("StereoTextureTransform", StereoTextureTransform.Value);
        asset.SetFloat4("RightEyeTransform", Transform(RightEyeTextureScale.Value, RightEyeTextureOffset.Value));

        asset.SetColor("Tint", Tint.Value);
        asset.SetFloat("Exposure", Exposure.Value);
        asset.SetFloat("Gamma", Gamma.Value);
        asset.SetBool("ClampIntensity", MaxIntensity.Value.HasValue);
        asset.SetFloat("MaxIntensity", MaxIntensity.Value ?? 0f);

        asset.SetTexture("TintTexture", TintTexture.Asset);
        asset.SetInt("TintTextureMode", (int)TintTextureMode.Value);
        asset.SetFloat4("TintTextureTransform", Transform(TintTextureScale.Value, TintTextureOffset.Value));
        asset.SetColor("Tint0", Tint0.Value);
        asset.SetColor("Tint1", Tint1.Value);

        asset.SetTexture("OffsetTexture", OffsetTexture.Asset);
        asset.SetTexture("OffsetMaskTexture", OffsetMask.Asset);
        asset.SetFloat4("OffsetTextureTransform", Transform(OffsetTextureScale.Value, OffsetTextureOffset.Value));
        asset.SetFloat2("OffsetMagnitude", OffsetMagnitude.Value * radians);

        asset.SetFloat("RenderQueue", RenderQueue.Value);
    }

    private static float4 Transform(float2 scale, float2 offset) => new(scale.x, scale.y, offset.x, offset.y);

    public override bool PrimaryColorIsTextured => Texture.Target != null || Cubemap.Target != null;

    public override bool TryGetPrimaryColor(out colorHDR color)
    {
        color = Tint.Value;
        return true;
    }
}
