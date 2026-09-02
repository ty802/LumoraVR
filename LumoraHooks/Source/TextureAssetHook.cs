// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

[ImplementableHook(typeof(TextureAsset))]
public class TextureAssetHook : AssetHook, ITextureAssetHook, IGodotTexture
{
    private ImageTexture _godotTexture = null!;
    private TextureWrapMode _wrapU = TextureWrapMode.Repeat;
    private TextureWrapMode _wrapV = TextureWrapMode.Repeat;

    // Completed at the END of the deferred BuildTexture (when _godotTexture actually exists). TextureAsset.LoadSelf
    // awaits this before reporting the asset loaded, so a material can't bind this texture while it's still null. -xlinka
    private System.Threading.Tasks.TaskCompletionSource<bool> _uploadTcs = null!;

    // bumping this invalidates every cached blob
    // 2: compressed caches written from bottom-up rows (before the decode flip was removed) are stale.
    private const int GpuCacheVersion = 2;

    public ImageTexture GodotTexture => _godotTexture;

    public Texture2D GodotTexture2D => _godotTexture;

    public bool IsValid => _godotTexture != null;

    public void UploadData(byte[] pixels, int width, int height, bool hasMipmaps)
    {
        // Godot RenderingServer resource creation must run on the main thread, but this is invoked INLINE from the
        // off-main asset-load thread (TextureAsset.SetImageData). Touching the renderer off-thread is an intermittent
        // stall/corruption bug; defer the actual build to the main thread (same pattern as RenderTextureHook). The
        // TCS completes when that deferred build has run, so LoadSelf can wait for the GPU texture to truly exist
        // before reporting the asset loaded (else a material binds a null albedo in the gap = white body). -xlinka
        var tcs = NewUploadTcs();
        global::Godot.Callable.From(() =>
        {
            try { BuildTexture(pixels, width, height, hasMipmaps); }
            finally { tcs.TrySetResult(true); }
        }).CallDeferred();
    }

