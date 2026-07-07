// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

// emits one textured quad per glyph laid out within the RectTransform rect.
// Rich text (inline tags) is supported via RichTextParser when RichText is set.
// TODO - xlinka: overflow modes, kerning beyond pair table
public class Text : Graphic, ILayoutElement
{
    public readonly Sync<string> Content;
    public readonly AssetRef<FontSet> Font;
    // Resolves inline <sprite=name> tags in rich text. Optional; when unset, sprite placeholders draw nothing. -xlinka
    public readonly SyncRef<SpriteSet> Sprites;
    // Resolves inline <font=name> tags in rich text. Optional; when unset, <font> runs use the default Font. -xlinka
    public readonly SyncRef<FontSetGroup> Fonts;
    public readonly AssetRef<MaterialAsset> Material;
    public readonly Sync<float> Size;
    public readonly Sync<color> Color;
    public readonly Sync<TextHorizontalAlignment> HorizontalAlignment;
    public readonly Sync<TextVerticalAlignment> VerticalAlignment;
    public readonly Sync<float> LineSpacing;
    public readonly Sync<bool> WordWrap;
    // When true, Content is parsed for inline tags (<color>, <alpha>, ...) - see RichTextParser.
    public readonly Sync<bool> RichText;
    // Editing visuals, driven by TextInput. CaretPosition < 0 = no caret (not editing);
    // SelectionStart < 0 = no selection. Indices are into the SHAPED string.
    public readonly Sync<int> CaretPosition;
    public readonly Sync<int> SelectionStart;
    public readonly Sync<color> CaretColor;
    public readonly Sync<color> SelectionColor;
    // Auto-size: shrink the rendered size to fit the rect on the enabled axis/axes, down to AutoSizeMin
    // (Size is the maximum). Off by default, so existing text is unchanged. On an auto-sized axis the
    // text reports no preferred size to layout - it adapts to whatever rect it's given.
    public readonly Sync<bool> AutoSizeHorizontal;
    public readonly Sync<bool> AutoSizeVertical;
    public readonly Sync<float> AutoSizeMin;

    private string _content = string.Empty;
    private IAssetProvider<FontSet>? _font;
    private IAssetProvider<MaterialAsset>? _material;
    private float _size;
    private color _color;
    private TextHorizontalAlignment _hAlign;
    private TextVerticalAlignment _vAlign;
    private float _lineSpacing;
    private bool _wrap;
    private bool _richText;
    private int _caretPos;
    private int _selStart;
    private color _caretColor;
    private color _selColor;
    private bool _autoH;
    private bool _autoV;
    private float _autoMin;
    // Text actually fed to the shaper: the raw Content, or the tag-stripped text in rich mode.
    private string _shapedText = string.Empty;
    private readonly List<color> _colors = new();
    private readonly List<byte> _styles = new();
    private readonly List<float> _sizes = new();
    private readonly List<color> _marks = new();
    private readonly List<bool> _nobr = new();
    private readonly List<string?> _sprites = new();
    private readonly List<RichTextParser.AlignMark> _alignMarks = new();
    private readonly List<RichTextParser.LineHeightMark> _lineHeightMarks = new();
    private readonly List<RichTextParser.FontMark> _fontMarks = new();
    private readonly List<(int Index, FontSet? Font)> _resolvedFontMarks = new();
    private System.Collections.Generic.IReadOnlyList<(int Index, FontSet? Font)>? _fontMarksArg;
    private FontSetGroup? _fontGroup;
    private SpriteSet? _spriteSet;
    // Sprite names resolved to (texture, uv) on the MAIN thread; the worker emit reads only this snapshot,
    // never the live slot tree (SpriteSet.Get walks child slots, which is main-thread only). -xlinka
    private readonly Dictionary<string, (IAssetProvider<TextureAsset> texture, Rect uv)> _resolvedSprites = new();
    private System.Collections.Generic.IReadOnlyList<float>? _sizeArg;
    private System.Collections.Generic.IReadOnlyList<bool>? _nobrArg;
    private readonly StringBuilder _richTextBuilder = new();
    private float _layoutPreferredWidth;
    private float _layoutPreferredHeight;
    // Font metrics snapshotted on the main thread (PreGraphicsCompute). The worker positions
    // glyphs from these instead of calling the font hook, which hits Godot's font server. - xlinka
    private float _ascent;
    private float _lineHeight;
    private float _rawLineHeight; // font line height WITHOUT _lineSpacing, for <line-height> overrides

