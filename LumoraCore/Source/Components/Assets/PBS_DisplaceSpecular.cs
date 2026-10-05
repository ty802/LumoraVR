// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Displace material in the specular workflow. SpecularColor's RGB is the reflectance and its alpha the
// smoothness; SpecularMap carries the same pair and replaces the colour when set.
[ComponentCategory("Assets/Materials")]
public class PBS_DisplaceSpecular : PBS_DisplaceMaterial
{
    public readonly Sync<colorHDR> SpecularColor;
    public readonly AssetRef<TextureAsset> SpecularMap;

    protected override bool Specular => true;

    public float Smoothness
    {
        get => SpecularColor.Value.a;
        set
        {
            var c = SpecularColor.Value;
            SpecularColor.Value = new colorHDR(c.r, c.g, c.b, value);
        }
    }

    public PBS_DisplaceSpecular()
    {
        SpecularColor = new Sync<colorHDR>(this, colorHDR.Transparent);
        SpecularMap = new AssetRef<TextureAsset>(this);
    }

    protected override void UpdateWorkflow(MaterialAsset asset)
    {
        asset.SetColor("SpecularColor", SpecularColor.Value);
        asset.SetTexture("SpecularMap", SpecularMap.Asset);
    }
}
