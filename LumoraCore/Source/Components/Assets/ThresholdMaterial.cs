// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

// Cuts whatever is BEHIND the surface to two levels per channel: below Threshold goes dark, above it
// goes full, with a ramp Transition wide between the two. Per channel, so a coloured scene comes out
// in the eight primaries rather than black and white.
//
// Drawn after the transparent band, over everything it filters. -xlinka
[ComponentCategory("Assets/Materials")]
public class ThresholdMaterial : MaterialProvider
{
    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Threshold;

    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Transition;

    // Additive and multiply combine the cut colour with the uncut one behind it.
    public readonly Sync<BlendMode> BlendMode;
    public readonly Sync<Culling> Culling;

    // -1 draws at 3500, after ordinary transparent surfaces.
    public readonly Sync<int> RenderQueue;

    protected override MaterialType MaterialType => MaterialType.Threshold;

    public override bool UseLoadingPlaceholder => false;

    public ThresholdMaterial()
    {
        Threshold = new Sync<float>(this, 0.5f);
        Transition = new Sync<float>(this, 0.005f);
        BlendMode = new Sync<BlendMode>(this, Assets.BlendMode.Opaque);
        Culling = new Sync<Culling>(this, Assets.Culling.Back);
        RenderQueue = new Sync<int>(this, -1);
    }

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        asset.SetBlendMode(BlendMode.Value);
        asset.SetCulling(Culling.Value);
        asset.SetFloat("Threshold", Threshold.Value);
        asset.SetFloat("Transition", Transition.Value);
        asset.SetFloat("RenderQueue", RenderQueue.Value < 0 ? 3500 : RenderQueue.Value);
    }
}
