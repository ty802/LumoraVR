// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

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

    // Shared shaping engine with built-in result caching (see TextShaper).
    private readonly TextShaper _shaper = new();

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
        if (fontSet != null)
            TextShaper.RequestGlyphs(fontSet, _content, _size);

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
        _shaper.Shape(fontSet, _content, _size, _wrap, wrapWidth);

        float lineHeight = fontSet.GetLineHeight(_size) * _lineSpacing;
        if (lineHeight <= 0f)
        {
            lineHeight = _size;
        }

        _layoutPreferredWidth = _shaper.MaxLineWidth;
        _layoutPreferredHeight = lineHeight * _shaper.Lines.Count;
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

    private IAssetProvider<MaterialAsset>? GetMaterialForFont(GraphicsChunk.RenderData renderData, FontAsset font)
    {
        if (_material != null)
        {
            return _material;
        }

        // Shared per-atlas material (owned by the chunk) so all text batches into one submesh
        // instead of one surface per Text component. - xlinka
        var atlas = font.AtlasTexture;
        return atlas != null ? renderData.GetSharedTextMaterial(atlas) : null;
    }

    private void LayoutAndEmit(FontSet font, GraphicsChunk.RenderData renderData, PhosMesh mesh)
    {
        var rect = RectTransform!.LocalComputeRect;
        _shaper.Shape(font, _content, _size, _wrap, _wrap && rect.width > 0f ? rect.width : 0f);
        var lines = _shaper.Lines;
        if (lines.Count == 0) return;

        float ascent = font.GetAscent(_size);
        float lineHeight = font.GetLineHeight(_size) * _lineSpacing;
        if (lineHeight <= 0f) lineHeight = _size;

        float blockHeight = lineHeight * lines.Count;
        float blockTop = _vAlign switch
        {
            TextVerticalAlignment.Middle => rect.yMin + (rect.height + blockHeight) * 0.5f,
            TextVerticalAlignment.Bottom => rect.yMin + blockHeight,
            _ => rect.yMax,
        };

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            float penX = AlignLineStart(rect, line.Width);
            float penY = blockTop - ascent - lineHeight * lineIndex;

            for (int i = 0; i < line.Glyphs.Count; i++)
            {
                var glyph = line.Glyphs[i];
                var material = GetMaterialForFont(renderData, glyph.Font);
                if (material == null) continue;

                var submesh = renderData.GetSubmesh(material);
                EmitGlyph(submesh, mesh, penX + glyph.X, penY, glyph.Metrics, glyph.UV, renderData.ClipRect);
            }
        }
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
}