    // Compression runs OFF the main thread. BPTC on a 2048-square chain is hundreds of milliseconds
    // of pure CPU; doing it inline in the deferred main-thread build would drop frames on every
    // texture load. Image is a plain CPU-side resource until it is handed to ImageTexture, so the
    // expensive part is safe to do on a worker and only the resource creation is deferred. The
    // compressed result is cached to disk keyed by the variant, so each variant pays that cost once
    // per machine and every later load is a file read. -xlinka
    public void UploadTexture(TextureUploadRequest request)
    {
        var levels = request.MipLevels;
        if (levels == null || levels.Length == 0 || request.Width <= 0 || request.Height <= 0)
            return;

        var tcs = NewUploadTcs();
        System.Threading.Tasks.Task.Run(() =>
        {
            Image image = null!;
            try
            {
                image = PrepareImage(request);
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"TextureAssetHook: failed to prepare {request.Width}x{request.Height} texture: {ex.Message}");
            }

            global::Godot.Callable.From(() =>
            {
                try
                {
                    if (image != null)
                    {
                        Adopt(image);
                        ReportUpload(request, image);
                    }
                }
                catch (Exception ex)
                {
                    LumoraLogger.Error($"TextureAssetHook: failed to build texture: {ex.Message}");
                }
                finally { tcs.TrySetResult(true); }
            }).CallDeferred();
        });
    }

    // completes once the deferred BuildTexture has run (the GPU texture exists) -xlinka
    public System.Threading.Tasks.Task WaitForUploadAsync()
        => _uploadTcs?.Task ?? System.Threading.Tasks.Task.CompletedTask;

    private System.Threading.Tasks.TaskCompletionSource<bool> NewUploadTcs()
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        _uploadTcs = tcs;
        return tcs;
    }

    // OFF-THREAD IMAGE PREPARATION

    private static Image PrepareImage(TextureUploadRequest request)
    {
        var levels = request.MipLevels;
        int width = request.Width;
        int height = request.Height;

        // A cached compressed blob short-circuits everything: same key means the same pixels and the
        // same compression intent, so there is nothing to recompute.
        string cachePath = GetCachePath(request);
        if (cachePath != null && TryReadCache(cachePath, out var cached))
            return cached;

        Image image = BuildSourceImage(levels, width, height, request.GenerateMipmaps);

        if (request.AllowBlockCompression && TryCompress(image, request))
        {
            if (cachePath != null)
                TryWriteCache(cachePath, image);
        }

        return image;
    }

    private static Image BuildSourceImage(byte[][] levels, int width, int height, bool generateMipmaps)
    {
        // A supplied chain is only usable as-is when it runs all the way to 1x1, which is the layout
        // the renderer expects behind a single buffer. A partial chain is not an error, it just means
        // we hand over the base level and let the renderer fill the rest in.
        int fullLevels = FullMipCount(width, height);
        if (levels.Length > 1 && levels.Length == fullLevels)
        {
            long total = 0;
            foreach (var level in levels)
                total += level?.LongLength ?? 0;

            var packed = new byte[total];
            int offset = 0;
            foreach (var level in levels)
            {
                if (level == null)
                    continue;
                Buffer.BlockCopy(level, 0, packed, offset, level.Length);
                offset += level.Length;
            }
            return Image.CreateFromData(width, height, true, Image.Format.Rgba8, packed);
        }

        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, levels[0]);
        if (generateMipmaps || levels.Length > 1)
            image.GenerateMipmaps();
        return image;
    }

    // Block-compress in place. Returns true only when the format actually changed, so a device that
    // cannot do BPTC silently keeps the uncompressed image instead of failing the load.
    private static bool TryCompress(Image image, TextureUploadRequest request)
    {
        // Below one full block there is nothing to compress and Godot rejects it outright.
        if (image.GetWidth() < 4 || image.GetHeight() < 4)
            return false;

        var source = request.IsNormalMap
            ? Image.CompressSource.Normal
            : (request.SRgb == true ? Image.CompressSource.Srgb : Image.CompressSource.Generic);

        try
        {
            var before = image.GetFormat();
            var error = image.Compress(Image.CompressMode.Bptc, source);
            if (error != Error.Ok)
            {
                LumoraLogger.Log($"TextureAssetHook: BPTC unavailable for {image.GetWidth()}x{image.GetHeight()} ({error}); keeping RGBA8");
                return false;
            }
            return image.GetFormat() != before;
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"TextureAssetHook: compression failed, keeping RGBA8: {ex.Message}");
            return false;
        }
    }

    private static int FullMipCount(int width, int height)
    {
        int levels = 1;
        int w = System.Math.Max(1, width), h = System.Math.Max(1, height);
        while (w > 1 || h > 1)
        {
            w = System.Math.Max(1, w >> 1);
            h = System.Math.Max(1, h >> 1);
            levels++;
        }
        return levels;
    }

    // MACHINE-LOCAL COMPRESSED CACHE

    private static string GetCachePath(TextureUploadRequest request)
    {
        if (!request.AllowBlockCompression
            || string.IsNullOrEmpty(request.CacheKey)
            || string.IsNullOrEmpty(request.CacheDirectory))
            return null!;
        // Extension shared with the engine side: it probes this same path to find out whether a rung
        // is already compressed, and a private copy of the string here would silently make every
        // probe miss. -xlinka
        return Path.Combine(request.CacheDirectory!, request.CacheKey + TextureGpuCache.Extension);
    }

    private static bool TryReadCache(string path, out Image image)
    {
        image = null!;
        try
        {
            if (!File.Exists(path))
                return false;

            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (reader.ReadInt32() != GpuCacheVersion)
                return false;

            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            var format = (Image.Format)reader.ReadInt32();
            bool mipmaps = reader.ReadBoolean();
            int length = reader.ReadInt32();
            if (width <= 0 || height <= 0 || length <= 0 || length > stream.Length - stream.Position)
                return false;

            var data = reader.ReadBytes(length);
            image = Image.CreateFromData(width, height, mipmaps, format, data);
            return image != null;
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"TextureAssetHook: unreadable compressed cache '{path}': {ex.Message}");
            return false;
        }
    }

    private static void TryWriteCache(string path, Image image)
    {
        try
        {
            var data = image.GetData();
            if (data == null || data.Length == 0)
                return;

            // Write beside the target then move into place, so a crash mid-write can never leave a
            // truncated blob that a later run would happily load as a texture.
            var temp = path + ".tmp";
            using (var stream = File.Create(temp))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(GpuCacheVersion);
                writer.Write(image.GetWidth());
                writer.Write(image.GetHeight());
                writer.Write((int)image.GetFormat());
                writer.Write(image.HasMipmaps());
                writer.Write(data.Length);
                writer.Write(data);
            }
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"TextureAssetHook: could not cache compressed texture '{path}': {ex.Message}");
        }
    }

    // MAIN-THREAD RESOURCE CREATION

    // Keep the SAME texture resource for the life of the asset and push new contents through it.
    //
    // This is what makes a progressive load look like a texture getting sharper instead of a texture
    // disappearing and coming back. Every material that bound this texture holds a reference to the
    // resource object, and swapping the object out from under them leaves each one pointing at a
    // disposed handle until something re-binds it. SetImage does not have that problem: internally it
    // builds the new texture and swaps it in behind the same handle, so a size change (128 -> 2048)
    // and a format change (RGBA8 -> BPTC) both land without anyone downstream noticing. Do not
    // "optimize" this back into dispose-and-recreate. -xlinka
    private void Adopt(Image image)
    {
        if (_godotTexture == null)
            _godotTexture = ImageTexture.CreateFromImage(image);
        else
            _godotTexture.SetImage(image);
    }

    private void ReportUpload(TextureUploadRequest request, Image image)
    {
        if (request.Report == null)
            return;

        var data = image.GetData();
        long bytes = data?.Length ?? 0;
        int mipCount = image.HasMipmaps() ? FullMipCount(image.GetWidth(), image.GetHeight()) : 1;
        request.Report(MapFormat(image.GetFormat()), mipCount, bytes);
    }

    private static TextureFormatKind MapFormat(Image.Format format) => format switch
    {
        Image.Format.Rgba8 => TextureFormatKind.RGBA8,
        Image.Format.Rgb8 => TextureFormatKind.RGB8,
        Image.Format.Rg8 => TextureFormatKind.RG8,
        Image.Format.R8 => TextureFormatKind.R8,
        Image.Format.Dxt1 => TextureFormatKind.BC1_RGB,
        Image.Format.Dxt5 => TextureFormatKind.BC3_RGBA,
        Image.Format.RgtcR => TextureFormatKind.BC4_R,
        Image.Format.RgtcRg => TextureFormatKind.BC5_RG,
        Image.Format.BptcRgba => TextureFormatKind.BC7_RGBA,
        _ => TextureFormatKind.Unknown,
    };

    private void BuildTexture(byte[] pixels, int width, int height, bool hasMipmaps)
    {
        // Check if this is raw RGBA data or encoded image data
        int expectedRgbaSize = width * height * 4;

        Image image;

        if (width > 0 && height > 0 && pixels.Length == expectedRgbaSize)
        {
            // Raw RGBA8 pixel data - create without mipmaps first (we only have base level data)
            image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels);

            if (hasMipmaps)
            {
                image.GenerateMipmaps();
            }
        }
        else
        {
            // Encoded image data (PNG, JPEG, etc.) - let Godot decode it
            image = new Image();
            var error = image.LoadPngFromBuffer(pixels);

            if (error != Error.Ok)
            {
                error = image.LoadJpgFromBuffer(pixels);
            }

            if (error != Error.Ok)
            {
                error = image.LoadWebpFromBuffer(pixels);
            }

            if (error != Error.Ok)
            {
                error = image.LoadBmpFromBuffer(pixels);
            }

            if (error != Error.Ok)
            {
                GD.PrintErr($"TextureAssetHook: Failed to decode image data");
                return;
            }

            if (hasMipmaps)
            {
                image.GenerateMipmaps();
            }
        }

        Adopt(image);
    }

    // In Godot 4, wrap mode is set on the material/sampler, not the texture itself; we just store
    // the values for reference.
    public void SetWrapMode(TextureWrapMode wrapU, TextureWrapMode wrapV)
    {
        _wrapU = wrapU;
        _wrapV = wrapV;
    }

    public TextureWrapMode WrapModeU => _wrapU;

    public TextureWrapMode WrapModeV => _wrapV;

    public override void Unload()
    {
        if (_godotTexture != null)
        {
            _godotTexture.Dispose();
            _godotTexture = null!;
        }
    }
}