    // Shared shaping engine with built-in result caching (see TextShaper).
    private readonly TextShaper _shaper = new();

    public Text()
    {
        Content = new Sync<string>(this, string.Empty);
        Font = new AssetRef<FontSet>(this);
        Sprites = new SyncRef<SpriteSet>(this);
        Fonts = new SyncRef<FontSetGroup>(this);
        Material = new AssetRef<MaterialAsset>(this);
        Size = new Sync<float>(this, 16f);
        Color = new Sync<color>(this, Lumora.Core.Math.color.White);
        HorizontalAlignment = new Sync<TextHorizontalAlignment>(this, TextHorizontalAlignment.Left);
        VerticalAlignment = new Sync<TextVerticalAlignment>(this, TextVerticalAlignment.Top);
        LineSpacing = new Sync<float>(this, 1f);
        WordWrap = new Sync<bool>(this, true);
        RichText = new Sync<bool>(this, false);
        CaretPosition = new Sync<int>(this, -1);
        SelectionStart = new Sync<int>(this, -1);
        CaretColor = new Sync<color>(this, Lumora.Core.Math.color.White);
        SelectionColor = new Sync<color>(this, new Lumora.Core.Math.color(0.25f, 0.5f, 1f, 0.4f));
        AutoSizeHorizontal = new Sync<bool>(this, false);
        AutoSizeVertical = new Sync<bool>(this, false);
        AutoSizeMin = new Sync<float>(this, 6f);
    }

    public override bool RequiresPreGraphicsCompute => true;
    public float? MinWidth => 0f;
    public float? PreferredWidth => _layoutPreferredWidth;
    public float? FlexibleWidth => 0f;
    public float? MinHeight => _layoutPreferredHeight;
    public float? PreferredHeight => _layoutPreferredHeight;
    public float? FlexibleHeight => 0f;
    public float? Area => null;
    public int Priority => 0;
    public LayoutMetric ChangedMetrics { get; private set; }

    // Signature of the LAYOUT-affecting fields at the last flag, so a pure color change (selection
    // highlight, hover tint) re-meshes the glyphs in place instead of triggering a full relayout.
    private int _layoutSignature = -1;

    private int ComputeLayoutSignature()
    {
        var hash = new System.HashCode();
        hash.Add(Content.Value);
        hash.Add(Size.Value);
        hash.Add(HorizontalAlignment.Value);
        hash.Add(VerticalAlignment.Value);
        hash.Add(LineSpacing.Value);
        hash.Add(WordWrap.Value);
        hash.Add(RichText.Value);
        hash.Add(AutoSizeHorizontal.Value);
        hash.Add(AutoSizeVertical.Value);
        hash.Add(AutoSizeMin.Value);
        hash.Add(CaretPosition.Value);
        hash.Add(SelectionStart.Value);
        hash.Add(Font.Target);
        hash.Add(Material.Target);
        return hash.ToHashCode();
    }

    protected override void FlagChanges(RectTransform rect)
    {
        int signature = ComputeLayoutSignature();
        if (signature == _layoutSignature)
        {
            // Only a color/visual field changed - re-emit this glyph mesh, don't relayout.
            rect.MarkGraphicDirty();
            return;
        }
        _layoutSignature = signature;
        ChangedMetrics = LayoutMetric.MinWidth | LayoutMetric.PreferredWidth | LayoutMetric.MinHeight | LayoutMetric.PreferredHeight;
        rect.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
        _content = Content.Value ?? string.Empty;
        _font = Font.Target;
        _spriteSet = Sprites.Target; // snapshot on the main thread; ComputeGraphic runs on the worker
        _fontGroup = Fonts.Target;
        _material = Material.Target;
        _size = Size.Value;
        _color = Color.Value;
        _hAlign = HorizontalAlignment.Value;
        _vAlign = VerticalAlignment.Value;
        _lineSpacing = LineSpacing.Value;
        _wrap = WordWrap.Value;
        _richText = RichText.Value;
        _caretPos = CaretPosition.Value;
        _selStart = SelectionStart.Value;
        _caretColor = CaretColor.Value;
        _selColor = SelectionColor.Value;
        _autoH = AutoSizeHorizontal.Value;
        _autoV = AutoSizeVertical.Value;
        _autoMin = AutoSizeMin.Value;

        // Strip inline tags into _shapedText (+ per-char _colors) so shaping/layout is
        // identical to plain text; plain mode shapes Content directly.
        if (_richText)
        {
            RichTextParser.Parse(_content, _color, _richTextBuilder, _colors, _styles, _sizes, _marks, _sprites, _alignMarks, _lineHeightMarks, _fontMarks);
            _shapedText = _richTextBuilder.ToString();
            _sizeArg = HasSizeVariation() ? _sizes : null; // null keeps the shaper cache for uniform text
            _nobrArg = BuildNoBreak() ? _nobr : null; // null keeps the shaper cache when nothing is nobr
            ResolveSprites();
            ResolveFontMarks();
        }
        else
        {
            _shapedText = _content;
            _sizeArg = null;
            _nobrArg = null;
            _fontMarksArg = null;
            _sprites.Clear();
            _resolvedSprites.Clear();
            _alignMarks.Clear();
            _lineHeightMarks.Clear();
            _fontMarks.Clear();
            _resolvedFontMarks.Clear();
        }
    }

