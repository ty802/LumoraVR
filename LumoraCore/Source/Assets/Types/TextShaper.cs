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
        // Index of this glyph's codepoint in the shaped string. Lets a consumer map a
        // glyph back to per-source-character data (e.g. rich-text per-character color).
        public readonly int SourceIndex;

        public PositionedGlyph(FontAsset font, int codepoint, in GlyphMetrics metrics, in Rect uv, float x, int sourceIndex)
        {
            Font = font;
            Codepoint = codepoint;
            Metrics = metrics;
            UV = uv;
            X = x;
            SourceIndex = sourceIndex;
        }

        public PositionedGlyph WithX(float x) => new(Font, Codepoint, Metrics, UV, x, SourceIndex);
    }

    public sealed class Line
    {
        public readonly List<PositionedGlyph> Glyphs = new();
        public float Width;
        public float MaxSizeScale = 1f;   // largest per-glyph size multiplier on this line
        public bool HardBreak;            // ended at an explicit newline (not a wrap), so justify leaves it alone
    }

    private readonly List<Line> _lines = new();

    private bool _valid;
    private string? _shapedContent;
    private FontSet? _shapedFont;
    private float _shapedSize;
    private bool _shapedWrap;
    private float _shapedMaxWidth;
    private int _shapedGeneration;
    private IReadOnlyList<float>? _shapedScales;
    private IReadOnlyList<bool>? _shapedNobr;

    private float _size;
    private bool _wrap;
    private IReadOnlyList<float>? _sizeScales;
    private IReadOnlyList<bool>? _nobr;

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
    public bool Shape(FontSet font, string content, float size, bool wrap, float maxWidth, IReadOnlyList<float>? sizeScales = null, IReadOnlyList<bool>? nobr = null)
    {
        int generation = font.CacheGeneration;
        // Per-char size scales and non-breaking flags can change behind the same list reference, so never
        // reuse the cache when either is in play (uniform text still caches).
        if (sizeScales == null
            && nobr == null
            && _valid
            && _shapedScales == null
            && _shapedNobr == null
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
        _sizeScales = sizeScales;
        _nobr = nobr;
        BuildLines(font, content, maxWidth);

        _shapedFont = font;
        _shapedContent = content;
        _shapedSize = size;
        _shapedWrap = wrap;
        _shapedMaxWidth = maxWidth;
        _shapedGeneration = generation;
        _shapedScales = sizeScales;
        _shapedNobr = nobr;
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
            int start = i;
            int cp = char.ConvertToUtf32(content, i);
            if (char.IsHighSurrogate(content[i])) i++;
            if (cp == '\r') continue;

            if (cp == '\n')
            {
                FlushWord(ref line, ref word, ref pendingWhitespace, maxWidth);
                line.HardBreak = true;
                _lines.Add(line);
                line = new Line();
                pendingWhitespace = 0f;
                prevWordCodepoint = 0;
                prevWordFont = null;
                continue;
            }

            if (IsCollapsibleWhitespace(cp))
            {
                if (IsNoBreak(start))
                {
                    // Non-breaking space inside a <nobr> run: keep it IN the current word so wrapping can't
                    // split the run at this space (the whole run flushes as one unbreakable unit). -xlinka
                    AppendCodepoint(font, word, cp, start, ref prevWordCodepoint, ref prevWordFont);
                    continue;
                }

                FlushWord(ref line, ref word, ref pendingWhitespace, maxWidth);
                if (line.Width > 0f)
                {
                    pendingWhitespace += MeasureWhitespace(font, cp);
                }

                prevWordCodepoint = 0;
                prevWordFont = null;
                continue;
            }

            AppendCodepoint(font, word, cp, start, ref prevWordCodepoint, ref prevWordFont);
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

    private bool IsNoBreak(int sourceIndex)
        => _nobr != null && (uint)sourceIndex < (uint)_nobr.Count && _nobr[sourceIndex];

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

    private void AppendCodepoint(FontSet font, Line target, int codepoint, int sourceIndex, ref int prevCodepoint, ref FontAsset? prevFont)
    {
        float scale = 1f;
        if (_sizeScales != null && (uint)sourceIndex < (uint)_sizeScales.Count)
            scale = _sizeScales[sourceIndex];
        float gsize = _size * scale;
        if (scale > target.MaxSizeScale)
            target.MaxSizeScale = scale;

        float kerning = 0f;
        float advance;

        if (font.TryGetGlyph(codepoint, gsize, out var metrics, out var uv, out var glyphFont) && glyphFont != null)
        {
            if (prevCodepoint != 0 && ReferenceEquals(prevFont, glyphFont))
            {
                kerning = font.GetKerning(glyphFont, prevCodepoint, codepoint, gsize);
            }

            target.Glyphs.Add(new PositionedGlyph(glyphFont, codepoint, metrics, uv, target.Width + kerning, sourceIndex));
            advance = metrics.Advance;
            prevCodepoint = codepoint;
            prevFont = glyphFont;
        }
        else
        {
            advance = gsize * 0.5f;
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
        if (source.MaxSizeScale > target.MaxSizeScale)
            target.MaxSizeScale = source.MaxSizeScale;
    }
}
