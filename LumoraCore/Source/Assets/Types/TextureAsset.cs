// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading.Tasks;
using Lumora.Core.Logging;

namespace Lumora.Core.Assets;

// Asset containing texture/image data (RGBA8 with optional mipmaps). URL instances gather and
// decode their image in LoadSelf; procedural instances (atlases, render targets,
// generated textures) are created via InitializeDynamic and fed data through
// SetImageData.
//
// A URL instance resolves a VARIANT before it gathers anything: the descriptor's resolution cap
// plus the source's real dimensions name a generated blob, and if that blob exists locally or on a
// peer it is loaded instead of the source file. The base URL is never rewritten, so a save that
// references local://.../hash keeps working whether or not any variant was ever generated. -xlinka
public class TextureAsset : ImplementableAsset<ITextureAssetHook>
{
    private byte[] _pixelData = null!;
    private byte[][] _mipLevels = Array.Empty<byte[]>();
    private int _width;
    private int _height;
    private bool _hasMipmaps;

    public int Width => _width;

    public int Height => _height;

    public byte[] PixelData => _pixelData;

    // One entry means base level only; the renderer may still generate mips from it.
    public byte[][] MipLevels => _mipLevels;

    public bool HasMipmaps => _hasMipmaps;

    // Counted, mips included.
    public long MemorySize
    {
        get
        {
            if (_mipLevels.Length > 1)
            {
                long total = 0;
                foreach (var level in _mipLevels)
                    total += level?.LongLength ?? 0;
                return total;
            }
            return _pixelData?.LongLength ?? 0;
        }
    }

    // Null for a procedural texture that has not been analyzed.
    public TextureMetadata? Metadata { get; private set; }

    // Null when it loaded the source directly.
    public TextureVariantId? LoadedVariant { get; private set; }

    public bool IsVariant => LoadedVariant is { IsOriginal: false };

    // Set by the font atlas builder.
    public bool IsMSDF { get; set; }

    // Matches the font's msdf_pixel_range. Only meaningful when IsMSDF.
    public int MsdfPixelRange { get; set; } = 8;

    // URL (static) instances only; procedural instances set data through SetImageData.
    protected override async Task LoadSelf()
    {
        var descriptor = TargetVariant as TextureVariantDescriptor ?? TextureVariantDescriptor.Default;

        if (await TryLoadVariantAsync(descriptor).ConfigureAwait(false))
            return;

        var bytes = await AssetManager.RequestGather(AssetURL).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0)
        {
            FailLoad($"No image data gathered for {AssetURL}");
            return;
        }

        int width, height;
        byte[]? rgba;
        try
        {
            rgba = TextureVariantStore.DecodeRgba(bytes, out width, out height);
        }
        catch (Exception ex)
        {
            FailLoad($"Failed to decode image {AssetURL}: {ex.Message}");
            return;
        }

        if (rgba == null || width <= 0 || height <= 0)
        {
            FailLoad($"Failed to decode image {AssetURL}: no pixels");
            return;
        }

        int mipCount = descriptor.GenerateMipmaps ? TextureMetadata.FullMipCount(width, height) : 1;
        Metadata = TextureMetadata.Analyze(
            rgba, width, height, mipCount,
            bytes.LongLength,
            descriptor.GenerateMipmaps
                ? TextureMetadata.ComputeGpuBytes(TextureFormatKind.RGBA8, width, height, mipCount)
                : (long)width * height * 4,
            TextureMetadata.DetectSRgb(bytes),
            descriptor.IsNormalMap);
        LoadedVariant = new TextureVariantId(0, descriptor.GenerateMipmaps, descriptor.Compression);

        SetImageData(rgba, width, height, descriptor.GenerateMipmaps);
        Hook?.SetWrapMode(descriptor.WrapU, descriptor.WrapV);

