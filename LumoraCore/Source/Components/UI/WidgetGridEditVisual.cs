// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Edit-mode grid overlay for a <see cref="WidgetGrid"/>: a tiled grid-line
/// texture that fades in while the grid's <see cref="WidgetGrid.EditMode"/> is on,
/// so you can see the cells you're placing widgets into. Tiled one texel-cell per
/// grid cell (<see cref="WidgetGrid.CellSize"/>). Renders behind widget content.
/// </summary>
public sealed class WidgetGridEditVisual : UIComponent
{
    private const float AnimSpeed = 4f;
    private static readonly color GridTint = new color(1f, 1f, 1f, 0.72f);

    public readonly SyncRef<WidgetGrid> Grid;
    public readonly AssetRef<TextureAsset> CellTexture;

    private TiledRawImage? _tiles;
    private RectTransform? _tilesRect;
    private Image? _preview;
    private RectTransform? _previewRect;
    private GridCellTextureProvider? _ownTexture;
    private float _showLerp;

    public WidgetGridEditVisual()
    {
        Grid = new SyncRef<WidgetGrid>(this);
        CellTexture = new AssetRef<TextureAsset>(this);
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        var grid = Grid.Target;
        bool edit = grid != null && !grid.IsDestroyed && grid.EditMode.Value;
        _showLerp = System.Math.Clamp(_showLerp + (edit ? delta : -delta) * AnimSpeed, 0f, 1f);

        // The drag/resize destination preview runs every frame (independent of the grid-line fade).
        UpdatePreview(grid, edit);

        EnsureTiles();
        if (_tiles == null || _tiles.IsDestroyed)
            return;

        if (_showLerp <= 0.001f)
        {
            _tiles.Enabled.Value = false;
            return;
        }

        _tiles.Enabled.Value = true;

        if (grid != null)
        {
            // Tile at the full cell PITCH (cell + spacing) so lines land on real cell boundaries, and
            // inset the overlay to where cells actually start (padding + centering), matching CellAt /
            // ArrangeWidgets - otherwise the lines drift away from the drop cells. -xlinka
            var cell = grid.EffectiveCellSize;
            var spacing = grid.Spacing.Value;
            _tiles.TileSize.Value = new float2(cell.x + spacing.x, cell.y + spacing.y);

            if (_tilesRect != null && !_tilesRect.IsDestroyed)
            {
                var pad = grid.Padding.Value;
                var center = grid.CenteringOffset;
                _tilesRect.OffsetMin.Value = new float2(pad.x + center.x, pad.y + center.y);
                _tilesRect.OffsetMax.Value = new float2(-(pad.x + center.x), -(pad.y + center.y));
            }
        }

        var tint = GridTint;
        tint.a *= _showLerp;
        _tiles.Tint.Value = tint;
    }

    // Draw the drag/resize destination preview (green = will place, red = blocked) at the cell rect the
    // active handle stashed on the grid. Hidden when not editing or no drag is in progress. -xlinka
    private void UpdatePreview(WidgetGrid? grid, bool edit)
    {
        EnsurePreview();
        if (_preview == null || _preview.IsDestroyed || _previewRect == null)
            return;

        var pr = grid?.DragPreviewRect;
        if (!edit || grid == null || pr == null)
        {
            _preview.Enabled.Value = false;
            return;
        }

        var rect = pr.Value;
        var cell = grid.EffectiveCellSize;
        var spacing = grid.Spacing.Value;
        var pad = grid.Padding.Value;
        var center = grid.CenteringOffset;

        // Same cell -> pixel mapping as WidgetGrid.ArrangeWidgets, so the preview lands exactly on cells.
        float width = rect.Width * cell.x + (rect.Width - 1) * spacing.x;
        float height = rect.Height * cell.y + (rect.Height - 1) * spacing.y;
        float xMin = pad.x + center.x + rect.X * (cell.x + spacing.x);
        float yMax = -(pad.y + center.y + rect.Y * (cell.y + spacing.y));

        _previewRect.AnchorMin.Value = new float2(0f, 1f);
        _previewRect.AnchorMax.Value = new float2(0f, 1f);
        _previewRect.OffsetMin.Value = new float2(xMin, yMax - height);
        _previewRect.OffsetMax.Value = new float2(xMin + width, yMax);

        _preview.Tint.Value = grid.DragPreviewValid
            ? new color(0.25f, 1f, 0.45f, 0.35f)  // green - will place here
            : new color(1f, 0.3f, 0.3f, 0.35f);   // red - blocked
        _preview.Enabled.Value = true;
    }

    private void EnsurePreview()
    {
        if (_preview != null && !_preview.IsDestroyed)
            return;

        var slot = Slot.FindChild("DragPreview", recursive: false) ?? Slot.AddSlot("DragPreview");
        slot.OrderOffset.Value = 1500L; // above grid lines + widget content

        _previewRect = slot.GetComponent<RectTransform>() ?? slot.AttachComponent<RectTransform>();

        _preview = slot.GetComponent<Image>() ?? slot.AttachComponent<Image>();
        _preview.Enabled.Value = false;
    }

    /// <summary>Attach the edit overlay to a grid.</summary>
    public static WidgetGridEditVisual Setup(WidgetGrid grid)
    {
        var visual = grid.Slot.GetComponent<WidgetGridEditVisual>() ?? grid.Slot.AttachComponent<WidgetGridEditVisual>();
        visual.Grid.Target = grid;
        return visual;
    }

    private void EnsureTiles()
    {
        if (_tiles != null && !_tiles.IsDestroyed)
            return;

        var slot = Slot.FindChild("EditGrid", recursive: false) ?? Slot.AddSlot("EditGrid");
        // Behind the widgets (which are later children / default order).
        slot.OrderOffset.Value = -1000L;

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
