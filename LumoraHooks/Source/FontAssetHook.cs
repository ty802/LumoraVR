// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Godot;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using MathRect = Lumora.Core.Math.Rect;

namespace Lumora.Godot.Hooks;

public sealed class FontAssetHook : AssetHook, IFontAssetHook
{
    private const int AtlasSize = 2048;
    private const int Padding = 4;

    // One rasterization size for the entire atlas, scaled per Text on read. Godot's
    // MultichannelSignedDistanceField flag silently no-ops for LoadDynamicFont (verified
    // via diag: srcFormat=La8 with R=G=B=255, coverage in the alpha channel only). Treat
    // the cache as plain coverage and let the shader do fwidth-based edge AA. - xlinka
    private const int RasterSize = 48;
    private const int PixelRangeHint = 4;

    private readonly Dictionary<int, GlyphEntry> _glyphs = new();
    private FontFile? _font;
    private TextureAsset? _atlasTexture;
    private byte[]? _atlasPixels;
    private int _penX;
    private int _penY;
    private int _rowHeight;

    public bool IsValid => _font != null;
    public TextureAsset? AtlasTexture => _atlasTexture;
    public int PixelRange => PixelRangeHint;

    public void LoadFromFile(string path)
    {
        _font?.Dispose();
        _font = new FontFile();
        _glyphs.Clear();
        ResetAtlas();

        var error = _font.LoadDynamicFont(path);
        if (error != Error.Ok)
        {
            error = _font.LoadBitmapFont(path);
        }

        if (error != Error.Ok)
        {
            _font.Dispose();
            _font = null;
            GD.PrintErr($"FontAssetHook: Failed to load font '{path}' ({error})");
        }
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

            // Cached metrics are stored at RasterSize; scale to the requested display size. - xlinka
            float scale = displaySize / RasterSize;
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

        int glyphIndex = GetGlyphIndex(codepoint, RasterSize);
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

        var sizeKey = new Vector2I(RasterSize, 0);
        _font.RenderGlyph(0, sizeKey, glyphIndex);

        var glyphOffset = _font.GetGlyphOffset(0, sizeKey, glyphIndex);
        var advance = _font.GetGlyphAdvance(0, RasterSize, glyphIndex);
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
        CopyGlyph(sourceImage, sourceRect, dstX, dstY, width, height);
        UploadAtlas();

        // Metrics are stored at RasterSize coordinates; TryGetGlyph scales on read. Quad size
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
    }

    public float GetLineHeight(float displaySize)
    {
        if (_font == null) return displaySize;
        float raw = _font.GetHeight(RasterSize);
        return raw * displaySize / RasterSize;
    }

    public float GetAscent(float displaySize)
    {
        if (_font == null) return displaySize * 0.8f;
        return _font.GetAscent(RasterSize) * displaySize / RasterSize;
    }

    public float GetDescent(float displaySize)
    {
        if (_font == null) return displaySize * 0.2f;
        return _font.GetDescent(RasterSize) * displaySize / RasterSize;
    }

    public float GetKerning(int leftCodepoint, int rightCodepoint, float displaySize)
    {
        if (_font == null)
        {
            return 0f;
        }

        int left = GetGlyphIndex(leftCodepoint, RasterSize);
        int right = GetGlyphIndex(rightCodepoint, RasterSize);
        if (left == 0 || right == 0)
        {
            return 0f;
        }

        // Kerning is queried at RasterSize and scaled to the display size, same path as metrics. - xlinka
        return _font.GetKerning(0, RasterSize, new Vector2I(left, right)).X * displaySize / RasterSize;
    }

    public override void Unload()
    {
        _font?.Dispose();
        _font = null;
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
            advance = _font.GetCharSize(codepoint, RasterSize).X;
        }

        if (advance <= 0f)
        {
            advance = RasterSize * 0.5f;
        }

        _glyphs[codepoint] = new GlyphEntry(
            new GlyphMetrics { Advance = advance, Offset = float2.Zero, Size = float2.Zero },
            MathRect.Zero,
            Missing: false
        );
    }

    private void StoreMissingGlyph(int codepoint)
    {
        _glyphs[codepoint] = new GlyphEntry(default, MathRect.Zero, Missing: true);
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

                // Godot's runtime TTF rasterizer outputs LA8: luminance flat at 255, coverage
                // in the alpha channel. Confirmed via the [diag] log. Store coverage in our
                // atlas alpha; the shader applies fwidth-based AA on it. - xlinka
                var color = source.GetPixel(sx, sy);
                int dst = ((dstY + y) * AtlasSize + dstX + x) * 4;
                _atlasPixels[dst + 0] = 255;
                _atlasPixels[dst + 1] = 255;
                _atlasPixels[dst + 2] = 255;
                _atlasPixels[dst + 3] = ToByte(color.A);
            }
        }
    }

    private void UploadAtlas()
    {
        if (_atlasTexture != null && _atlasPixels != null)
        {
            _atlasTexture.SetImageData(_atlasPixels, AtlasSize, AtlasSize, false);
        }
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
    }

    private readonly record struct GlyphEntry(GlyphMetrics Metrics, MathRect UVRect, bool Missing);
}
