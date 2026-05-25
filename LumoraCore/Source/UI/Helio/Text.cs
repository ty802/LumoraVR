// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading.Tasks;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

// emits one textured quad per glyph laid out within the RectTransform rect.
// TODO - xlinka: rich text, overflow modes, kerning beyond pair table
public class Text : Graphic, ILayoutElement
{
    public readonly Sync<string> Content;
    public readonly AssetRef<FontSet> Font;
    public readonly AssetRef<MaterialAsset> Material;
    public readonly Sync<float> Size;
    public readonly Sync<color> Color;
    public readonly Sync<TextHorizontalAlignment> HorizontalAlignment;
    public readonly Sync<TextVerticalAlignment> VerticalAlignment;
    public readonly Sync<float> LineSpacing;
    public readonly Sync<bool> WordWrap;

    private string _content = string.Empty;
    private IAssetProvider<FontSet>? _font;
    private IAssetProvider<MaterialAsset>? _material;
    private float _size;
    private color _color;
    private TextHorizontalAlignment _hAlign;
    private TextVerticalAlignment _vAlign;
    private float _lineSpacing;
    private bool _wrap;
    private float _layoutPreferredWidth;
    private float _layoutPreferredHeight;
    private readonly Dictionary<TextureAsset, UITextMaterial> _textMaterials = new();
    private readonly List<LineLayout> _layoutLines = new();

