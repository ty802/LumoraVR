// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

[ComponentCategory("Assets/Material Property Blocks")]
public class MainTexturePropertyBlock : MaterialPropertyBlockProvider
{
    public readonly AssetRef<TextureAsset> Texture;

    public TextureAsset DirectTexture { get; set; } = null!;

    public MainTexturePropertyBlock()
    {
        Texture = new AssetRef<TextureAsset>(this);
    }

    protected override void UpdateBlock(MaterialPropertyBlockAsset asset)
    {
        asset.SetTexture("Texture", Texture.Asset ?? DirectTexture);
    }
}
