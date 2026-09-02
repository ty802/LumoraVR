// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Edit-mode overlay for a WidgetGrid: faint cell lines that fade in while editing, plus three
// highlights the grid computes each frame: the cell under the pointer, the widget the pointer would
// pick up, and where a carried widget would land (green fits, red blocked). The lines sit behind the
// widgets, the highlights above them. Hairline alphas are low on purpose: the blend is linear, so a
// value a compositor would call subtle reads as a slab here. -xlinka
[ComponentCategory("Hidden")]
public sealed class WidgetGridEditVisual : UIComponent
{
    private const float AnimSpeed = 4f;
    private const float GridAlpha = 0.12f;
    private static readonly color CursorColor = new color(1f, 0.85f, 0.2f, 0.3f);
    private static readonly color HoverColor = new color(0.2f, 0.85f, 1f, 0.25f);
    private static readonly color FitsColor = new color(0.25f, 1f, 0.45f, 0.3f);
    private static readonly color BlockedColor = new color(1f, 0.3f, 0.3f, 0.3f);

    public readonly SyncRef<WidgetGrid> Grid;
    public readonly AssetRef<TextureAsset> CellTexture;

    private TiledRawImage? _tiles;
    private RectTransform? _tilesRect;
    private GridCellTextureProvider? _ownTexture;
    private Image? _cursor;
    private Image? _hover;
    private Image? _preview;
    private float _showLerp;

    public WidgetGridEditVisual()
    {
        Grid = new SyncRef<WidgetGrid>(this);
        CellTexture = new AssetRef<TextureAsset>(this);
    }

    public static WidgetGridEditVisual Setup(WidgetGrid grid)
    {
        var visual = grid.Slot.GetComponent<WidgetGridEditVisual>() ?? grid.Slot.AttachComponent<WidgetGridEditVisual>();
        visual.Grid.Target = grid;
        return visual;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        var grid = Grid.Target;
        bool edit = grid != null && !grid.IsDestroyed && grid.EditMode.Value;
        _showLerp = System.Math.Clamp(_showLerp + (edit ? delta : -delta) * AnimSpeed, 0f, 1f);

        UpdateTiles(grid);

        _cursor ??= Overlay("EditCursor", 1600L);
        _hover ??= Overlay("EditHover", 1500L);
        _preview ??= Overlay("EditPreview", 1550L);
        Show(_cursor, grid, edit ? grid!.CursorRect : null, CursorColor);
        Show(_hover, grid, edit ? grid!.HoverRect : null, HoverColor);
        Show(_preview, grid, edit ? grid!.PreviewRect : null, grid != null && grid.PreviewValid ? FitsColor : BlockedColor);
    }

    private void UpdateTiles(WidgetGrid? grid)
    {
        EnsureTiles();
        if (_tiles == null || _tiles.IsDestroyed)
            return;

        if (_showLerp <= 0.001f || grid == null)
        {
            _tiles.Enabled.Value = false;
            return;
        }
        _tiles.Enabled.Value = true;

        // Tile at the cell pitch and start half a gap before the first cell, so every line runs down
        // the middle of a gap instead of along a cell's edge. The texture is one texel per pixel of
        // pitch, which keeps its one-texel line at one pixel. -xlinka
        var pitch = grid.Pitch;
        var half = grid.Spacing.Value * 0.5f;
        _tiles.TileSize.Value = pitch;
        if (_ownTexture != null && !_ownTexture.IsDestroyed)
        {
            int texels = System.Math.Clamp((int)System.MathF.Round(pitch.x), 8, 256);
            if (_ownTexture.Size.Value != texels)
                _ownTexture.Size.Value = texels;
        }

        if (_tilesRect != null && !_tilesRect.IsDestroyed)
        {
            var inset = grid.Padding.Value + grid.CenteringOffset - half;
            _tilesRect.OffsetMin.Value = inset;
            _tilesRect.OffsetMax.Value = -inset;
        }

        _tiles.Tint.Value = new color(1f, 1f, 1f, GridAlpha * _showLerp);
    }

    private Image Overlay(string name, long order)
    {
        var slot = Slot.FindChild(name, recursive: false) ?? Slot.AddSlot(name);
        slot.OrderOffset.Value = order;
        _ = slot.GetComponent<RectTransform>() ?? slot.AttachComponent<RectTransform>();
        var image = slot.GetComponent<Image>() ?? slot.AttachComponent<Image>();
        image.Enabled.Value = false;
        return image;
    }

    private static void Show(Image image, WidgetGrid? grid, Rect? rect, in color tint)
    {
        if (image.IsDestroyed)
            return;
        if (grid == null || rect == null)
        {
            image.Enabled.Value = false;
            return;
        }
        var transform = image.RectTransform ?? image.Slot.GetComponent<RectTransform>();
        if (transform != null)
            WidgetGrid.ApplyTopDown(transform, rect.Value);
        if (image.Tint.Value != tint)
            image.Tint.Value = tint;
        image.Enabled.Value = true;
    }

    private void EnsureTiles()
    {
        if (_tiles != null && !_tiles.IsDestroyed)
            return;

        var slot = Slot.FindChild("EditGrid", recursive: false) ?? Slot.AddSlot("EditGrid");
        slot.OrderOffset.Value = -1000L; // behind the widgets

        var rect = slot.GetComponent<RectTransform>() ?? slot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
        _tilesRect = rect;

        _tiles = slot.GetComponent<TiledRawImage>() ?? slot.AttachComponent<TiledRawImage>();
        _tiles.InteractionTarget.Value = false;
        _tiles.Texture.Target = ResolveTexture();
        _tiles.Enabled.Value = false;
    }

    private IAssetProvider<TextureAsset> ResolveTexture()
    {
        if (CellTexture.Target != null)
            return CellTexture.Target;

        var provider = _ownTexture
            ?? Slot.FindChild("GridCellTex", recursive: false)?.GetComponent<GridCellTextureProvider>()
            ?? Slot.AddSlot("GridCellTex").AttachComponent<GridCellTextureProvider>()!;
        _ownTexture = provider;
        CellTexture.Target = provider;
        return provider;
    }
}
