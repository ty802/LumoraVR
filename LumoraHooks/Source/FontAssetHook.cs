// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using MathRect = Lumora.Core.Math.Rect;

namespace Lumora.Godot.Hooks;

[ImplementableHook(typeof(FontAsset))]
public sealed class FontAssetHook : AssetHook, IFontAssetHook
{
    private const int AtlasSize = 2048;
    private const int Padding = 4;

    // One rasterization size for the whole atlas, scaled per Text on read. Coverage fonts raster at 96 so small
    // text has detail to downsample from; MSDF fonts raster at their msdf_size (a distance field is
    // resolution-independent, so the small base is enough and keeps the atlas tight). Set at load. -xlinka
    private int _rasterSize = 96;
    // Distance-field span the MSDF was generated with (the font's msdf_pixel_range); carried to the atlas + shader.
    private int _msdfPixelRange = 8;
    // MSDF was REQUESTED (imported with the flag, or a raw load asked for it) vs actually PRODUCED. The raw
    // dynamic rasterizer can silently emit LA8 coverage instead, so we confirm per-glyph before trusting it;
    // _isMsdf is what gates the distance-field path everywhere. -xlinka
    private bool _msdfIntended;
    private bool _msdfDetermined;
    private bool _isMsdf;

    private readonly Dictionary<int, GlyphEntry> _glyphs = new();
    private FontFile? _font;
    // True only when WE created the FontFile (raw LoadDynamicFont). When the font came from ResourceLoader it's
    // a shared imported resource we must NOT dispose. -xlinka
    private bool _ownsFont;
    private TextureAsset? _atlasTexture;
    private byte[]? _atlasPixels;
    private int _penX;
    private int _penY;
    private int _rowHeight;
    private int _cacheGeneration;

    public bool IsValid => _font != null;
    public TextureAsset? AtlasTexture => _atlasTexture;
    public int PixelRange => _msdfPixelRange;
    public int CacheGeneration => _cacheGeneration;

    public void LoadFromFile(string path)
    {
        if (_ownsFont) _font?.Dispose();
        _font = null;
        _ownsFont = false;
        _glyphs.Clear();
        ResetAtlas();

        // Godot strips the RAW source of imported files from exports (our font has a .ttf.import) and keeps only
        // the imported resource inside the .pck. But the engine resolved res:// to an OS DISK path
        // (ResourceRoot = GlobalizePath("res://")), which only has real files in the EDITOR - in an export that
        // dir is the exe folder and the font isn't there, so the raw read failed and text vanished. Recover the
        // res:// path by stripping ResourceRoot (the exact value that built this path - reliable in editor AND
        // export, unlike a GlobalizePath/LocalizePath round-trip) and load the IMPORTED FontFile from the .pck
        // via ResourceLoader. Genuine external files (a user-imported font outside ResourceRoot) still raw-load.
        // -xlinka
        string? resPath = TryGetResPath(path);
        bool resExists = resPath != null && ResourceLoader.Exists(resPath);
        // DIAG (export-debug): shows the resolved path + whether the imported resource was found. Remove once
        // text is confirmed in the Linux/exported build. -xlinka
        GD.Print($"FontAssetHook.LoadFromFile: root='{Lumora.Core.Engine.Current?.ResourceRoot}' path='{path}' " +
                 $"resPath='{resPath}' resExists={resExists} rawExists={System.IO.File.Exists(path)}");
        if (resExists)
        {
            _font = ResourceLoader.Load<FontFile>(resPath);
            if (_font != null)
            {
                _ownsFont = false; // shared imported resource - do not dispose it
                GD.Print($"FontAssetHook: loaded imported FontFile from '{resPath}'");
                ConfigureMsdf();
                return;
            }
            GD.PrintErr($"FontAssetHook: ResourceLoader.Load returned null for '{resPath}'");
        }

        _font = new FontFile();
        _ownsFont = true;
        // Ask the raw rasterizer for MSDF as well. It may honor it (crisp) or silently emit LA8 coverage; the
        // per-glyph check in RequestGlyph downgrades _isMsdf when it falls back, so requesting it is always safe.
        // Set BEFORE loading so the rasterizer picks it up. -xlinka
        _font.MultichannelSignedDistanceField = true;
        _font.MsdfPixelRange = _msdfPixelRange;
        _font.MsdfSize = 48;
        var error = _font.LoadDynamicFont(path);
        if (error != Error.Ok)
        {
            error = _font.LoadBitmapFont(path);
        }

        if (error != Error.Ok)
        {
            _font.Dispose();
            _font = null;
            _ownsFont = false;
            GD.PrintErr($"FontAssetHook: Failed to load font '{path}' ({error})");
        }

        ConfigureMsdf();
    }

