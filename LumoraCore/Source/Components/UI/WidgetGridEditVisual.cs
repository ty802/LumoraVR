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
    private static readonly color GridTint = new color(1f, 1f, 1f, 0.55f);

    public readonly SyncRef<WidgetGrid> Grid;
    public readonly AssetRef<TextureAsset> CellTexture;

    private TiledRawImage? _tiles;
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
            _tiles.TileSize.Value = grid.CellSize.Value;

        var tint = GridTint;
        tint.a *= _showLerp;
        _tiles.Tint.Value = tint;
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
