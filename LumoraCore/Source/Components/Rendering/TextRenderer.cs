// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;
using Helio.UI;

namespace Lumora.Core.Components;

// Text in the world: nameplates, labels, indicators.
// Text is engine-generated geometry, not a platform widget: glyph quads are
// emitted into the PhosMesh (shared TextShaper + font atlas pipeline, same as
// Helio UI text) and rendered through the standard MeshRenderer/material
// chain with a depth-tested TextMaterial. The platform layer never knows
// "text" exists - it uploads a mesh like any other. Each peer builds its own
// local renderer slot; only the sync fields replicate. - xlinka
[ComponentCategory("Rendering")]
public class TextRenderer : ProceduralMesh, IPrimaryColorSource
{
    // Newlines start new lines. There is no wrap here, only the breaks you type.
    public readonly Sync<string> Text;

    // line height in metres
    public readonly Sync<float> Size;

    // vertex colour on every glyph
    public readonly Sync<color> Color;

    // Shaping and the glyph atlas both come from here. With no font this renders NOTHING.
    public readonly AssetRef<FontSet> Font;

    public readonly Sync<TextHorizontalAlignment> HorizontalAlign;
    public readonly Sync<TextVerticalAlignment> VerticalAlign;
    public readonly Sync<float> LineSpacing;

    // forwarded to the text material; transparent = no outline
    public readonly Sync<colorHDR> OutlineColor;

    // atlas texels, 0 = none; what makes floating text readable over anything
    public readonly Sync<float> OutlineThickness;

    // Metres past which this label stops drawing. 0 = draws at any distance, which is what a nameplate
    // wants. A sign bolted to a wall does not: the glyphs are sub-pixel long before you run out of world
    // to walk backwards into, and every one of them is still a mesh being drawn. Goes onto the child
    // renderer as a Godot visibility range with a fade band, so it dissolves rather than pops. -xlinka
    public readonly Sync<float> MaxViewDistance;

    public const float ViewDistanceFadeMargin = 3f;

    // Width and height of the laid-out text in metres, local space, filled in during meshing. Zero with no
    // text or no font. Fit a backing panel to this rather than guessing at a size.
    public float2 RenderedSize { get; private set; }

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

    // Multi-atlas glyph routing. Text that mixes scripts (Latin + CJK + emoji) pulls
    // glyphs from several font atlases, and a material binds exactly one atlas, so each
    // atlas needs its own submesh + material. _materialPool lives on the renderer slot
    // and grows to the high-water-mark of distinct atlases ever seen - it's reused every
    // regen so typing doesn't leak a component per keystroke. _atlasToSubmesh + the
    // ordered _regenAtlases are rebuilt each UpdateMeshData; submesh index i lines up
    // with _materialPool[i] and Materials[i]. _material stays pool[0]/Materials[0], so
    // ordinary single-atlas text is byte-identical to before. - xlinka
    private readonly List<TextMaterial> _materialPool = new();
    private readonly Dictionary<TextureAsset, PhosTriangleSubmesh> _atlasToSubmesh = new();
    private readonly List<TextureAsset> _regenAtlases = new();

    public TextRenderer()
    {
        Text = new Sync<string>(this, string.Empty);
        Size = new Sync<float>(this, 0.1f);
        Color = new Sync<color>(this, Lumora.Core.Math.color.White);
        Font = new AssetRef<FontSet>(this);
        HorizontalAlign = new Sync<TextHorizontalAlignment>(this, TextHorizontalAlignment.Center);
        VerticalAlign = new Sync<TextVerticalAlignment>(this, TextVerticalAlignment.Middle);
        LineSpacing = new Sync<float>(this, 1f);
        OutlineColor = new Sync<colorHDR>(this, new colorHDR(0f, 0f, 0f, 0f));
        OutlineThickness = new Sync<float>(this, 0f);
        MaxViewDistance = new Sync<float>(this, 0f);
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
        // Outline is a material parameter, not geometry - forward to the material, don't re-mesh.
        OutlineColor.OnChanged += _ => ApplyOutline();
        OutlineThickness.OnChanged += _ => ApplyOutline();
        MaxViewDistance.OnChanged += _ => ApplyViewDistance();
    }

