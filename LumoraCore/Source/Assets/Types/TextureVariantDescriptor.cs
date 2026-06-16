// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

/// <summary>
/// The load options that distinguish one shared <see cref="TextureAsset"/> instance from another
/// for the same URL: mipmaps, wrap modes, and normal-map flag. Requests with equal descriptors
/// share a texture; differing ones get separate instances.
/// </summary>
public sealed record TextureVariantDescriptor(
    bool GenerateMipmaps,
    TextureWrapMode WrapU,
    TextureWrapMode WrapV,
    bool IsNormalMap) : IAssetVariantDescriptor
{
    public static readonly TextureVariantDescriptor Default =
        new(GenerateMipmaps: true, TextureWrapMode.Repeat, TextureWrapMode.Repeat, IsNormalMap: false);
}
