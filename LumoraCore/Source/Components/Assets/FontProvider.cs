// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core;
using Lumora.Core.Assets;

namespace Lumora.Core.Components.Assets;

[ComponentCategory("Assets")]
public sealed class FontProvider : UrlAssetProvider<FontSet, FontMetadata>
{
    public readonly SyncFieldList<Uri> FallbackURLs;

    public FontProvider()
    {
        FallbackURLs = new SyncFieldList<Uri>();
    }

    protected override Task<FontMetadata> LoadMetadata(Uri url, CancellationToken token)
    {
        long length = url.IsFile && File.Exists(url.LocalPath)
            ? new FileInfo(url.LocalPath).Length
            : 0;

        return Task.FromResult(new FontMetadata { ByteLength = length });
    }

    protected override Task<FontSet> LoadAssetData(Uri url, FontMetadata metadata, CancellationToken token)
    {
        if (!url.IsFile)
        {
            throw new NotSupportedException($"FontProvider only supports resolved file paths, got {url.Scheme}");
        }

        var set = new FontSet();
        set.InitializeDynamic();
        set.AddFont(LoadFont(url.LocalPath));

        foreach (var fallbackUrl in FallbackURLs)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            var resolved = ProcessURL(fallbackUrl);
            if (resolved == null || !resolved.IsFile || !File.Exists(resolved.LocalPath))
            {
                continue;
            }

            set.AddFont(LoadFont(resolved.LocalPath));
        }

        return Task.FromResult(set);
    }

    private static FontAsset LoadFont(string path)
    {
        var asset = new FontAsset();
        asset.InitializeDynamic();
        asset.LoadFromFile(path);
        return asset;
    }
}