    // Outline is a material parameter shared by every atlas material, so push it to the
    // whole pool (not just _material) when it changes. Safe when the pool is empty (the
    // renderer isn't built yet) - the loop just no-ops. - xlinka
    private void ApplyOutline()
    {
        for (int i = 0; i < _materialPool.Count; i++)
        {
            var m = _materialPool[i];
            if (m == null || m.IsDestroyed)
                continue;
            m.OutlineColor.Value = OutlineColor.Value;
            m.OutlineThickness.Value = OutlineThickness.Value;
        }
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
        _renderer.Material.Target = _material; // Materials[0] = _material (the first atlas)

        // Seed the pool with the primary material so pool index 0 == Materials[0]; any
        // extra atlases attach more materials lazily during meshing. - xlinka
        _materialPool.Clear();
        _materialPool.Add(_material);
        ApplyOutline();

        // A label is a flat sheet of glyph quads with an alpha-cut texture on it. In a shadow cascade that
        // is either nothing at all or a solid black rectangle where the text should be, and either way it
        // is a second pass over every glyph in the world for a silhouette nobody wants. -xlinka
        _renderer.ShadowCastMode.Value = ShadowCastMode.Off;
        ApplyViewDistance();
    }

    private void ApplyViewDistance()
    {
        if (_renderer == null || _renderer.IsDestroyed)
            return;
        float distance = MathF.Max(0f, MaxViewDistance.Value);
        float margin = distance > 0f ? ViewDistanceFadeMargin : 0f;
        if (_renderer.MaxViewDistance == distance && _renderer.ViewDistanceFadeMargin == margin)
            return;
        _renderer.MaxViewDistance = distance;
        _renderer.ViewDistanceFadeMargin = margin;
        _renderer.MarkChangeDirty();
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

        mesh.Clear(); // also empties Submeshes; per-atlas submeshes are created lazily below
        mesh.HasColors = true;
        mesh.SetHasUV(0, true);
        _atlasToSubmesh.Clear();
        _regenAtlases.Clear();

        var font = _fontSet;
        if (font == null || !font.IsValid || string.IsNullOrEmpty(_text))
        {
            RenderedSize = float2.Zero;
            return; // empty mesh (Clear left 0 submeshes); nothing renders
        }

        TextShaper.RequestGlyphs(font, _text, _size);
        _shaper.Shape(font, _text, _size, wrap: false, maxWidth: 0f);
        var lines = _shaper.Lines;
        if (lines.Count == 0)
        {
            RenderedSize = float2.Zero;
            return; // empty mesh; nothing renders
        }

        float ascent = font.GetAscent(_size);
        float lineHeight = font.GetLineHeight(_size) * _lineSpacing;
        if (lineHeight <= 0f) lineHeight = _size;

        float blockHeight = lineHeight * lines.Count;

        float maxLineWidth = 0f;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Width > maxLineWidth)
                maxLineWidth = lines[i].Width;
        RenderedSize = new float2(maxLineWidth, blockHeight);
        float blockTop = _vAlign switch
        {
            TextVerticalAlignment.Middle => blockHeight * 0.5f,
            TextVerticalAlignment.Bottom => blockHeight,
            _ => 0f,
        };

        // Route every glyph to the submesh for its own atlas, so glyphs from the font
        // fallback chain (CJK, emoji, symbols) render instead of being dropped. Each
        // distinct atlas gets a submesh + a material bound to it. - xlinka
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
                var atlas = glyph.Font?.AtlasTexture;
                if (atlas == null)
                    continue; // atlas not built yet; a CacheGeneration bump re-drives this

