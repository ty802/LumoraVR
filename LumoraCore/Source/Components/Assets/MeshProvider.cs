// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

/// <summary>
/// Component that provides mesh assets from URLs. The mesh gathers and decodes itself through the
/// <see cref="AssetManager"/> (see <see cref="MeshDecoder"/>); this component resolves the URL and
/// supplies the load options (import scale, keep-readable) as the variant descriptor, so meshes
/// with matching options for the same URL are shared.
/// </summary>
public class MeshProvider : StaticAssetProvider<MeshDataAsset>
{
    /// <summary>
    /// Whether to keep mesh data readable after GPU upload (for physics/runtime manipulation).
    /// </summary>
    public readonly Sync<bool> KeepReadable;

    /// <summary>Scale factor applied when loading the mesh.</summary>
    public readonly Sync<float> ImportScale;

    public MeshProvider()
    {
        KeepReadable = new Sync<bool>(this, false);
        ImportScale = new Sync<float>(this, 1.0f);
    }

    protected override IAssetVariantDescriptor? GetVariantDescriptor() =>
        new MeshVariantDescriptor(ImportScale.Value, KeepReadable.Value);
}
