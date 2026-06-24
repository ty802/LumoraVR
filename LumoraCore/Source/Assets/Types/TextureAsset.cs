// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Threading.Tasks;
using StbImageSharp;

namespace Lumora.Core.Assets;

/// <summary>
/// Asset containing texture/image data (RGBA8 with optional mipmaps). URL instances gather and
/// decode their image in <see cref="LoadSelf"/>; procedural instances (atlases, render targets,
/// generated textures) are created via <c>InitializeDynamic</c> and fed data through
/// <see cref="SetImageData"/>.
/// </summary>
public class TextureAsset : ImplementableAsset<ITextureAssetHook>
{
    private byte[] _pixelData = null!;
    private int _width;
    private int _height;
    private bool _hasMipmaps;

    /// <summary>
    /// Texture width in pixels.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Texture height in pixels.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// The raw RGBA8 pixel data.
    /// </summary>
    public byte[] PixelData => _pixelData;

    /// <summary>
    /// Whether the texture has mipmaps.
    /// </summary>
    public bool HasMipmaps => _hasMipmaps;

    /// <summary>
    /// Memory size in bytes of the pixel data.
    /// </summary>
    public long MemorySize => _pixelData?.Length ?? 0;

    /// <summary>
    /// Gather and decode this texture from its URL. Only runs for URL (static) instances;
    /// procedural instances set their data directly via <see cref="SetImageData"/>.
    /// </summary>
    protected override async Task LoadSelf()
    {
        var bytes = await AssetManager.RequestGather(AssetURL).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0)
        {
            FailLoad($"No image data gathered for {AssetURL}");
            return;
        }

        var descriptor = TargetVariant as TextureVariantDescriptor ?? TextureVariantDescriptor.Default;

        int width, height;
        byte[] rgba;
        try
        {
            using var stream = new MemoryStream(bytes);
            var result = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            width = result.Width;
            height = result.Height;
            rgba = FlipRgbaVertical(result.Data, width, height);
        }
        catch (Exception ex)
        {
            FailLoad($"Failed to decode image {AssetURL}: {ex.Message}");
            return;
        }

        SetImageData(rgba, width, height, descriptor.GenerateMipmaps);
        Hook?.SetWrapMode(descriptor.WrapU, descriptor.WrapV);

        // Don't let LoadSelf return (-> FullyLoaded) until the GPU texture is actually built. SetImageData ->
        // Hook.UploadData only QUEUES a deferred main-thread build; if we reported loaded now, the asset's single
        // load-complete notification would fire while Hook.IsValid is still false, the consuming material would bind
        // a null albedo, and nothing re-binds it (white body). Awaiting the upload makes that notification fire when
        // the texture is genuinely usable - mirrors the reference, which awaits texture GPU integration before
        // marking the asset loaded. -xlinka
        if (Hook != null)
            await Hook.WaitForUploadAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Set the texture image data.
    /// Expects RGBA8 format (4 bytes per pixel).
    /// </summary>
    /// <param name="pixels">RGBA8 pixel data</param>
    /// <param name="width">Width in pixels</param>
    /// <param name="height">Height in pixels</param>
    /// <param name="mipmaps">Whether mipmaps should be generated</param>
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
        _width = width;
        _height = height;
        _hasMipmaps = mipmaps;
        Version++;

        // Upload to hook if available
        Hook?.UploadData(_pixelData, _width, _height, _hasMipmaps);
    }

    /// <summary>
    /// Create texture from existing RGBA8 data.
    /// </summary>
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
        _width = 0;
        _height = 0;
        _hasMipmaps = false;
        base.Unload();
    }

    // StbImageSharp decodes top-down; the renderer's UV origin expects bottom-up, so flip rows.
    private static byte[] FlipRgbaVertical(byte[] rgba, int width, int height)
    {
        if (rgba == null || width <= 0 || height <= 0)
        {
            return rgba!;
        }

        int stride = width * 4;
        if (rgba.Length < stride * height)
        {
            return rgba;
        }

        var flipped = new byte[rgba.Length];
        for (int y = 0; y < height; y++)
        {
            int src = y * stride;
            int dst = (height - 1 - y) * stride;
            Buffer.BlockCopy(rgba, src, flipped, dst, stride);
        }

        return flipped;
    }
}
