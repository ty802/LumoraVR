// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;
using Helio.UI;

namespace Lumora.Core.Components;

/// <summary>
/// Renders text in 3D space (nameplates, labels, indicators).
/// </summary>
// Text is engine-generated geometry, not a platform widget: glyph quads are
// emitted into the PhosMesh (shared TextShaper + font atlas pipeline, same as
// Helio UI text) and rendered through the standard MeshRenderer/material
// chain with a depth-tested TextMaterial. The platform layer never knows
// "text" exists - it uploads a mesh like any other. Each peer builds its own
// local renderer slot; only the sync fields replicate. - xlinka
[ComponentCategory("Rendering")]
public class TextRenderer : ProceduralMesh
{
    /// <summary>
    /// The text to render. Newlines start new lines; size is line height in meters.
    /// </summary>
    public readonly Sync<string> Text;

    /// <summary>
    /// Line height in meters.
    /// </summary>
    public readonly Sync<float> Size;

    /// <summary>
    /// Vertex color applied to all glyphs.
    /// </summary>
    public readonly Sync<color> Color;

    /// <summary>
    /// Font set used for shaping and the glyph atlas.
    /// </summary>
    public readonly AssetRef<FontSet> Font;

    public readonly Sync<TextHorizontalAlignment> HorizontalAlign;
    public readonly Sync<TextVerticalAlignment> VerticalAlign;
    public readonly Sync<float> LineSpacing;

    private readonly TextShaper _shaper = new();

    // Shaped state captured on the engine thread for mesh emission.
    private string _text = string.Empty;
    private float _size;
    private color _color;
    private TextHorizontalAlignment _hAlign;
    private TextVerticalAlignment _vAlign;
    private float _lineSpacing;
    private FontSet? _fontSet;

    // Font arrival/atlas growth detection (assets load async).
    private FontSet? _watchedFont;
    private int _watchedGeneration = -1;

    private Slot _rendererSlot = null!;
    private MeshRenderer _renderer = null!;
    private TextMaterial _material = null!;
    private TextureAsset _assignedAtlas = null!;
    private PhosTriangleSubmesh? _submesh;

    public TextRenderer()
    {
        Text = new Sync<string>(this, string.Empty);
        Size = new Sync<float>(this, 0.1f);
        Color = new Sync<color>(this, Lumora.Core.Math.color.White);
        Font = new AssetRef<FontSet>(this);
        HorizontalAlign = new Sync<TextHorizontalAlignment>(this, TextHorizontalAlignment.Center);
        VerticalAlign = new Sync<TextVerticalAlignment>(this, TextVerticalAlignment.Middle);
        LineSpacing = new Sync<float>(this, 1f);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        SubscribeToChanges(Text);
        SubscribeToChanges(Size);
        SubscribeToChanges(Color);
        SubscribeToChanges(HorizontalAlign);
        SubscribeToChanges(VerticalAlign);
        SubscribeToChanges(LineSpacing);
    }

    public override void OnStart()
    {
        EnsureLocalRenderer();
        base.OnStart();
    }

    // Fonts load asynchronously and the atlas fills lazily; poll the asset
    // ref and atlas generation, regenerating when either moves. Cheap when
    // idle (two reference compares + an int). - xlinka
    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();

        var font = Font.Target?.Asset;
        int generation = font?.CacheGeneration ?? -1;
        if (!ReferenceEquals(font, _watchedFont) || generation != _watchedGeneration)
        {
            _watchedFont = font;
            _watchedGeneration = generation;
            RegenerateMesh();
        }
    }

    private void EnsureLocalRenderer()
    {
        if (_rendererSlot != null && !_rendererSlot.IsDestroyed)
            return;

        _rendererSlot = Slot.AddLocalSlot("TextRenderer");
        _material = _rendererSlot.AttachComponent<TextMaterial>();
        _renderer = _rendererSlot.AttachComponent<MeshRenderer>();
        _renderer.Mesh.Target = this;
        _renderer.Material.Target = _material;
    }

    protected override void PrepareAssetUpdateData()
    {
        _text = Text.Value ?? string.Empty;
        _size = Size.Value;
        _color = Color.Value;
        _hAlign = HorizontalAlign.Value;
        _vAlign = VerticalAlign.Value;
        _lineSpacing = LineSpacing.Value;
        _fontSet = Font.Target?.Asset;
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        uploadHint.SetAll();

        mesh.Clear();
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);
        _submesh = new PhosTriangleSubmesh(mesh);
        mesh.Submeshes.Add(_submesh);

        var font = _fontSet;
        if (font == null || !font.IsValid || string.IsNullOrEmpty(_text))
        {
            UpdateAtlasBinding(null!);
            return;
        }

        TextShaper.RequestGlyphs(font, _text, _size);
        _shaper.Shape(font, _text, _size, wrap: false, maxWidth: 0f);
        var lines = _shaper.Lines;
        if (lines.Count == 0)
        {
            UpdateAtlasBinding(null!);
            return;
        }

        float ascent = font.GetAscent(_size);
        float lineHeight = font.GetLineHeight(_size) * _lineSpacing;
        if (lineHeight <= 0f) lineHeight = _size;

        float blockHeight = lineHeight * lines.Count;
        float blockTop = _vAlign switch
        {
            TextVerticalAlignment.Middle => blockHeight * 0.5f,
            TextVerticalAlignment.Bottom => blockHeight,
            _ => 0f,
        };

        // One material per renderer: emit glyphs from the primary atlas only.
        // Fallback-chain glyphs from other atlases are skipped until per-atlas
        // submesh/material support is needed. - xlinka
        FontAsset? primaryFont = null;

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            float penX = _hAlign switch
            {
                TextHorizontalAlignment.Center => -line.Width * 0.5f,
                TextHorizontalAlignment.Right => -line.Width,
                _ => 0f,
            };
            float penY = blockTop - ascent - lineHeight * lineIndex;

            for (int i = 0; i < line.Glyphs.Count; i++)
            {
                var glyph = line.Glyphs[i];
                primaryFont ??= glyph.Font;
                if (!ReferenceEquals(glyph.Font, primaryFont))
                    continue;

                EmitGlyph(mesh, penX + glyph.X, penY, glyph.Metrics, glyph.UV);
            }
        }

        UpdateAtlasBinding(primaryFont?.AtlasTexture!);
    }

    private void EmitGlyph(PhosMesh mesh, float penX, float penY, in GlyphMetrics metrics, in Rect uv)
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

        // Godot UV is Y-down: the screen-TOP vertex samples the atlas-top row
        // (smaller V) - same convention as Helio Text glyph emission. - xlinka
        mesh.SetUV(0, v0 + 0, new float2(uv.xMin, uv.yMin));
        mesh.SetUV(0, v0 + 1, new float2(uv.xMax, uv.yMin));
        mesh.SetUV(0, v0 + 2, new float2(uv.xMax, uv.yMax));
        mesh.SetUV(0, v0 + 3, new float2(uv.xMin, uv.yMax));

        _submesh!.AddQuadAsTriangles(v0, v0 + 1, v0 + 2, v0 + 3);
    }

    private void UpdateAtlasBinding(TextureAsset atlas)
    {
        if (_material == null || _material.IsDestroyed)
            return;

        if (ReferenceEquals(_assignedAtlas, atlas))
            return;

        _assignedAtlas = atlas;
        _material.DirectTexture = atlas;
        _material.ForceUpdate();
    }

    protected override void ClearMeshData()
    {
        _submesh = null;
    }

    /// <summary>
    /// Set the text content.
    /// </summary>
    public void SetText(string text)
    {
        Text.Value = text ?? string.Empty;
    }
}