    public override ValueTask PreGraphicsCompute()
    {
        var fontSet = _font?.Asset;
        if (fontSet == null || !fontSet.IsValid)
        {
            _ascent = _size * 0.8f;
            _lineHeight = _size;
            _rawLineHeight = _size;
            return default;
        }

        var rect = RectTransform?.LocalComputeRect ?? default;

        // Auto-size: shrink _size to fit the rect BEFORE rasterizing/shaping, so glyphs are requested and
        // metrics read at the final size. _size is the per-cycle working copy (reset from Size each compute),
        // so fitting it here doesn't accumulate across frames.
        if ((_autoH || _autoV) && !string.IsNullOrEmpty(_shapedText))
            _size = ComputeFittedSize(fontSet, rect.width, rect.height);

        // request rasterization for every codepoint we're about to draw - xlinka
        TextShaper.RequestGlyphs(fontSet, _shapedText, _size, _fontMarksArg);

        // Shape and read font metrics HERE, on the main thread, and do NOT move this to the worker no
        // matter how tempting. ComputeGraphic runs on the canvas worker, and the font hook
        // (GetAscent/GetLineHeight/GetKerning) calls Godot's font server, which is not thread safe. I
        // learned this the hard way: off-main it hands back garbage metrics and text randomly
        // collapses/vanishes for a frame or two, which is an absolute nightmare to repro. The worker
        // only positions the already-shaped glyphs. - xlinka
        float wrapWidth = _wrap && rect.width > 0f ? rect.width : 0f;
        _shaper.Shape(fontSet, _shapedText, _size, _wrap, wrapWidth, _sizeArg, _nobrArg, _fontMarksArg);

        _ascent = fontSet.GetAscent(_size);
        _rawLineHeight = fontSet.GetLineHeight(_size);
        _lineHeight = _rawLineHeight * _lineSpacing;
        if (_lineHeight <= 0f)
            _lineHeight = _size;
        if (_rawLineHeight <= 0f)
            _rawLineHeight = _size;

        return default;
    }

    public override void ComputeGraphic(GraphicsChunk.RenderData renderData)
    {
        if (RectTransform == null) return;
        if (string.IsNullOrEmpty(_shapedText)) return;

        var fontSet = _font?.Asset;
        if (fontSet == null || !fontSet.IsValid) return;

        var mesh = renderData.Mesh;

        PrepareMesh(mesh);
        LayoutAndEmit(renderData, mesh);
    }

    public override bool IsPointInside(in float2 point)
        => RectTransform?.LocalComputeRect.Contains(point) ?? false;

    public void ClearChangedMetrics()
    {
        ChangedMetrics = LayoutMetric.None;
    }

    public void EnsureValidMetrics(LayoutDirection direction)
    {
        var fontSet = _font?.Asset;
        if (fontSet == null || !fontSet.IsValid || string.IsNullOrEmpty(_shapedText))
        {
            _layoutPreferredWidth = 0f;
            _layoutPreferredHeight = 0f;
            return;
        }

        var rect = RectTransform?.LocalComputeRect ?? default;
        float wrapWidth = _wrap && rect.width > 0f ? rect.width : 0f;
        _shaper.Shape(fontSet, _shapedText, _size, _wrap, wrapWidth, _sizeArg, _nobrArg, _fontMarksArg);

        float lineHeight = fontSet.GetLineHeight(_size) * _lineSpacing;
        if (lineHeight <= 0f)
        {
            lineHeight = _size;
        }

        float totalHeight = 0f;
        var measuredLines = _shaper.Lines;
        for (int i = 0; i < measuredLines.Count; i++)
            totalHeight += lineHeight * measuredLines[i].MaxSizeScale;

        // An auto-sized axis adapts to whatever rect it's given, so it reports no preferred size to layout
        // (driving the natural size would fight the fit).
        _layoutPreferredWidth = _autoH ? 0f : _shaper.MaxLineWidth;
        _layoutPreferredHeight = _autoV ? 0f : totalHeight;
    }

