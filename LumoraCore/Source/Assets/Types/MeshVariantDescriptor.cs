// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

/// <summary>
/// The load options that distinguish one shared <see cref="MeshDataAsset"/> instance from another
/// for the same URL: import scale (changes geometry) and whether the mesh data is kept readable
/// after upload. Requests with equal descriptors share a mesh.
/// </summary>
public sealed record MeshVariantDescriptor(
    float ImportScale,
    bool KeepReadable) : IAssetVariantDescriptor
{
    public static readonly MeshVariantDescriptor Default = new(ImportScale: 1.0f, KeepReadable: false);
}
