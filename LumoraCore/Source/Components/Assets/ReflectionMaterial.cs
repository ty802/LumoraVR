// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// The surface of a mirror or a portal. It draws a texture, but not by UV: the texture is a render of
// the world from the viewer's eye reflected across this surface's plane, so every fragment reads it at
// its own SCREEN position and the reflection lands exactly where the eye expects it. Put any other
// texture in it and you get that texture stuck to the screen, which is the quickest way to tell a
// screen-projected material from a UV one.
//
// Pair it with a CameraPortal on the same slot, which is what fills ReflectionTexture every frame.
// The normal map does not shade anything - the reflected world is already lit - it nudges the sample
// position, which is what makes water ripple and old glass wobble.
//
// MirrorSampling exists because a Godot camera is always right-handed: a mirror renders with a proper
// camera, the image is the mirror image of what a mirror should show, and this un-mirrors it on
// sampling. A portal keeps its handedness and must not be un-mirrored, so CameraPortal writes this from
// its mode. -xlinka
[ComponentCategory("Assets/Materials/Special")]
public class ReflectionMaterial : MaterialProvider
{
    public readonly AssetRef<TextureAsset> ReflectionTexture;

    public readonly AssetRef<TextureAsset> NormalMap;
    public readonly Sync<float2> NormalMapScale;
    public readonly Sync<float2> NormalMapOffset;

    [Range(0f, 2f, "0.00")]
    public readonly Sync<float> Distort;

    public readonly Sync<colorHDR> TintColor;

    public readonly Sync<BlendMode> BlendMode;

    [Range(0f, 1f, "0.##")]
    public readonly Sync<float> AlphaCutoff;

    public readonly Sync<Culling> Culling;

    public readonly Sync<float> OffsetFactor;
    public readonly Sync<float> OffsetUnits;

    public readonly Sync<int> RenderQueue;

    // See the header. True for a mirror, false for a portal; CameraPortal keeps it right.
    public readonly Sync<bool> MirrorSampling;
    public readonly Sync<bool> SurfaceUVSampling;

    protected override MaterialType MaterialType => MaterialType.Reflection;

    public ReflectionMaterial()
    {
        ReflectionTexture = new AssetRef<TextureAsset>(this);

        NormalMap = new AssetRef<TextureAsset>(this);
        NormalMapScale = new Sync<float2>(this, float2.One);
        NormalMapOffset = new Sync<float2>(this, float2.Zero);

        Distort = new Sync<float>(this, 0f);
        TintColor = new Sync<colorHDR>(this, colorHDR.White);

        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Opaque);
        AlphaCutoff = new Sync<float>(this, 0.5f);
        Culling = new Sync<Culling>(this, Assets.Culling.Back);

        OffsetFactor = new Sync<float>(this, 0f);
        OffsetUnits = new Sync<float>(this, 0f);
        RenderQueue = new Sync<int>(this, -1);

        MirrorSampling = new Sync<bool>(this, true);
        SurfaceUVSampling = new Sync<bool>(this, false);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);

        asset.SetTexture("ReflectionTexture", ReflectionTexture.Target?.Asset!);

        asset.SetTexture("NormalMap", NormalMap.Target?.Asset!);
        asset.SetFloat2("NormalMapScale", NormalMapScale.Value);
        asset.SetFloat2("NormalMapOffset", NormalMapOffset.Value);
        asset.SetFloat("Distort", Distort.Value);

        asset.SetColor("TintColor", TintColor.Value);
        asset.SetFloat("AlphaCutoff", AlphaCutoff.Value);
        asset.SetBool("MirrorSampling", MirrorSampling.Value);
        asset.SetBool("SurfaceUvSampling", SurfaceUVSampling.Value);

        asset.SetFloat("OffsetFactor", OffsetFactor.Value);
        asset.SetFloat("OffsetUnits", OffsetUnits.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value);
    }

    public override bool TryGetPrimaryColor(out colorHDR color)
    {
        color = TintColor.Value;
        return true;
    }
}
