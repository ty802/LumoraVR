// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

// Shared text shaping engine: codepoints in, positioned glyph lines out.
// One shaping path for every text consumer (Helio Text graphics, world-space
// TextRenderer) so kerning, wrapping, and whitespace behave identically.
// Shaping is the dominant per-rebuild cost (per-glyph atlas + kerning lookups,
// line breaking, allocations), so results are cached and only re-shaped when
// an input changes. The atlas never repacks, so cached glyph UVs stay valid
// until the font gains glyphs (CacheGeneration). - xlinka
public sealed class TextShaper
{
    public readonly struct PositionedGlyph
    {
        public readonly FontAsset Font;
        public readonly int Codepoint;
        public readonly GlyphMetrics Metrics;
        public readonly Rect UV;
        public readonly float X;

        public PositionedGlyph(FontAsset font, int codepoint, in GlyphMetrics metrics, in Rect uv, float x)
        {
            Font = font;
            Codepoint = codepoint;
            Metrics = metrics;
            UV = uv;
            X = x;
        }

        public PositionedGlyph WithX(float x) => new(Font, Codepoint, Metrics, UV, x);
    }

    public sealed class Line
    {
        public readonly List<PositionedGlyph> Glyphs = new();
        public float Width;
    }

    private readonly List<Line> _lines = new();

    private bool _valid;
    private string? _shapedContent;
    private FontSet? _shapedFont;
    private float _shapedSize;
    private bool _shapedWrap;
    private float _shapedMaxWidth;
    private int _shapedGeneration;

    private float _size;
    private bool _wrap;

    public IReadOnlyList<Line> Lines => _lines;

    public float MaxLineWidth
    {
        get
        {
            float width = 0f;
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Width > width)
                    width = _lines[i].Width;
            }
            return width;
        }
    }

    /// <summary>
    /// Shape content into positioned glyph lines. No-op when every shaping
    /// input matches the cached result. Returns true if a reshape happened.
    /// </summary>
    public bool Shape(FontSet font, string content, float size, bool wrap, float maxWidth)
    {
        int generation = font.CacheGeneration;
        if (_valid
            && ReferenceEquals(_shapedFont, font)
            && _shapedContent == content
            && _shapedSize == size
            && _shapedWrap == wrap
            && _shapedMaxWidth == maxWidth
            && _shapedGeneration == generation)
        {
            return false;
        }

        _size = size;
        _wrap = wrap;
        BuildLines(font, content, maxWidth);

        _shapedFont = font;
        _shapedContent = content;
        _shapedSize = size;
        _shapedWrap = wrap;
        _shapedMaxWidth = maxWidth;
        _shapedGeneration = generation;
        _valid = true;
        return true;
    }

    public void Invalidate()
    {
        _valid = false;
    }

    /// <summary>
    /// Request atlas rasterization for every renderable codepoint in content.
    /// Call before Shape when the content may contain new glyphs.
    /// </summary>
    public static void RequestGlyphs(FontSet font, string content, float size)
    {
        if (font == null || string.IsNullOrEmpty(content))
            return;

        for (int i = 0; i < content.Length; i++)
        {
            int cp = char.ConvertToUtf32(content, i);
            if (char.IsHighSurrogate(content[i])) i++;
            if (cp == '\r' || cp == '\n' || cp == '\t') continue;
            font.RequestGlyph(cp, size);
        }
    }

    private void BuildLines(FontSet font, string content, float maxWidth)
    {
        _lines.Clear();
        if (string.IsNullOrEmpty(content))
            return;

        var line = new Line();
        var word = new Line();
        float pendingWhitespace = 0f;
        int prevWordCodepoint = 0;
        FontAsset? prevWordFont = null;

        for (int i = 0; i < content.Length; i++)
        {
            int cp = char.ConvertToUtf32(content, i);
            if (char.IsHighSurrogate(content[i])) i++;
            if (cp == '\r') continue;

            if (cp == '\n')
            {
                FlushWord(ref line, ref word, ref pendingWhitespace, maxWidth);
                _lines.Add(line);
                line = new Line();
                pendingWhitespace = 0f;
                prevWordCodepoint = 0;
                prevWordFont = null;
                continue;
            }

            if (IsCollapsibleWhitespace(cp))
            {
                FlushWord(ref line, ref word, ref pendingWhitespace, maxWidth);
                if (line.Width > 0f)
                {
                    pendingWhitespace += MeasureWhitespace(font, cp);
                }

                prevWordCodepoint = 0;
                prevWordFont = null;
                continue;
            }

            AppendCodepoint(font, word, cp, ref prevWordCodepoint, ref prevWordFont);
        }

        FlushWord(ref line, ref word, ref pendingWhitespace, maxWidth);
        _lines.Add(line);
    }

    private void FlushWord(ref Line line, ref Line word, ref float pendingWhitespace, float maxWidth)
    {
        if (word.Width <= 0f && word.Glyphs.Count == 0) return;

        float spacer = line.Width > 0f ? pendingWhitespace : 0f;
        if (_wrap && maxWidth > 0f && line.Width > 0f && line.Width + spacer + word.Width > maxWidth)
        {
            _lines.Add(line);
            line = new Line();
            spacer = 0f;
        }

        AppendLayout(line, word, line.Width + spacer);
        word = new Line();
        pendingWhitespace = 0f;
    }

    private static bool IsCollapsibleWhitespace(int codepoint)
        => codepoint == ' ' || codepoint == '\t';

    private float MeasureWhitespace(FontSet font, int codepoint)
    {
        if (codepoint == '\t')
        {
            return _size * 2f;
        }

        return font.TryGetGlyph(' ', _size, out var metrics, out _, out _)
            ? metrics.Advance
            : _size * 0.5f;
    }

    private void AppendCodepoint(FontSet font, Line target, int codepoint, ref int prevCodepoint, ref FontAsset? prevFont)
    {
        float kerning = 0f;
        float advance;

        if (font.TryGetGlyph(codepoint, _size, out var metrics, out var uv, out var glyphFont) && glyphFont != null)
        {
            if (prevCodepoint != 0 && ReferenceEquals(prevFont, glyphFont))
            {
                kerning = font.GetKerning(glyphFont, prevCodepoint, codepoint, _size);
            }

            target.Glyphs.Add(new PositionedGlyph(glyphFont, codepoint, metrics, uv, target.Width + kerning));
            advance = metrics.Advance;
            prevCodepoint = codepoint;
            prevFont = glyphFont;
        }
        else
        {
            advance = _size * 0.5f;
            prevCodepoint = 0;
            prevFont = null;
        }

        target.Width += kerning + advance;
    }

    private static void AppendLayout(Line target, Line source, float startX)
    {
        for (int i = 0; i < source.Glyphs.Count; i++)
        {
            var glyph = source.Glyphs[i];
            target.Glyphs.Add(glyph.WithX(startX + glyph.X));
        }

        target.Width = startX + source.Width;
    }
}
