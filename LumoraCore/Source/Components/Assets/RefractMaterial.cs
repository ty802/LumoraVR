// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Glass, water, heat haze: whatever is BEHIND the surface, shifted along the surface's normal as seen
// from the eye. With no normal map a curved mesh bends the view like a lens; a normal map adds ripples
// on top. RefractionStrength is how far, and it weakens with distance the way real glass seems to.
//
// A shifted sample can land on something standing IN FRONT of the glass and drag it into the picture.
// The depth at the sample guards against that: the bend fades to none as the sampled surface comes
// nearer than DepthBias metres behind the glass, over DepthFadeDivisor metres. A big bias with a tiny
// divisor turns the guard off.
//
// Drawn just before the ordinary transparent band, so glass sees the opaque world and transparent
// surfaces in front of it still draw over it. -xlinka
[ComponentCategory("Assets/Materials")]
public class RefractMaterial : MaterialProvider
{
    [Range(0f, 1f, "0.##")]
    public readonly Sync<float> RefractionStrength;
    public readonly Sync<float> DepthBias;
    public readonly Sync<float> DepthFadeDivisor;

    public readonly AssetRef<TextureAsset> NormalMap;
    public readonly Sync<float2> NormalTextureScale;
    public readonly Sync<float2> NormalTextureOffset;

    // Additive and multiply combine the bent colour with the straight one behind it.
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;

    // -1 draws at 2900.
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.Refract;

    public override bool UseLoadingPlaceholder => false;

    public RefractMaterial()
    {
        RefractionStrength = new Sync<float>(this, 0.05f);
        DepthBias = new Sync<float>(this, 0f);
        DepthFadeDivisor = new Sync<float>(this, 0.1f);
        NormalMap = new AssetRef<TextureAsset>(this);
        NormalTextureScale = new Sync<float2>(this, float2.One);
        NormalTextureOffset = new Sync<float2>(this, float2.Zero);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Opaque);
        Culling = new Sync<Culling>(this, Assets.Culling.Back);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        asset.SetFloat("RefractionStrength", RefractionStrength.Value);
        asset.SetFloat("DepthBias", DepthBias.Value);
        asset.SetFloat("DepthFadeDivisor", DepthFadeDivisor.Value);
        asset.SetTexture("NormalMap", NormalMap.Asset);
        var scale = NormalTextureScale.Value;
        var offset = NormalTextureOffset.Value;
        asset.SetFloat4("NormalMapTransform", new float4(scale.x, scale.y, offset.x, offset.y));
        asset.SetFloat("RenderQueue", RenderQueue.Value < 0 ? 2900 : RenderQueue.Value);
    }
}
