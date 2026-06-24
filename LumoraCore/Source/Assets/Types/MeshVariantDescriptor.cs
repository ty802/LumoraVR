// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

/// <summary>
/// The load options that distinguish one shared <see cref="MeshDataAsset"/> instance from another
/// for the same URL: import scale (changes geometry), whether the mesh data is kept readable after
/// upload, and which glTF mesh to decode (-1 = the whole file concatenated; >= 0 = only that mesh,
/// so a multi-mesh model becomes one Phos asset per mesh). Requests with equal descriptors share a mesh.
/// </summary>
public sealed record MeshVariantDescriptor(
    float ImportScale,
    bool KeepReadable,
    int MeshIndex = -1) : IAssetVariantDescriptor
{
    public static readonly MeshVariantDescriptor Default = new(ImportScale: 1.0f, KeepReadable: false);
}