    public Text()
    {
        Content = new Sync<string>(this, string.Empty);
        Font = new AssetRef<FontSet>(this);
        Material = new AssetRef<MaterialAsset>(this);
        Size = new Sync<float>(this, 16f);
        Color = new Sync<color>(this, Lumora.Core.Math.color.White);
        HorizontalAlignment = new Sync<TextHorizontalAlignment>(this, TextHorizontalAlignment.Left);
        VerticalAlignment = new Sync<TextVerticalAlignment>(this, TextVerticalAlignment.Top);
        LineSpacing = new Sync<float>(this, 1f);
        WordWrap = new Sync<bool>(this, true);
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

    protected override void FlagChanges(RectTransform rect)
    {
        ChangedMetrics = LayoutMetric.MinWidth | LayoutMetric.PreferredWidth | LayoutMetric.MinHeight | LayoutMetric.PreferredHeight;
        rect.MarkChangeDirty();
    }

    public override void PrepareCompute()
    {
        _content = Content.Value ?? string.Empty;
        _font = Font.Target;
        _material = Material.Target;
        _size = Size.Value;
        _color = Color.Value;
        _hAlign = HorizontalAlignment.Value;
        _vAlign = VerticalAlignment.Value;
        _lineSpacing = LineSpacing.Value;
        _wrap = WordWrap.Value;
    }

    public override ValueTask PreGraphicsCompute()
    {
        // request rasterization for every codepoint we're about to draw - xlinka
        var fontSet = _font?.Asset;
        if (fontSet == null || string.IsNullOrEmpty(_content)) return default;

        for (int i = 0; i < _content.Length; i++)
        {
            int cp = char.ConvertToUtf32(_content, i);
            if (char.IsHighSurrogate(_content[i])) i++;
            if (cp == '\r' || cp == '\n' || cp == '\t') continue;
            fontSet.RequestGlyph(cp, _size);
        }

        return default;
    }

    public override void ComputeGraphic(GraphicsChunk.RenderData renderData)
    {
        if (RectTransform == null) return;
        if (string.IsNullOrEmpty(_content)) return;

        var fontSet = _font?.Asset;
        if (fontSet == null || !fontSet.IsValid) return;

        var mesh = renderData.Mesh;

        PrepareMesh(mesh);
        LayoutAndEmit(fontSet, renderData, mesh);
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
        if (fontSet == null || !fontSet.IsValid || string.IsNullOrEmpty(_content))
        {
            _layoutPreferredWidth = 0f;
            _layoutPreferredHeight = 0f;
            return;
        }

        var rect = RectTransform?.LocalComputeRect ?? default;
        float wrapWidth = _wrap && rect.width > 0f ? rect.width : 0f;
        BuildLines(fontSet, wrapWidth);

        float width = 0f;
        for (int i = 0; i < _layoutLines.Count; i++)
        {
            if (_layoutLines[i].Width > width)
            {
                width = _layoutLines[i].Width;
            }
        }

        float lineHeight = fontSet.GetLineHeight(_size) * _lineSpacing;
        if (lineHeight <= 0f)
        {
            lineHeight = _size;
        }

        _layoutPreferredWidth = width;
        _layoutPreferredHeight = lineHeight * _layoutLines.Count;
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

    // Per-Text local UI_Text material with the atlas baked in. Atlas changes (font swap)
    // flip the DirectTexture and force the material asset to re-apply. - xlinka
    private UITextMaterial EnsureTextMaterial(TextureAsset atlas)
    {
        if (_textMaterials.TryGetValue(atlas, out var material))
        {
            return material;
        }

        var matSlot = Slot.AddLocalSlot("TextMaterial");
        material = matSlot.AttachComponent<UITextMaterial>();
        material.DirectTexture = atlas;
        material.ForceUpdate();
        _textMaterials[atlas] = material;
        return material;
    }

    private IAssetProvider<MaterialAsset>? GetMaterialForFont(FontAsset font)
    {
        if (_material != null)
        {
            return _material;
        }

        var atlas = font.AtlasTexture;
        return atlas != null ? EnsureTextMaterial(atlas) : null;
    }

    private void LayoutAndEmit(FontSet font, GraphicsChunk.RenderData renderData, PhosMesh mesh)
    {
        var rect = RectTransform!.LocalComputeRect;
        BuildLines(font, _wrap ? rect.width : 0f);
        if (_layoutLines.Count == 0) return;

        float ascent = font.GetAscent(_size);
        float lineHeight = font.GetLineHeight(_size) * _lineSpacing;
        if (lineHeight <= 0f) lineHeight = _size;

        float blockHeight = lineHeight * _layoutLines.Count;
        float blockTop = _vAlign switch
        {
            TextVerticalAlignment.Middle => rect.yMin + (rect.height + blockHeight) * 0.5f,
            TextVerticalAlignment.Bottom => rect.yMin + blockHeight,
            _ => rect.yMax,
        };

        for (int lineIndex = 0; lineIndex < _layoutLines.Count; lineIndex++)
        {
            var line = _layoutLines[lineIndex];
            float penX = AlignLineStart(rect, line.Width);
            float penY = blockTop - ascent - lineHeight * lineIndex;

            for (int i = 0; i < line.Glyphs.Count; i++)
            {
                var glyph = line.Glyphs[i];
                var material = GetMaterialForFont(glyph.Font);
                if (material == null) continue;

                var submesh = renderData.GetSubmesh(material);
                EmitGlyph(submesh, mesh, penX + glyph.X, penY, glyph.Metrics, glyph.UV, renderData.ClipRect);
            }
        }
    }

    private void BuildLines(FontSet font, float maxWidth)
    {
        _layoutLines.Clear();

        var line = new LineLayout();
        var word = new LineLayout();
        float pendingWhitespace = 0f;
        int prevWordCodepoint = 0;
        FontAsset? prevWordFont = null;

        for (int i = 0; i < _content.Length; i++)
        {
            int cp = char.ConvertToUtf32(_content, i);
            if (char.IsHighSurrogate(_content[i])) i++;
            if (cp == '\r') continue;

            if (cp == '\n')
            {
                FlushWord(ref line, ref word, ref pendingWhitespace, maxWidth);
                CommitLine(line);
                line = new LineLayout();
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
        CommitLine(line);
    }

    private void FlushWord(ref LineLayout line, ref LineLayout word, ref float pendingWhitespace, float maxWidth)
    {
        if (word.Width <= 0f && word.Glyphs.Count == 0) return;

        float spacer = line.Width > 0f ? pendingWhitespace : 0f;
        if (_wrap && maxWidth > 0f && line.Width > 0f && line.Width + spacer + word.Width > maxWidth)
        {
            CommitLine(line);
            line = new LineLayout();
            spacer = 0f;
        }

        AppendLayout(line, word, line.Width + spacer);
        word = new LineLayout();
        pendingWhitespace = 0f;
    }

    private float AlignLineStart(in Rect rect, float lineWidth)
    {
        return _hAlign switch
        {
            TextHorizontalAlignment.Center => rect.xMin + (rect.width - lineWidth) * 0.5f,
            TextHorizontalAlignment.Right => rect.xMax - lineWidth,
            _ => rect.xMin,
        };
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

    private void AppendCodepoint(FontSet font, LineLayout target, int codepoint, ref int prevCodepoint, ref FontAsset? prevFont)
    {
        float kerning = 0f;
        float advance;

        if (font.TryGetGlyph(codepoint, _size, out var metrics, out var uv, out var glyphFont) && glyphFont != null)
        {
            if (prevCodepoint != 0 && ReferenceEquals(prevFont, glyphFont))
            {
                kerning = font.GetKerning(glyphFont, prevCodepoint, codepoint, _size);
            }

            target.Glyphs.Add(new GlyphLayout(glyphFont, codepoint, metrics, uv, target.Width + kerning));
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

    private static void AppendLayout(LineLayout target, LineLayout source, float startX)
    {
        for (int i = 0; i < source.Glyphs.Count; i++)
        {
            var glyph = source.Glyphs[i];
            target.Glyphs.Add(glyph.WithX(startX + glyph.X));
        }

        target.Width = startX + source.Width;
    }

    private void CommitLine(LineLayout line)
    {
        _layoutLines.Add(line);
    }

    private void EmitGlyph(PhosTriangleSubmesh submesh, PhosMesh mesh, float penX, float penY, in GlyphMetrics metrics, in Rect uv, Rect? clipRect)
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

        int v0 = mesh.VertexCount;
        mesh.IncreaseVertexCount(4);

        mesh.RawPositions[v0 + 0] = new float3(xMin, yMax, 0f);
        mesh.RawPositions[v0 + 1] = new float3(xMax, yMax, 0f);
        mesh.RawPositions[v0 + 2] = new float3(xMax, yMin, 0f);
        mesh.RawPositions[v0 + 3] = new float3(xMin, yMin, 0f);

        mesh.RawColors[v0 + 0] = _color;
        mesh.RawColors[v0 + 1] = _color;
        mesh.RawColors[v0 + 2] = _color;
        mesh.RawColors[v0 + 3] = _color;

        // Godot UV is Y-down (V=0 at the top of the texture, V=1 at the bottom). The atlas
        // stores glyphs with uv.yMin = top row and uv.yMax = bottom row, so the screen-TOP
        // vertex (yMax in Y-up world) needs the SMALLER V to land on atlas-top. - xlinka
        mesh.SetUV(0, v0 + 0, new float2(uMin, vMin));
        mesh.SetUV(0, v0 + 1, new float2(uMax, vMin));
        mesh.SetUV(0, v0 + 2, new float2(uMax, vMax));
        mesh.SetUV(0, v0 + 3, new float2(uMin, vMax));

        submesh.AddQuadAsTriangles(v0, v0 + 1, v0 + 2, v0 + 3);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static void PrepareMesh(PhosMesh mesh)
    {
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);
    }

    private sealed class LineLayout
    {
        public readonly List<GlyphLayout> Glyphs = new();
        public float Width;
    }

    private readonly struct GlyphLayout
    {
        public readonly int Codepoint;
        public readonly FontAsset Font;
        public readonly GlyphMetrics Metrics;
        public readonly Rect UV;
        public readonly float X;

        public GlyphLayout(FontAsset font, int codepoint, in GlyphMetrics metrics, in Rect uv, float x)
        {
            Font = font;
            Codepoint = codepoint;
            Metrics = metrics;
            UV = uv;
            X = x;
        }

        public GlyphLayout WithX(float x) => new(Font, Codepoint, Metrics, UV, x);
    }
}