    // Largest size in [AutoSizeMin, Size] whose shaped text fits the rect on the enabled axis/axes. Shapes
    // a few candidates (the shaper caches by size); the slight under-shoot helps converge when word-wrap
    // makes height non-linear in size. -xlinka
    private float ComputeFittedSize(FontSet fontSet, float availW, float availH)
    {
        float baseSize = _size;
        float min = _autoMin > 0f ? _autoMin : 1f;
        if (min > baseSize)
            min = baseSize;
        float size = baseSize;

        for (int iter = 0; iter < 6; iter++)
        {
            float wrapW = _wrap && availW > 0f ? availW : 0f;
            _shaper.Shape(fontSet, _shapedText, size, _wrap, wrapW, _sizeArg, _nobrArg, _fontMarksArg);

            float textW = _shaper.MaxLineWidth;
            float lh = fontSet.GetLineHeight(size) * _lineSpacing;
            if (lh <= 0f)
                lh = size;
            float textH = 0f;
            var lines = _shaper.Lines;
            for (int i = 0; i < lines.Count; i++)
                textH += lh * lines[i].MaxSizeScale;

            float ratio = 1f;
            if (_autoH && availW > 0f && textW > availW)
                ratio = System.Math.Min(ratio, availW / textW);
            if (_autoV && availH > 0f && textH > availH)
                ratio = System.Math.Min(ratio, availH / textH);
            if (ratio >= 0.999f)
                break;

            float next = System.Math.Max(min, size * ratio * 0.98f);
            if (next >= size - 0.01f)
            {
                size = next;
                break;
            }
            size = next;
        }

        return System.Math.Clamp(size, min, baseSize);
    }

    public LayoutMetric FilterChangedMetrics(LayoutMetric metrics)
    {
        return metrics;
    }

    public void LayoutRectWidthChanged()
    {
        ChangedMetrics |= LayoutMetric.MinHeight | LayoutMetric.PreferredHeight;
    }

    public void LayoutRectHeightChanged()
    {
    }