                var submesh = GetOrCreateSubmesh(mesh, atlas);
                EmitGlyph(mesh, submesh, penX + glyph.X, penY, glyph.Metrics, glyph.UV);
            }
        }

        BindAtlasMaterials();
    }

    private void EmitGlyph(PhosMesh mesh, PhosTriangleSubmesh submesh, float penX, float penY, in GlyphMetrics metrics, in Rect uv)
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

        submesh.AddQuadAsTriangles(v0, v0 + 1, v0 + 2, v0 + 3);
    }

    // Return the submesh batching glyphs for `atlas`, creating it on first use this
    // regen. Submeshes are added in first-seen order and recorded in _regenAtlases, so
    // submesh index i == _regenAtlases[i] == the material it binds. - xlinka
    private PhosTriangleSubmesh GetOrCreateSubmesh(PhosMesh mesh, TextureAsset atlas)
    {
        if (_atlasToSubmesh.TryGetValue(atlas, out var existing))
            return existing;

        var submesh = new PhosTriangleSubmesh(mesh);
        mesh.Submeshes.Add(submesh);
        _atlasToSubmesh[atlas] = submesh;
        _regenAtlases.Add(atlas);
        return submesh;
    }

    // Bind one material per atlas submesh, index-aligned: surface i renders with
    // Materials[i], whose DirectTexture is _regenAtlases[i]. The pool only grows, so
    // Materials may hold more entries than we used this regen - those trailing ones map
    // to no surface (the mesh has none past _regenAtlases.Count, and the hook clamps
    // surface->material), so they're harmless and we never have to trim the list. - xlinka
    private void BindAtlasMaterials()
    {
        // A join/decode can fire RegenerateMesh (via a Sync change during Decode) BEFORE
        // OnStart/EnsureLocalRenderer runs, so the renderer slot + pool don't exist yet.
        // Skip binding then - the geometry still builds, and the first post-start update
        // re-drives and binds. Mirrors the old UpdateAtlasBinding null-material guard, and
        // avoids mutating the data model (AttachComponent) mid-decode. - xlinka
        if (_rendererSlot == null || _rendererSlot.IsDestroyed || _materialPool.Count == 0)
            return;

        for (int i = 0; i < _regenAtlases.Count; i++)
        {
            var material = AcquireMaterial(i);
            if (material == null || material.IsDestroyed)
                continue;

            var atlas = _regenAtlases[i];
            material.DirectTexture = atlas;
            // Match the shader path to the atlas: an MSDF atlas (distance field in RGB, opaque alpha) must use
            // the median-reconstruction branch, or the coverage-in-alpha path fills the glyph quads solid. Set
            // per atlas because a fallback chain can mix MSDF and coverage atlases. - xlinka
            material.UseMSDF.Value = atlas.IsMSDF;
            material.PixelRange.Value = atlas.MsdfPixelRange;
            material.OutlineColor.Value = OutlineColor.Value;
            material.OutlineThickness.Value = OutlineThickness.Value;
            material.ForceUpdate();
        }
    }

    // Pooled material for submesh index `i`. Reuses an existing one (zero allocation on
    // the common keystroke path); otherwise attaches a new TextMaterial to the renderer
    // slot and appends a matching Materials entry, keeping pool[i] == Materials[i]. - xlinka
    private TextMaterial AcquireMaterial(int index)
    {
        if (index < _materialPool.Count)
            return _materialPool[index];

        var material = _rendererSlot.AttachComponent<TextMaterial>();
        _materialPool.Add(material);
        _renderer.Materials.Add().Target = material;
        return material;
    }

    protected override void ClearMeshData()
    {
        // Drop per-regen routing, but keep the material pool (reused next regen). - xlinka
        _atlasToSubmesh.Clear();
        _regenAtlases.Clear();
    }

    // set the text content
    public void SetText(string text)
    {
        Text.Value = text ?? string.Empty;
    }

    // The glyph colour is baked into the mesh's vertex colours, so the TextMaterial on the child
    // renderer slot is a white passthrough and reading THAT would hand back white for every label.
    // This field is the one that carries the colour. -xlinka
    public bool TryGetPrimaryColor(out colorHDR color)
    {
        color = Color.Value;
        return true;
    }
}
