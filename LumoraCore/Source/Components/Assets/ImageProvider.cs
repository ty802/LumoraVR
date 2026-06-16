// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

/// <summary>
/// Component that provides texture assets from URLs. The texture gathers and decodes itself
/// through the <see cref="AssetManager"/>; this component resolves the URL and supplies the load
/// options (wrap/mipmaps/normal-map) as the variant descriptor, so textures with matching options
/// for the same URL are shared.
/// </summary>
public class ImageProvider : StaticAssetProvider<TextureAsset>
{
    /// <summary>Whether this is a normal map (affects compression/variant identity).</summary>
    public readonly Sync<bool> IsNormalMap;

    /// <summary>Horizontal texture wrap mode.</summary>
    public readonly Sync<TextureWrapMode> WrapModeU;

    /// <summary>Vertical texture wrap mode.</summary>
    public readonly Sync<TextureWrapMode> WrapModeV;

    /// <summary>Whether to generate mipmaps for the texture.</summary>
    public readonly Sync<bool> GenerateMipmaps;

    public ImageProvider()
    {
        IsNormalMap = new Sync<bool>(this, false);
        WrapModeU = new Sync<TextureWrapMode>(this, TextureWrapMode.Repeat);
        WrapModeV = new Sync<TextureWrapMode>(this, TextureWrapMode.Repeat);
        GenerateMipmaps = new Sync<bool>(this, true);
    }

    protected override IAssetVariantDescriptor? GetVariantDescriptor() =>
        new TextureVariantDescriptor(GenerateMipmaps.Value, WrapModeU.Value, WrapModeV.Value, IsNormalMap.Value);
}