    private void LayoutAndEmit(GraphicsChunk.RenderData renderData, PhosMesh mesh)
    {
        var rect = RectTransform!.LocalComputeRect;
        // Shaping + metrics were done on the main thread in PreGraphicsCompute; the worker only
        // positions the already-shaped glyphs (no font-server calls off-main). - xlinka
        var lines = _shaper.Lines;
        if (lines.Count == 0) return;

        float ascent = _ascent;

        // Lines can differ in height when rich-text <size> is used (line height scales with the tallest glyph),
        // or when a <line-height> override is active on the line; uniform text has MaxSizeScale == 1. -xlinka
        float blockHeight = 0f;
        for (int i = 0; i < lines.Count; i++)
            blockHeight += LineHeightFor(lines[i]) * lines[i].MaxSizeScale;

        float blockTop = _vAlign switch
        {
            TextVerticalAlignment.Middle => rect.yMin + (rect.height + blockHeight) * 0.5f,
            TextVerticalAlignment.Bottom => rect.yMin + blockHeight,
            _ => rect.yMax,
        };

        // Highlight backgrounds behind marked runs (rich-text <mark>), under everything else.
        if (_richText)
            EmitMarkBackgrounds(renderData, rect, lines, blockTop);

        // Selection (behind text) + caret, when an editor is driving this text.
        if (_caretPos >= 0)
            EmitEditingVisuals(renderData, rect, lines, blockTop);

        float yCursor = blockTop;
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            float lineH = LineHeightFor(line) * line.MaxSizeScale;
            var lineAlign = LineAlign(line);
            float penX = AlignLineStart(rect, line.Width, lineAlign);
            float penY = yCursor - ascent * line.MaxSizeScale;

            // Justify spreads the leftover width across word gaps on every line except the last (and never the
            // line that ends a paragraph). justifyOffset accumulates as we cross each gap. -xlinka
            float justifyPerGap = 0f;
            if (lineAlign == TextHorizontalAlignment.Justify && _wrap && lineIndex < lines.Count - 1 && !line.HardBreak)
            {
                int gaps = CountWordGaps(line);
                float extra = rect.width - line.Width;
                if (gaps > 0 && extra > 0f)
                    justifyPerGap = extra / gaps;
            }
            float justifyOffset = 0f;

            for (int i = 0; i < line.Glyphs.Count; i++)
            {
                var glyph = line.Glyphs[i];
                if (justifyPerGap > 0f && i > 0 && IsWordGap(line, i))
                    justifyOffset += justifyPerGap;

                // Inline sprite placeholder: draw the sprite texture instead of a font glyph.
                if (glyph.Codepoint == TextShaper.SpriteGlyph)
                {
                    EmitSprite(renderData, glyph, penX + glyph.X + justifyOffset, penY, renderData.GeometryClipRect);
                    continue;
                }

                PhosTriangleSubmesh submesh;
                if (_material != null)
                {
                    submesh = renderData.GetSubmesh(_material);
                }
                else
                {
                    // Key by atlas identity; the shared text material is created at submit (main).
                    var atlas = glyph.Font.AtlasTexture;
                    if (atlas == null) continue;
                    submesh = renderData.GetSubmesh(null, atlas, GraphicsChunk.RenderData.TextAtlas);
                }
                byte style = GlyphStyle(glyph);
                EmitGlyph(renderData, submesh, mesh, penX + glyph.X + justifyOffset, penY + GlyphBaseline(style), glyph.Metrics, glyph.UV, GlyphColor(glyph), style, renderData.GeometryClipRect);
            }

            yCursor -= lineH;
        }
    }

    private static float AlignLineStart(in Rect rect, float lineWidth, TextHorizontalAlignment align)
    {
        return align switch
        {
            TextHorizontalAlignment.Center => rect.xMin + (rect.width - lineWidth) * 0.5f,
            TextHorizontalAlignment.Right => rect.xMax - lineWidth,
            _ => rect.xMin, // Left and Justify both start at the left; justify then spreads the slack
        };
    }

    private static int LineFirstSourceIndex(TextShaper.Line line)
        => line.Glyphs.Count > 0 ? line.Glyphs[0].SourceIndex : -1;

    // Alignment active on this line: the last <align> marker at or before the line's first char, else the
    // Text-wide default. Markers are stored in source order. -xlinka
    private TextHorizontalAlignment LineAlign(TextShaper.Line line)
    {
        if (_alignMarks.Count == 0)
            return _hAlign;
        int idx = LineFirstSourceIndex(line);
        byte a = 0;
        for (int i = 0; i < _alignMarks.Count && _alignMarks[i].Index <= idx; i++)
            a = _alignMarks[i].Align;
        return a > 0 ? (TextHorizontalAlignment)(a - 1) : _hAlign;
    }

    // Line height active on this line: a <line-height> override (multiplier of the raw font height) or the
    // default (font height x line spacing). -xlinka
    private float LineHeightFor(TextShaper.Line line)
    {
        if (_lineHeightMarks.Count == 0)
            return _lineHeight;
        int idx = LineFirstSourceIndex(line);
        float h = 0f;
        for (int i = 0; i < _lineHeightMarks.Count && _lineHeightMarks[i].Index <= idx; i++)
            h = _lineHeightMarks[i].Height;
        return h > 0f ? _rawLineHeight * h : _lineHeight;
    }

    // A word gap sits before glyph i when there's visible whitespace between it and the previous glyph - the
    // previous glyph's advance doesn't cover the distance to it. Reads off glyph spacing (not source chars), so
    // a non-breaking space (which IS a glyph) correctly produces no gap after it. -xlinka
    private bool IsWordGap(TextShaper.Line line, int i)
    {
        var prev = line.Glyphs[i - 1];
        float gap = line.Glyphs[i].X - prev.X - prev.Metrics.Advance;
        return gap > _size * 0.2f;
    }

    private int CountWordGaps(TextShaper.Line line)
    {
        int gaps = 0;
        for (int i = 1; i < line.Glyphs.Count; i++)
            if (IsWordGap(line, i)) gaps++;
        return gaps;
    }

    // Per-glyph color from the rich-text color map (rich mode), else the uniform _color.
    private color GlyphColor(in TextShaper.PositionedGlyph glyph)
    {
        if (_richText && glyph.SourceIndex >= 0 && glyph.SourceIndex < _colors.Count)
            return _colors[glyph.SourceIndex];
        return _color;
    }

    private byte GlyphStyle(in TextShaper.PositionedGlyph glyph)
    {
        if (_richText && glyph.SourceIndex >= 0 && glyph.SourceIndex < _styles.Count)
            return _styles[glyph.SourceIndex];
        return 0;
    }

    private bool HasSizeVariation()
    {
        for (int i = 0; i < _sizes.Count; i++)
            if (_sizes[i] != 1f)
                return true;
        return false;
    }

    // Fill _nobr from the parsed style flags: true where a char is inside a <nobr> run, so the shaper keeps
    // its spaces from becoming wrap points. Returns false when nothing is nobr, keeping the shaper cache warm. -xlinka
    private bool BuildNoBreak()
    {
        _nobr.Clear();
        bool any = false;
        for (int i = 0; i < _styles.Count; i++)
        {
            bool nb = (_styles[i] & RichTextParser.StyleNoBreak) != 0;
            _nobr.Add(nb);
            any |= nb;
        }
        return any;
    }

    // Per-char highlight color from the rich-text mark map (a<=0 = no highlight).
    private color GlyphMark(in TextShaper.PositionedGlyph glyph)
    {
        if (_richText && glyph.SourceIndex >= 0 && glyph.SourceIndex < _marks.Count)
            return _marks[glyph.SourceIndex];
        return default;
    }

    // Vertical offset (canvas Y-up) for sub/superscript glyphs: superscript rides above the baseline,
    // subscript drops below it. Fraction of the base size so it scales with the text. -xlinka
    private float GlyphBaseline(byte style)
    {
        if ((style & RichTextParser.StyleSup) != 0) return _size * 0.34f;
        if ((style & RichTextParser.StyleSub) != 0) return _size * -0.14f;
        return 0f;
    }

    private void EmitGlyph(GraphicsChunk.RenderData renderData, PhosTriangleSubmesh submesh, PhosMesh mesh, float penX, float penY, in GlyphMetrics metrics, in Rect uv, in color glyphColor, byte style, Rect? clipRect)
    {
        float xMin = penX + metrics.Offset.x;
        float yMin = penY + metrics.Offset.y;
        float xMax = xMin + metrics.Size.x;
        float yMax = yMin + metrics.Size.y;
        float uMin = uv.xMin;
        float uMax = uv.xMax;
        float vMin = uv.yMin;
        float vMax = uv.yMax;

        if (clipRect.HasValue)
        {
            var clip = clipRect.Value;
            if (xMax <= clip.xMin || xMin >= clip.xMax || yMax <= clip.yMin || yMin >= clip.yMax)
            {
                return;
            }

            if (xMin < clip.xMin)
            {
                float t = (clip.xMin - xMin) / (xMax - xMin);
                uMin = Lerp(uMin, uMax, t);
                xMin = clip.xMin;
            }
            if (xMax > clip.xMax)
            {
                float t = (clip.xMax - xMin) / (xMax - xMin);
                uMax = Lerp(uMin, uMax, t);
                xMax = clip.xMax;
            }
            if (yMin < clip.yMin)
            {
                float t = (clip.yMin - yMin) / (yMax - yMin);
                vMax = Lerp(vMax, vMin, t);
                yMin = clip.yMin;
            }
            if (yMax > clip.yMax)
            {
                float t = (clip.yMax - yMin) / (yMax - yMin);
                vMin = Lerp(vMax, vMin, t);
                yMax = clip.yMax;
            }
        }

        // Faux italic: shear x by skew*(vertexY - baseline) so the top leans right.
        float skew = (style & RichTextParser.StyleItalic) != 0 ? 0.2f : 0f;
        var col = glyphColor; // local copy: an 'in' param can't be captured by the local function

        void WriteQuad(float dx)
        {
            int v0 = mesh.VertexCount;
            mesh.IncreaseVertexCount(4);

            mesh.RawPositions[v0 + 0] = new float3(xMin + dx + skew * (yMax - penY), yMax, 0f);
            mesh.RawPositions[v0 + 1] = new float3(xMax + dx + skew * (yMax - penY), yMax, 0f);
            mesh.RawPositions[v0 + 2] = new float3(xMax + dx + skew * (yMin - penY), yMin, 0f);
            mesh.RawPositions[v0 + 3] = new float3(xMin + dx + skew * (yMin - penY), yMin, 0f);

            mesh.RawColors[v0 + 0] = col;
            mesh.RawColors[v0 + 1] = col;
            mesh.RawColors[v0 + 2] = col;
            mesh.RawColors[v0 + 3] = col;

            // Godot UV is Y-down (V=0 at the top of the texture, V=1 at the bottom). The atlas
            // stores glyphs with uv.yMin = top row and uv.yMax = bottom row, so the screen-TOP
            // vertex (yMax in Y-up world) needs the SMALLER V to land on atlas-top. - xlinka
            mesh.SetUV(0, v0 + 0, new float2(uMin, vMin));
            mesh.SetUV(0, v0 + 1, new float2(uMax, vMin));
            mesh.SetUV(0, v0 + 2, new float2(uMax, vMax));
            mesh.SetUV(0, v0 + 3, new float2(uMin, vMax));

            submesh.AddQuadAsTriangles(v0, v0 + 1, v0 + 2, v0 + 3);
        }

        WriteQuad(0f);
        // Faux bold (coverage atlas can't SDF-dilate): a second slightly-offset emit.
        if ((style & RichTextParser.StyleBold) != 0)
            WriteQuad(_size * 0.04f);

        // Underline / strikethrough as solid quads spanning the glyph advance.
        if ((style & (RichTextParser.StyleUnderline | RichTextParser.StyleStrike)) != 0)
        {
            float adv = metrics.Advance > 0f ? metrics.Advance : metrics.Size.x;
            float x0 = penX;
            float x1 = penX + adv;
            if ((style & RichTextParser.StyleUnderline) != 0)
            {
                float y = penY - _size * 0.10f;
                EmitSolidQuad(renderData, x0, y - _size * 0.05f, x1, y, glyphColor, clipRect);
            }
            if ((style & RichTextParser.StyleStrike) != 0)
            {
                float y = penY + _size * 0.26f;
                EmitSolidQuad(renderData, x0, y, x1, y + _size * 0.05f, glyphColor, clipRect);
            }
        }
    }

    // Selection rects + caret, emitted into a solid (untextured) submesh requested
    // BEFORE the glyph submeshes so they render behind the glyphs. Steady caret.
    // Draw a highlight quad behind each run of same-colored <mark> glyphs, per line, before the glyphs. -xlinka
    private void EmitMarkBackgrounds(GraphicsChunk.RenderData renderData,
        in Rect rect, System.Collections.Generic.IReadOnlyList<TextShaper.Line> lines, float blockTop)
    {
        if (lines.Count == 0 || _marks.Count == 0) return;
        var clip = renderData.GeometryClipRect;
        float top = blockTop;
        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            float lh = LineHeightFor(line) * line.MaxSizeScale;
            float ls = AlignLineStart(rect, line.Width, LineAlign(line));
            int gi = 0;
            while (gi < line.Glyphs.Count)
            {
                var mark = GlyphMark(line.Glyphs[gi]);
                if (mark.a <= 0f) { gi++; continue; }

                float startX = line.Glyphs[gi].X;
                float endX = (gi + 1 < line.Glyphs.Count) ? line.Glyphs[gi + 1].X : line.Width;
                int gj = gi + 1;
                while (gj < line.Glyphs.Count)
                {
                    var next = GlyphMark(line.Glyphs[gj]);
                    if (next.a <= 0f || !SameColor(next, mark)) break;
                    endX = (gj + 1 < line.Glyphs.Count) ? line.Glyphs[gj + 1].X : line.Width;
                    gj++;
                }
                EmitSolidQuad(renderData, ls + startX, top - lh, ls + endX, top, mark, clip);
                gi = gj;
            }
            top -= lh;
        }
    }

    private static bool SameColor(in color a, in color b)
        => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

    private void EmitEditingVisuals(GraphicsChunk.RenderData renderData,
        in Rect rect, System.Collections.Generic.IReadOnlyList<TextShaper.Line> lines, float blockTop)
    {
        if (lines.Count == 0) return;
        var clip = renderData.GeometryClipRect;

        if (_selStart >= 0 && _selStart != _caretPos)
        {
            int a = _selStart < _caretPos ? _selStart : _caretPos;
            int b = _selStart < _caretPos ? _caretPos : _selStart;
            float top = blockTop;
            for (int li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                float lh = LineHeightFor(line) * line.MaxSizeScale;
                float startX = -1f;
                float endX = -1f;
                for (int gi = 0; gi < line.Glyphs.Count; gi++)
                {
                    var g = line.Glyphs[gi];
                    if (g.SourceIndex >= a && g.SourceIndex < b)
                    {
                        if (startX < 0f) startX = g.X;
                        endX = (gi + 1 < line.Glyphs.Count) ? line.Glyphs[gi + 1].X : line.Width;
                    }
                }
                if (startX >= 0f && endX > startX)
                {
                    float ls = AlignLineStart(rect, line.Width, LineAlign(line));
                    EmitSolidQuad(renderData, ls + startX, top - lh, ls + endX, top, _selColor, clip);
                }
                top -= lh;
            }
        }

        TryGetCaretPos(lines, _caretPos, out int caretLine, out float caretX);
        float caretTop = blockTop;
        for (int li = 0; li < caretLine; li++)
            caretTop -= LineHeightFor(lines[li]) * lines[li].MaxSizeScale;
        float caretLh = LineHeightFor(lines[caretLine]) * lines[caretLine].MaxSizeScale;
        float lineStart = AlignLineStart(rect, lines[caretLine].Width, LineAlign(lines[caretLine]));
        float cx = lineStart + caretX;
        float w = _size * 0.06f;
        if (w < 1f) w = 1f;
        EmitSolidQuad(renderData, cx, caretTop - caretLh, cx + w, caretTop, _caretColor, clip);
    }

    private static void TryGetCaretPos(System.Collections.Generic.IReadOnlyList<TextShaper.Line> lines, int charIndex, out int lineIndex, out float localX)
    {
        for (int li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            for (int gi = 0; gi < line.Glyphs.Count; gi++)
            {
                if (line.Glyphs[gi].SourceIndex >= charIndex)
                {
                    lineIndex = li;
                    localX = line.Glyphs[gi].X;
                    return;
                }
            }
        }
        lineIndex = lines.Count - 1;
        localX = lines[lineIndex].Width;
    }

    // Inline sprites show their own colors (no tint), matching the reference's non-tintable inline glyphs. -xlinka
    private static readonly color SpriteTint = color.White;

    // Resolve each <font> marker's name to a FontSet on the main thread (walks the FontSetGroup's child slots),
    // so the shaper worker only reads resolved FontSet references. -xlinka
    private void ResolveFontMarks()
    {
        _resolvedFontMarks.Clear();
        for (int i = 0; i < _fontMarks.Count; i++)
        {
            var m = _fontMarks[i];
            FontSet? f = (m.Name != null && _fontGroup != null) ? _fontGroup.Get(m.Name) : null;
            _resolvedFontMarks.Add((m.Index, f));
        }
        _fontMarksArg = _resolvedFontMarks.Count > 0 ? _resolvedFontMarks : null;
    }

    // Resolve every inline sprite name to its (texture, uv) on the main thread, so the worker emit never
    // touches the live slot tree. Distinct names only. -xlinka
    private void ResolveSprites()
    {
        _resolvedSprites.Clear();
        if (_spriteSet == null)
            return;
        for (int i = 0; i < _sprites.Count; i++)
        {
            var name = _sprites[i];
            if (string.IsNullOrEmpty(name) || _resolvedSprites.ContainsKey(name))
                continue;
            var sprite = _spriteSet.Get(name);
            var tex = sprite?.Texture.Target;
            if (sprite != null && tex != null)
                _resolvedSprites[name] = (tex, sprite.UVRect.Value);
        }
    }

    // Draw the sprite named for this placeholder glyph as a textured quad at the reserved square, from the
    // main-thread snapshot. Draws nothing if the name wasn't resolved (no SpriteSet, or unknown name). -xlinka
    private void EmitSprite(GraphicsChunk.RenderData renderData, in TextShaper.PositionedGlyph glyph, float x, float y, Rect? clip)
    {
        if (glyph.SourceIndex < 0 || glyph.SourceIndex >= _sprites.Count)
            return;
        var name = _sprites[glyph.SourceIndex];
        if (name == null || !_resolvedSprites.TryGetValue(name, out var resolved))
            return;

        var m = glyph.Metrics;
        float x0 = x + m.Offset.x;
        float y0 = y + m.Offset.y;
        var rect = Rect.FromMinMax(new float2(x0, y0), new float2(x0 + m.Size.x, y0 + m.Size.y));
        var submesh = renderData.GetSubmesh(null, resolved.texture, GraphicsChunk.RenderData.ImageTexture);
        RawImage.GenerateImage(submesh.Mesh, submesh, rect, resolved.uv, null, false, in SpriteTint, clip);
    }

    private static void EmitSolidQuad(GraphicsChunk.RenderData renderData, float x0, float y0, float x1, float y1, in color c, Rect? clip)
    {
        if (x1 <= x0 || y1 <= y0) return;
        var submesh = renderData.GetSubmesh(null, null, GraphicsChunk.RenderData.ImageTexture);
        RawImage.GenerateImage(submesh.Mesh, submesh, Rect.FromMinMax(new float2(x0, y0), new float2(x1, y1)), Rect.UnitRect, null, false, in c, clip);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static void PrepareMesh(PhosMesh mesh)
    {
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);
    }
}