        // Don't let LoadSelf return (-> FullyLoaded) until the GPU texture is actually built. SetImageData ->
        // Hook.UploadData only QUEUES a deferred main-thread build; if we reported loaded now, the asset's single
        // load-complete notification would fire while Hook.IsValid is still false, the consuming material would bind
        // a null albedo, and nothing re-binds it (white body). Awaiting the upload makes that notification fire when
        // the texture is genuinely usable: the asset isn't marked loaded until the GPU upload completes. -xlinka
        if (Hook != null)
            await Hook.WaitForUploadAsync().ConfigureAwait(false);
    }

    // Try to satisfy this request from a generated variant instead of the source file.
    //
    // Two lookups, both cheap: the metadata sidecar tells us the source's real dimensions without
    // decoding anything (and a peer can serve that sidecar), and those dimensions plus the
    // descriptor's cap name exactly one variant blob. If the blob is not reachable we fall through
    // to the source, so this is always an optimization and never a failure mode.
    //
    // What this deliberately does NOT do is progressive swap-up (show the 256 immediately, replace
    // it when the 1024 lands). The blocker is not the fetch layer, which handles a second request
    // fine; it is that a loaded asset has no re-load path. LoadSelf runs once, gated on the Created
    // state, and the load-complete notification fires once. Making an asset swap its own contents
    // after it has reported FullyLoaded means reopening that state machine for every asset type,
    // not just textures, and the failure mode if it is done carelessly is the white-body race
    // coming back. Deferred on purpose, not overlooked. Changing the QUALITY SETTING does swap
    // variants, because that produces a different descriptor and therefore a different asset
    // instance, which needs none of that machinery. -xlinka
    private async Task<bool> TryLoadVariantAsync(TextureVariantDescriptor descriptor)
    {
        if (descriptor.MaxSize <= 0 || AssetURL == null || AssetURL.Scheme != "local")
            return false;

        var db = Engine?.LocalDB;
        if (db == null)
            return false;

        var baseUri = AssetURL.OriginalString;
        var sourceMeta = await TextureVariantStore.TryLoadMetadataAsync(db, baseUri, AssetManager).ConfigureAwait(false);
        if (sourceMeta == null || sourceMeta.Width <= 0)
        {
            // Never analyzed. Generating on the spot would put a full decode plus four mip chains in
            // front of the user, so queue it instead and load the source this time: the cap starts
            // applying from the next load. This is what gives textures that never went through the
            // image importer (a model's embedded maps, anything restored from a save) their variants.
            TextureVariantStore.EnsureGeneratedInBackground(db, baseUri, descriptor.IsNormalMap, descriptor.GenerateMipmaps);
            return false;
        }

        var id = descriptor.ResolveVariant(sourceMeta.Width, sourceMeta.Height);
        if (id.IsOriginal)
            return false;

        var variantUri = TextureVariantStore.GetVariantUri(baseUri, id);
        if (variantUri == null)
            return false;

        // Held locally: straight read. Owned by a peer: worth asking for, because a variant is
        // exactly what we want crossing the wire instead of a full-resolution original, and a peer
        // that hasn't generated it answers "not available" and we fall through to the source. Owned
        // by US and missing: nobody else has it either, so don't waste a round trip. -xlinka
        bool local = db.Exists(variantUri);
        if (!local && (TextureVariantStore.IsOwnedLocally(db, baseUri) || Engine?.ActiveSessionTransferer == null))
            return false;

        byte[]? blob;
        try
        {
            blob = await AssetManager.RequestGather(new Uri(variantUri)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"TextureAsset: variant fetch failed for {variantUri}: {ex.Message}");
            return false;
        }

        var data = TextureVariantStore.Decode(blob);
        if (data == null)
        {
            if (blob != null)
                Logger.Warn($"TextureAsset: variant {id.Identifier} of {baseUri} is unreadable; using the source");
            return false;
        }

        // A peer-supplied variant is written into our cache under the SAME derived address, so the
        // next load resolves it locally. Without this the generic receive path would re-key it by
        // content hash and every session would pull it again. -xlinka
        if (!local && blob != null)
        {
            await db.SaveDerivedAssetAsync(baseUri, id.Identifier, blob, TextureVariantStore.VariantExtension)
                .ConfigureAwait(false);
        }

        Metadata = new TextureMetadata
        {
            Width = data.Width,
            Height = data.Height,
            ContentFormat = data.ContentFormat,
            HasAlpha = data.HasAlpha,
            MipCount = data.MipCount,
            SourceBytes = blob?.LongLength ?? 0,
            DecodedBytes = data.DecodedBytes,
            SRgb = sourceMeta.SRgb,
            IsNormalMap = descriptor.IsNormalMap || sourceMeta.IsNormalMap,
            VariantId = id.Identifier,
        };
        LoadedVariant = id;

        SetMipData(data.Levels, data.Width, data.Height);
        Hook?.SetWrapMode(descriptor.WrapU, descriptor.WrapV);

        if (Hook != null)
            await Hook.WaitForUploadAsync().ConfigureAwait(false);
        return true;
    }

    // Expects RGBA8, 4 bytes per pixel.
    public void SetImageData(byte[] pixels, int width, int height, bool mipmaps = false)
    {
        if (pixels == null)
        {
            throw new ArgumentNullException(nameof(pixels));
        }

        int expectedSize = width * height * 4; // RGBA8
        if (pixels.Length < expectedSize)
        {
            throw new ArgumentException($"Pixel data too small. Expected at least {expectedSize} bytes, got {pixels.Length}");
        }

        _pixelData = pixels;
        _mipLevels = new[] { pixels };
        _width = width;
        _height = height;
        _hasMipmaps = mipmaps;
        Version++;

        Upload(generateMipmaps: mipmaps);
    }

    // Used by the variant path, where the chain was filtered once at generation time and every machine that
    // loads it gets the same levels instead of re-deriving them.
    public void SetMipData(byte[][] levels, int width, int height)
    {
        if (levels == null || levels.Length == 0)
            throw new ArgumentException("At least the base mip level is required", nameof(levels));

        _mipLevels = levels;
        _pixelData = levels[0];
        _width = width;
        _height = height;
        _hasMipmaps = levels.Length > 1;
        Version++;

        Upload(generateMipmaps: false);
    }

    private void Upload(bool generateMipmaps)
    {
        var hook = Hook;
        if (hook == null)
            return;

        var metadata = Metadata;
        string? cacheKey = null;
        string? cacheDirectory = null;

        // Only a URL-backed texture gets a compressed-cache slot: its bytes are addressed by a
        // stable URI, so the same key always means the same pixels. A procedural texture is
        // regenerated by whatever built it and has no such identity to key on.
        if (AssetURL != null && metadata != null)
        {
            var db = Engine?.LocalDB;
            if (db != null)
            {
                cacheKey = BuildCacheKey(AssetURL.OriginalString, LoadedVariant, metadata);
                cacheDirectory = db.GetGpuCachePath();
            }
        }

        hook.UploadTexture(new TextureUploadRequest
        {
            MipLevels = _mipLevels,
            Width = _width,
            Height = _height,
            GenerateMipmaps = generateMipmaps,
            AllowBlockCompression = LoadedVariant?.Compression == TextureCompressionKind.Block,
            HasAlpha = metadata?.HasAlpha ?? true,
            IsNormalMap = metadata?.IsNormalMap ?? false,
            SRgb = metadata?.SRgb,
            CacheKey = cacheKey,
            CacheDirectory = cacheDirectory,
            Report = ReportUploaded,
        });
    }

    // Hash the identity of the pixels (URI + variant) and the dimensions, so a cache file can never
    // be read back for a different image. Dimensions are in the key because a procedural resize
    // reuses the same URI.
    private static string BuildCacheKey(string uri, TextureVariantId? variant, TextureMetadata metadata)
    {
        string identity = $"{uri}|{variant?.Identifier ?? "src"}|{metadata.Width}x{metadata.Height}|{metadata.MipCount}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    // This is the only way GpuFormat is ever set, which is why the VRAM figures downstream are measurements and
    // not estimates.
    public void ReportUploaded(TextureFormatKind gpuFormat, int mipCount, long gpuBytes)
    {
        var metadata = Metadata;
        if (metadata == null)
            return;
        metadata.GpuFormat = gpuFormat;
        _hasMipmaps = mipCount > 1;
    }

    public static TextureAsset FromRgba(byte[] rgbaData, int width, int height, bool mipmaps = false)
    {
        var texture = new TextureAsset();
        texture.InitializeDynamic();
        texture.SetImageData(rgbaData, width, height, mipmaps);
        return texture;
    }

    public override void Unload()
    {
        _pixelData = null!;
        _mipLevels = Array.Empty<byte[]>();
        _width = 0;
        _height = 0;
        _hasMipmaps = false;
        Metadata = null;
        LoadedVariant = null;
        base.Unload();
    }
}
