// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

// Slice material in the metallic workflow. MetallicMap is red for metallic and alpha for smoothness.
[ComponentCategory("Assets/Materials")]
public class PBS_SliceMetallic : PBS_Slice
{
    public readonly Sync<float> Metallic;
    public readonly Sync<float> Smoothness;
    public readonly AssetRef<TextureAsset> MetallicMap;

    protected override bool Specular => false;

    public PBS_SliceMetallic()
    {
        Metallic = new Sync<float>(this, 0f);
        Smoothness = new Sync<float>(this, 0.25f);
        MetallicMap = new AssetRef<TextureAsset>(this);
    }

    protected override void UpdateWorkflow(MaterialAsset asset)
    {
        asset.SetFloat("Metallic", Metallic.Value);
        asset.SetFloat("Smoothness", Smoothness.Value);
        asset.SetTexture("MetallicMap", MetallicMap.Asset);
    }
}
