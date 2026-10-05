// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// A hue, saturation and value shift over whatever is BEHIND the surface. Each channel is multiplied,
// then offset: hue wraps round, saturation clamps to 0..1, value is left alone so a bright scene stays
// bright. All three are 0..1, so a hue offset of 0.5 is the opposite colour. A fresh one shifts all
// three by 0.2, which is visibly doing something rather than an invisible pane.
//
// Drawn after the transparent band, over everything it filters. -xlinka
[ComponentCategory("Assets/Materials")]
public class HSVMaterial : MaterialProvider
{
    public readonly Sync<float3> HSV_Offset;
    public readonly Sync<float3> HSV_Multiply;

    // Additive and multiply combine the shifted colour with the unshifted one behind it.
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;

    // -1 draws at 3500, after ordinary transparent surfaces.
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.HSV;

    public override bool UseLoadingPlaceholder => false;

    public HSVMaterial()
    {
        HSV_Offset = new Sync<float3>(this, new float3(0.2f, 0.2f, 0.2f));
        HSV_Multiply = new Sync<float3>(this, new float3(1f, 1f, 1f));
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Opaque);
        Culling = new Sync<Culling>(this, Assets.Culling.Back);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        asset.SetFloat3("HsvOffset", HSV_Offset.Value);
        asset.SetFloat3("HsvMultiply", HSV_Multiply.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value < 0 ? 3500 : RenderQueue.Value);
    }
}
