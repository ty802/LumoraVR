// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core.Assets;

namespace Lumora.Core.Components.Assets;

[ComponentCategory("Assets")]
public sealed class FontProvider : UrlAssetProvider<FontAsset, FontMetadata>
{
    protected override Task<FontMetadata> LoadMetadata(Uri url, CancellationToken token)
    {
        long length = url.IsFile && File.Exists(url.LocalPath)
            ? new FileInfo(url.LocalPath).Length
            : 0;

        return Task.FromResult(new FontMetadata { ByteLength = length });
    }

    protected override Task<FontAsset> LoadAssetData(Uri url, FontMetadata metadata, CancellationToken token)
    {
        if (!url.IsFile)
        {
            throw new NotSupportedException($"FontProvider only supports resolved file paths, got {url.Scheme}");
        }

        var asset = new FontAsset();
        asset.InitializeDynamic();
        asset.LoadFromFile(url.LocalPath);
        return Task.FromResult(asset);
    }
}
