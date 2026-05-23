// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Threading.Tasks;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Helio.UI;

// emits one textured quad per glyph laid out within the RectTransform rect.
// TODO - xlinka: line wrapping, word break, tab stops, kerning beyond pair table
public class Text : Graphic
{
    public readonly Sync<string> Content;
    public readonly AssetRef<FontAsset> Font;
    public readonly AssetRef<MaterialAsset> Material;
    public readonly Sync<float> Size;
    public readonly Sync<color> Color;
    public readonly Sync<TextHorizontalAlignment> HorizontalAlignment;
    public readonly Sync<TextVerticalAlignment> VerticalAlignment;
    public readonly Sync<float> LineSpacing;
    public readonly Sync<bool> WordWrap;

    private string _content = string.Empty;
    private IAssetProvider<FontAsset>? _font;
    private IAssetProvider<MaterialAsset>? _material;
    private float _size;
    private color _color;
    private TextHorizontalAlignment _hAlign;
    private TextVerticalAlignment _vAlign;
    private float _lineSpacing;
    private bool _wrap;
    private UITextMaterial? _textMaterial;
    private TextureAsset? _boundAtlas;

    public Text()
    {
        Content = new Sync<string>(this, string.Empty);
        Font = new AssetRef<FontAsset>(this);
        Material = new AssetRef<MaterialAsset>(this);
        Size = new Sync<float>(this, 16f);
        Color = new Sync<color>(this, Lumora.Core.Math.color.White);
        HorizontalAlignment = new Sync<TextHorizontalAlignment>(this, TextHorizontalAlignment.Left);
        VerticalAlignment = new Sync<TextVerticalAlignment>(this, TextVerticalAlignment.Top);
        LineSpacing = new Sync<float>(this, 1f);
        WordWrap = new Sync<bool>(this, true);
    }

    public override bool RequiresPreGraphicsCompute => true;

    protected override void FlagChanges(RectTransform rect)
    {
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
        var fontAsset = _font?.Asset;
        if (fontAsset == null || string.IsNullOrEmpty(_content)) return default;

        for (int i = 0; i < _content.Length; i++)
        {
            int cp = char.ConvertToUtf32(_content, i);
            if (char.IsHighSurrogate(_content[i])) i++;
            fontAsset.Hook?.RequestGlyph(cp, _size);
        }

        return default;
    }

    public override void ComputeGraphic(GraphicsChunk.RenderData renderData)
    {
        if (RectTransform == null) return;
        if (string.IsNullOrEmpty(_content)) return;

        var fontAsset = _font?.Asset;
        if (fontAsset == null || !fontAsset.IsValid) return;

        var atlasTexture = fontAsset.AtlasTexture;
        if (atlasTexture == null) return;

        var textMaterial = _material ?? EnsureTextMaterial(atlasTexture);
        var submesh = renderData.GetSubmesh(textMaterial);
        var mesh = renderData.Mesh;

        PrepareMesh(mesh);
        LayoutAndEmit(fontAsset, submesh, mesh);
    }

    public override bool IsPointInside(in float2 point)
        => RectTransform?.LocalComputeRect.Contains(point) ?? false;

    // Per-Text local UI_Text material with the atlas baked in. Atlas changes (font swap)
    // flip the DirectTexture and force the material asset to re-apply. - xlinka
    private UITextMaterial EnsureTextMaterial(TextureAsset atlas)
    {
        if (_textMaterial == null)
        {
            var matSlot = Slot.AddLocalSlot("TextMaterial");
            _textMaterial = matSlot.AttachComponent<UITextMaterial>();
        }

        if (!ReferenceEquals(_boundAtlas, atlas))
        {
            _textMaterial.DirectTexture = atlas;
            _boundAtlas = atlas;
            _textMaterial.ForceUpdate();
        }

        return _textMaterial;
    }

    private void LayoutAndEmit(FontAsset font, PhosTriangleSubmesh submesh, PhosMesh mesh)
    {
        var rect = RectTransform!.LocalComputeRect;
        // Top-align in Y-up: baseline sits one ascent below the top of the rect, so the
        // tallest glyphs (caps + accents) reach exactly rect.yMax. Using lineHeight here
        // (= ascent + descent) leaves a descender-sized gap above the text. - xlinka
        float ascent = font.GetAscent(_size) * _lineSpacing;
        float penY = rect.yMax - ascent;

        // TODO - xlinka: wrap + multi-line
        float lineWidth = MeasureLine(font, _content);
        float penX = AlignLineStart(rect, lineWidth);

        int prev = 0;
        for (int i = 0; i < _content.Length; i++)
        {
            int cp = char.ConvertToUtf32(_content, i);
            if (char.IsHighSurrogate(_content[i])) i++;

            if (!font.TryGetGlyph(cp, _size, out var metrics, out var uv))
            {
                penX += _size * 0.5f;
                prev = 0;
                continue;
            }

            if (prev != 0)
            {
                penX += font.GetKerning(prev, cp, _size);
            }

            EmitGlyph(submesh, mesh, penX, penY, metrics, uv);
            penX += metrics.Advance;
            prev = cp;
        }
    }

    private float MeasureLine(FontAsset font, string line)
    {
        float width = 0f;
        int prev = 0;
        for (int i = 0; i < line.Length; i++)
        {
            int cp = char.ConvertToUtf32(line, i);
            if (char.IsHighSurrogate(line[i])) i++;
            if (!font.TryGetGlyph(cp, _size, out var metrics, out _))
            {
                width += _size * 0.5f;
                prev = 0;
                continue;
            }
            if (prev != 0)
            {
                width += font.GetKerning(prev, cp, _size);
            }
            width += metrics.Advance;
            prev = cp;
        }
        return width;
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

    private void EmitGlyph(PhosTriangleSubmesh submesh, PhosMesh mesh, float penX, float penY, in GlyphMetrics metrics, in Rect uv)
    {
        float xMin = penX + metrics.Offset.x;
        float yMin = penY + metrics.Offset.y;
        float xMax = xMin + metrics.Size.x;
        float yMax = yMin + metrics.Size.y;

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
        mesh.SetUV(0, v0 + 0, new float2(uv.xMin, uv.yMin));
        mesh.SetUV(0, v0 + 1, new float2(uv.xMax, uv.yMin));
        mesh.SetUV(0, v0 + 2, new float2(uv.xMax, uv.yMax));
        mesh.SetUV(0, v0 + 3, new float2(uv.xMin, uv.yMax));

        submesh.AddQuadAsTriangles(v0, v0 + 1, v0 + 2, v0 + 3);
    }

    private static void PrepareMesh(PhosMesh mesh)
    {
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);
    }
}