    // Map an engine resource disk path back to its res:// VFS path. The engine builds resource paths as
    // ResourceRoot + relative (ResourceRoot == GlobalizePath("res://")), so stripping ResourceRoot recovers the
    // relative part regardless of whether the file exists on disk - which is exactly the case in a .pck export.
    // Returns null for paths NOT under ResourceRoot (genuine external files), which keep the raw-load path. -xlinka
    private static string? TryGetResPath(string path)
    {
        var root = Lumora.Core.Engine.Current?.ResourceRoot;
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path))
            return null;

        static string Norm(string p) => p.Replace('\\', '/').TrimEnd('/');
        var np = Norm(path);
        var nr = Norm(root);
        if (np.Length <= nr.Length || !np.StartsWith(nr, StringComparison.OrdinalIgnoreCase))
            return null;

        var rel = np.Substring(nr.Length).TrimStart('/');
        return rel.Length == 0 ? null : "res://" + rel;
    }

    // Read MSDF intent + raster size from the loaded font. Imported fonts carry the flag from their import
    // settings; the raw path sets it above. Actual MSDF output is confirmed on the first real glyph. -xlinka
    private void ConfigureMsdf()
    {
        _msdfDetermined = false;
        _isMsdf = false;
        if (_font == null)
        {
            _msdfIntended = false;
            _rasterSize = 96;
            return;
        }

        _msdfIntended = _font.MultichannelSignedDistanceField;
        _msdfPixelRange = _font.MsdfPixelRange > 0 ? _font.MsdfPixelRange : 8;
        int msdfSize = _font.MsdfSize > 0 ? _font.MsdfSize : 48;
        // MSDF is resolution-independent, so raster at its native msdf_size; coverage needs a larger base to
        // downsample from. -xlinka
        _rasterSize = _msdfIntended ? msdfSize : 96;
    }

    // Confirm whether the font actually produced a distance field (varying RGB) vs coverage (flat white RGB with
    // detail in alpha). The raw dynamic rasterizer can silently ignore the MSDF request, so we never run the
    // distance reconstruction on coverage data. Runs once, on the first real glyph. -xlinka
    private void DetermineMsdf(Image source, Rect2 sourceRect)
    {
        _msdfDetermined = true;
        _isMsdf = _msdfIntended && HasVaryingRgb(source, sourceRect);

        if (_atlasTexture != null)
        {
            _atlasTexture.IsMSDF = _isMsdf;
            _atlasTexture.MsdfPixelRange = _msdfPixelRange;
        }

        GD.Print($"FontAssetHook: MSDF intended={_msdfIntended} produced={_isMsdf} rasterSize={_rasterSize} pixelRange={_msdfPixelRange}");
    }

    private static bool HasVaryingRgb(Image source, Rect2 sourceRect)
    {
        int x0 = (int)Math.Floor(sourceRect.Position.X);
        int y0 = (int)Math.Floor(sourceRect.Position.Y);
        int w = (int)Math.Ceiling(sourceRect.Size.X);
        int h = (int)Math.Ceiling(sourceRect.Size.Y);
        int sw = source.GetWidth();
        int sh = source.GetHeight();

        // Coverage glyphs are flat white in RGB (shape lives in alpha); an MSDF has per-channel distances, so
        // any pixel whose channels differ or dip below full is proof of a real distance field. -xlinka
        int stepY = Math.Max(1, h / 8);
        int stepX = Math.Max(1, w / 8);
        for (int j = 0; j < h; j += stepY)
        {
            int sy = y0 + j;
            if (sy < 0 || sy >= sh) continue;
            for (int i = 0; i < w; i += stepX)
            {
                int sx = x0 + i;
                if (sx < 0 || sx >= sw) continue;
                var c = source.GetPixel(sx, sy);
                if (c.R != c.G || c.G != c.B || c.R < 0.99f)
                    return true;
            }
        }
        return false;
    }

    public bool TryGetGlyph(int codepoint, float displaySize, out GlyphMetrics metrics, out MathRect uvRect)
    {
        if (_glyphs.TryGetValue(codepoint, out var entry))
        {
            if (entry.Missing)
            {
                metrics = default;
                uvRect = default;
                return false;
            }

            // Cached metrics are stored at _rasterSize; scale to the requested display size. - xlinka
            float scale = displaySize / _rasterSize;
            metrics = new GlyphMetrics
            {
                Advance = entry.Metrics.Advance * scale,
                Offset = entry.Metrics.Offset * scale,
                Size = entry.Metrics.Size * scale,
            };
            uvRect = entry.UVRect;
            return true;
        }

        metrics = default;
        uvRect = default;
        return false;
    }

    public void RequestGlyph(int codepoint, float displaySize)
    {
        if (_font == null)
        {
            return;
        }

        if (_glyphs.ContainsKey(codepoint))
        {
            return;
        }

        EnsureAtlas();

        int glyphIndex = GetGlyphIndex(codepoint, _rasterSize);
        if (glyphIndex == 0 && codepoint != '?')
        {
            StoreMissingGlyph(codepoint);
            return;
        }

        if (glyphIndex == 0)
        {
            StoreBlankGlyph(codepoint);
            return;
        }

        var sizeKey = new Vector2I(_rasterSize, 0);
        _font.RenderGlyph(0, sizeKey, glyphIndex);

        var glyphOffset = _font.GetGlyphOffset(0, sizeKey, glyphIndex);
        var advance = _font.GetGlyphAdvance(0, _rasterSize, glyphIndex);
        var sourceRect = _font.GetGlyphUVRect(0, sizeKey, glyphIndex);
        int textureIndex = _font.GetGlyphTextureIdx(0, sizeKey, glyphIndex);

        int width = Math.Max(0, (int)Math.Ceiling(sourceRect.Size.X));
        int height = Math.Max(0, (int)Math.Ceiling(sourceRect.Size.Y));
        if (width == 0 || height == 0)
        {
            StoreBlankGlyph(codepoint, advance.X);
            return;
        }

        if (!Reserve(width, height, out int dstX, out int dstY))
        {
            StoreBlankGlyph(codepoint, advance.X);
            return;
        }

        var sourceImage = _font.GetTextureImage(0, sizeKey, textureIndex);
        if (sourceImage == null)
        {
            StoreBlankGlyph(codepoint, advance.X);
            return;
        }

        sourceImage.Convert(Image.Format.Rgba8);
        if (!_msdfDetermined)
        {
            DetermineMsdf(sourceImage, sourceRect);
        }
        CopyGlyph(sourceImage, sourceRect, dstX, dstY, width, height);
        ScheduleUpload();

        // Metrics are stored at _rasterSize coordinates; TryGetGlyph scales on read. Quad size
        // matches the atlas region (width x height), so UV-to-quad ratio is 1:1. - xlinka
        var metrics = new GlyphMetrics
        {
            Advance = advance.X,
            Offset = new float2(glyphOffset.X, -glyphOffset.Y - height),
            Size = new float2(width, height)
        };

        var uv = new MathRect(
            dstX / (float)AtlasSize,
            dstY / (float)AtlasSize,
            width / (float)AtlasSize,
            height / (float)AtlasSize
        );

        _glyphs[codepoint] = new GlyphEntry(metrics, uv, Missing: false);
        _cacheGeneration++;
    }

    public float GetLineHeight(float displaySize)
    {
        if (_font == null) return displaySize;
        float raw = _font.GetHeight(_rasterSize);
        return raw * displaySize / _rasterSize;
    }

    public float GetAscent(float displaySize)
    {
        if (_font == null) return displaySize * 0.8f;
        return _font.GetAscent(_rasterSize) * displaySize / _rasterSize;
    }

    public float GetDescent(float displaySize)
    {
        if (_font == null) return displaySize * 0.2f;
        return _font.GetDescent(_rasterSize) * displaySize / _rasterSize;
    }

    public float GetKerning(int leftCodepoint, int rightCodepoint, float displaySize)
    {
        if (_font == null)
        {
            return 0f;
        }

        int left = GetGlyphIndex(leftCodepoint, _rasterSize);
        int right = GetGlyphIndex(rightCodepoint, _rasterSize);
        if (left == 0 || right == 0)
        {
            return 0f;
        }

        // Kerning is queried at _rasterSize and scaled to the display size, same path as metrics. - xlinka
        return _font.GetKerning(0, _rasterSize, new Vector2I(left, right)).X * displaySize / _rasterSize;
    }

    public override void Unload()
    {
        if (_ownsFont) _font?.Dispose();
        _font = null;
        _ownsFont = false;
        _atlasTexture?.Unload();
        _atlasTexture = null;
        _atlasPixels = null;
        _glyphs.Clear();
    }

    private int GetGlyphIndex(int codepoint, int pixelSize)
    {
        if (_font == null)
        {
            return 0;
        }

        return _font.GetGlyphIndex(pixelSize, codepoint, 0);
    }

    private void StoreBlankGlyph(int codepoint, float advance = 0f)
    {
        if (_font != null && advance <= 0f)
        {
            advance = _font.GetCharSize(codepoint, _rasterSize).X;
        }

        if (advance <= 0f)
        {
            advance = _rasterSize * 0.5f;
        }

        _glyphs[codepoint] = new GlyphEntry(
            new GlyphMetrics { Advance = advance, Offset = float2.Zero, Size = float2.Zero },
            MathRect.Zero,
            Missing: false
        );
        _cacheGeneration++;
    }

    private void StoreMissingGlyph(int codepoint)
    {
        _glyphs[codepoint] = new GlyphEntry(default, MathRect.Zero, Missing: true);
        _cacheGeneration++;
    }

    private void EnsureAtlas()
    {
        if (_atlasTexture != null && _atlasPixels != null)
        {
            return;
        }

        _atlasTexture = new TextureAsset();
        _atlasTexture.InitializeDynamic();
        ResetAtlas();
    }

    private void ResetAtlas()
    {
        _atlasPixels = new byte[AtlasSize * AtlasSize * 4];
        _penX = Padding;
        _penY = Padding;
        _rowHeight = 0;
        _cacheGeneration++;
        UploadAtlas();
    }

    private bool Reserve(int width, int height, out int x, out int y)
    {
        if (_penX + width + Padding > AtlasSize)
        {
            _penX = Padding;
            _penY += _rowHeight + Padding;
            _rowHeight = 0;
        }

        if (_penY + height + Padding > AtlasSize)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = _penX;
        y = _penY;
        _penX += width + Padding;
        _rowHeight = Math.Max(_rowHeight, height);
        return true;
    }

    private void CopyGlyph(Image source, Rect2 sourceRect, int dstX, int dstY, int width, int height)
    {
        if (_atlasPixels == null)
        {
            return;
        }

        int srcX = (int)Math.Floor(sourceRect.Position.X);
        int srcY = (int)Math.Floor(sourceRect.Position.Y);
        int srcWidth = source.GetWidth();
        int srcHeight = source.GetHeight();

        for (int y = 0; y < height; y++)
        {
            int sy = srcY + y;
            if (sy < 0 || sy >= srcHeight)
            {
                continue;
            }

            for (int x = 0; x < width; x++)
            {
                int sx = srcX + x;
                if (sx < 0 || sx >= srcWidth)
                {
                    continue;
                }

                var color = source.GetPixel(sx, sy);
                int dst = ((dstY + y) * AtlasSize + dstX + x) * 4;
                if (_isMsdf)
                {
                    // Keep the 3-channel distance field in RGB; the shader rebuilds the glyph from their median.
                    // Alpha carries no shape, so leave it opaque. - xlinka
                    _atlasPixels[dst + 0] = ToByte(color.R);
                    _atlasPixels[dst + 1] = ToByte(color.G);
                    _atlasPixels[dst + 2] = ToByte(color.B);
                    _atlasPixels[dst + 3] = 255;
                }
                else
                {
                    // Coverage path: the rasterizer outputs LA8 (flat white RGB, coverage in alpha). Store
                    // coverage in our atlas alpha; the shader applies fwidth-based AA on it. - xlinka
                    _atlasPixels[dst + 0] = 255;
                    _atlasPixels[dst + 1] = 255;
                    _atlasPixels[dst + 2] = 255;
                    _atlasPixels[dst + 3] = ToByte(color.A);
                }
            }
        }
    }

    private bool _uploadPending;

    private void ScheduleUpload()
    {
        if (_uploadPending)
            return;
        _uploadPending = true;
        Callable.From(FlushAtlasUpload).CallDeferred();
    }

    private void FlushAtlasUpload()
    {
        _uploadPending = false;
        UploadAtlas();
    }

    private void UploadAtlas()
    {
        if (_atlasTexture != null && _atlasPixels != null)
        {
            _atlasTexture.SetImageData(_atlasPixels, AtlasSize, AtlasSize, true);
        }
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
    }

    private readonly record struct GlyphEntry(GlyphMetrics Metrics, MathRect UVRect, bool Missing);
}
