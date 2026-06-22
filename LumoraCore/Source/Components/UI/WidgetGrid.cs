// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class WidgetGrid : UIComponent
{
    public readonly Sync<float2> CellSize;
    public readonly Sync<float2> Spacing;
    public readonly Sync<float2> Padding;
    public readonly Sync<bool> EditMode;

    // CellCount mode: when both > 0 the grid is a fixed FixedColumns x FixedRows and the cell SIZE is
    // computed to fill the rect. When 0, cells stay the fixed CellSize and the grid dimensions are derived
    // from how many fit (CellSize mode). -xlinka
    public readonly Sync<int> FixedColumns;
    public readonly Sync<int> FixedRows;

    // Center the placed cell block within the rect's leftover space. -xlinka
    public readonly Sync<bool> CenterContent;

    public WidgetGrid()
    {
        CellSize = new Sync<float2>(this, new float2(64f, 64f));
        Spacing = new Sync<float2>(this, new float2(4f, 4f));
        Padding = new Sync<float2>(this, new float2(8f, 8f));
        EditMode = new Sync<bool>(this, false);
        FixedColumns = new Sync<int>(this, 0);
        FixedRows = new Sync<int>(this, 0);
        CenterContent = new Sync<bool>(this, true);
    }

    // Last integer grid dimensions we reflowed against; reflow only when the cell
    // count actually changes (not every frame the rect jitters). -xlinka
    private (int cols, int rows) _lastGridSize = (-1, -1);

    public override void OnStart()
    {
        base.OnStart();
        // Auto-attach the edit-mode grid overlay so it can never be forgotten (the grid owns its own edit
        // visual). Without this, toggling edit mode showed nothing because nobody ever called Setup. -xlinka
        WidgetGridEditVisual.Setup(this);
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        // One global UI-edit flag drives every grid (and popped-out panels). Turning
        // it on reveals the grid lines and makes widgets draggable.
        if (EditMode.Value != WidgetPanel.EditMode)
            EditMode.Value = WidgetPanel.EditMode;

        var size = GridSize;
        if (size != _lastGridSize)
        {
            _lastGridSize = size;
            Reflow();
        }

        ArrangeWidgets();
    }

    /// <summary>
    /// Pull a widget off the grid: re-host its preset as a standalone, grabbable
    /// panel in userspace (in front of the user) and remove it from the grid. The
    /// grid only knows the preset's runtime type, so the panel rebuilds the preset
    /// rather than moving the canvas subtree (fine for stateless readout widgets).
    /// </summary>
    public void PopOut(Widget widget)
    {
        if (widget == null || widget.IsDestroyed)
            return;

        var preset = widget.Slot.GetComponent<WidgetPreset>();
        var presetType = preset?.GetType();
        if (presetType == null)
            return;

        float3 position;
        floatQ rotation;
        var userRoot = Engine.Current?.WorldManager?.FocusedWorld?.LocalUser?.Root;
        if (userRoot?.HeadSlot != null)
        {
            // -Z (Backward) is the view direction; +Z spawns it behind the user.
            position = userRoot.HeadPosition + userRoot.HeadRotation * (float3.Backward * 0.7f) + float3.Down * 0.1f;
            rotation = userRoot.HeadRotation;
        }
        else
        {
            position = new float3(0f, 1.4f, -0.7f);
            rotation = floatQ.Identity;
        }

        WidgetPanel.Spawn(presetType, position, rotation);
        widget.Slot.Destroy();
    }

    public void ArrangeWidgets()
    {
        var cell = EffectiveCellSize;
        var spacing = Spacing.Value;
        var padding = Padding.Value;
        var center = CenteringOffset;
        bool edit = EditMode.Value;

        var widgets = new List<Widget>(Slot.GetComponentsInChildren<Widget>(false));
        foreach (var widget in widgets)
        {
            var rect = widget.RectTransform ?? widget.Slot.GetComponent<RectTransform>() ?? widget.Slot.AttachComponent<RectTransform>();
            int gridWidth = Max(1, widget.GridWidth.Value);
            int gridHeight = Max(1, widget.GridHeight.Value);
            float width = gridWidth * cell.x + (gridWidth - 1) * spacing.x;
            float height = gridHeight * cell.y + (gridHeight - 1) * spacing.y;
            float xMin = padding.x + center.x + widget.GridX.Value * (cell.x + spacing.x);
            float yMax = -(padding.y + center.y + widget.GridY.Value * (cell.y + spacing.y));

            rect.AnchorMin.Value = new float2(0f, 1f);
            rect.AnchorMax.Value = new float2(0f, 1f);
            rect.OffsetMin.Value = new float2(xMin, yMax - height);
            rect.OffsetMax.Value = new float2(xMin + width, yMax);

            var handle = widget.Slot.GetComponent<WidgetDragHandle>() ?? widget.Slot.AttachComponent<WidgetDragHandle>();
            handle.Grid.Target = this;
            handle.TargetWidget.Target = widget;
            handle.Interactable.Value = edit;

            // Bottom-right resize grip on its OWN small corner child slot, so dragging the grip resizes
            // while dragging the body still moves (the two handles don't fight). Edit-mode only; visual
            // styling of the grip is left to the render layer. -xlinka
            var gripSlot = widget.Slot.FindChildOrAdd("ResizeGrip");
            var gripRect = gripSlot.GetComponent<RectTransform>() ?? gripSlot.AttachComponent<RectTransform>();
            gripRect.AnchorMin.Value = new float2(1f, 0f);
            gripRect.AnchorMax.Value = new float2(1f, 0f);
            gripRect.OffsetMin.Value = new float2(-18f, 0f);
            gripRect.OffsetMax.Value = new float2(0f, 18f);
            var resize = gripSlot.GetComponent<WidgetResizeHandle>() ?? gripSlot.AttachComponent<WidgetResizeHandle>();
            resize.Grid.Target = this;
            resize.TargetWidget.Target = widget;
            resize.Interactable.Value = edit;

            // Visible edit chrome - these are non-interactive Graphics (not IUIInteractable), so they
            // never shadow the drag/resize handles. A cyan outline marks each widget as draggable, and a
            // solid grip marks the resize corner; both show only in edit mode. -xlinka
            var outlineSlot = widget.Slot.FindChildOrAdd("EditOutline");
            outlineSlot.OrderOffset.Value = 1000L; // draw over the widget content
            var outlineRect = outlineSlot.GetComponent<RectTransform>() ?? outlineSlot.AttachComponent<RectTransform>();
            outlineRect.AnchorMin.Value = float2.Zero;
            outlineRect.AnchorMax.Value = float2.One;
            outlineRect.OffsetMin.Value = float2.Zero;
            outlineRect.OffsetMax.Value = float2.Zero;
            var outline = outlineSlot.GetComponent<BorderedImage>() ?? outlineSlot.AttachComponent<BorderedImage>();
            outline.Tint.Value = new color(0f, 0f, 0f, 0f);            // transparent fill
            outline.BorderTint.Value = new color(0f, 0.8f, 1f, 0.9f);  // cyan border
            outline.BorderThickness.Value = 2f;
            outline.Enabled.Value = edit;

            var gripImage = gripSlot.GetComponent<Image>() ?? gripSlot.AttachComponent<Image>();
            gripImage.Tint.Value = new color(0f, 0.8f, 1f, 0.9f);
            gripImage.Enabled.Value = edit;
        }
    }

    // === 2D grid placement (collision-aware) ===
    // ArrangeWidgets above is the pure RENDER pass (visual rect from each widget's stored cell rect). The
    // methods below are the PLACEMENT pass: they decide a widget's GridX/Y/W/H without overlapping others,
    // via the unit-tested WidgetGridPlacement engine. Drag + add go through these so two widgets can never
    // land on the same cells. -xlinka

    /// <summary>Grid dimensions (columns, rows): fixed FixedColumns x FixedRows if both are set, else derived from how many fixed CellSize cells fit the rect.</summary>
    public (int cols, int rows) GridSize
    {
        get
        {
            int fc = FixedColumns.Value, fr = FixedRows.Value;
            if (fc > 0 && fr > 0)
                return (fc, fr);
            var r = RectTransform?.LocalComputeRect;
            if (r == null)
                return (0, 0);
            var cell = CellSize.Value;
            var spacing = Spacing.Value;
            var pad = Padding.Value;
            int cols = CountAlong(r.Value.width - 2f * pad.x, cell.x, spacing.x);
            int rows = CountAlong(r.Value.height - 2f * pad.y, cell.y, spacing.y);
            return (cols, rows);
        }
    }

    /// <summary>
    /// Per-cell pixel size used for layout: the fixed CellSize, or - in CellCount mode - the size computed
    /// so FixedColumns x FixedRows cells (plus spacing) exactly fill the rect. -xlinka
    /// </summary>
    public float2 EffectiveCellSize
    {
        get
        {
            int fc = FixedColumns.Value, fr = FixedRows.Value;
            var cell = CellSize.Value;
            if (fc <= 0 || fr <= 0)
                return cell;
            var r = RectTransform?.LocalComputeRect;
            if (r == null)
                return cell;
            var spacing = Spacing.Value;
            var pad = Padding.Value;
            float w = (r.Value.width - 2f * pad.x - (fc - 1) * spacing.x) / fc;
            float h = (r.Value.height - 2f * pad.y - (fr - 1) * spacing.y) / fr;
            return new float2(w > 1f ? w : 1f, h > 1f ? h : 1f);
        }
    }

    /// <summary>Pixel offset that centers the placed cell block in the rect's leftover space (zero when CenterContent is off or the grid fills the rect).</summary>
    public float2 CenteringOffset
    {
        get
        {
            if (!CenterContent.Value)
                return float2.Zero;
            var r = RectTransform?.LocalComputeRect;
            if (r == null)
                return float2.Zero;
            var (cols, rows) = GridSize;
            var cell = EffectiveCellSize;
            var spacing = Spacing.Value;
            var pad = Padding.Value;
            float gridW = cols * cell.x + Max(0, cols - 1) * spacing.x;
            float gridH = rows * cell.y + Max(0, rows - 1) * spacing.y;
            float ox = (r.Value.width - 2f * pad.x - gridW) * 0.5f;
            float oy = (r.Value.height - 2f * pad.y - gridH) * 0.5f;
            return new float2(ox > 0f ? ox : 0f, oy > 0f ? oy : 0f);
        }
    }

    // How many cells of `cell` (with `spacing` between them) fit in `usable`: n*cell + (n-1)*spacing <= usable.
    private static int CountAlong(float usable, float cell, float spacing)
    {
        float stride = cell + spacing;
        if (stride <= 0f || usable <= 0f)
            return 1;
        int n = (int)MathF.Floor((usable + spacing) / stride);
        return n < 1 ? 1 : n;
    }

    // Inverse of "n cells -> pixel extent": how many whole cells best match `extent` pixels. >= 1.
    private static int CellsForExtent(float extent, float cell, float spacing)
    {
        float stride = cell + spacing;
        if (stride <= 0f)
            return 1;
        int n = (int)MathF.Round((extent + spacing) / stride);
        return n < 1 ? 1 : n;
    }

    /// <summary>Grid cell (column, row) under a point in this grid's local space (row 0 = top).</summary>
    public (int col, int row) CellAt(float2 localPoint)
    {
        var r = RectTransform?.LocalComputeRect ?? default;
        var cell = EffectiveCellSize;
        var spacing = Spacing.Value;
        var pad = Padding.Value;
        var center = CenteringOffset;
        int col = (int)MathF.Floor((localPoint.x - r.xMin - pad.x - center.x) / (cell.x + spacing.x));
        int row = (int)MathF.Floor((r.yMax - localPoint.y - pad.y - center.y) / (cell.y + spacing.y));
        return (col, row);
    }

    // Cell rects of every widget except `exclude`, for collision tests.
    private List<GridRect> OccupiedRects(Widget? exclude)
    {
        var list = new List<GridRect>();
        foreach (var w in Slot.GetComponentsInChildren<Widget>(false))
        {
            if (ReferenceEquals(w, exclude) || w.IsDestroyed)
                continue;
            list.Add(new GridRect(w.GridX.Value, w.GridY.Value, Max(1, w.GridWidth.Value), Max(1, w.GridHeight.Value)));
        }
        return list;
    }

    // Footprints the widget will accept, in the order the placement engine should try them: its
    // last-placed size FIRST (stability across reflows), then its authored
    // footprint down to its minimum (largest area first). So a widget keeps its current size when it
    // still fits, can grow back toward its authored size when room opens, and shrinks to fit when tight
    // instead of failing to place. -xlinka
    private static List<(int w, int h)> PreferredCells(Widget widget)
    {
        var (mw, mh) = MinCells(widget);
        var (aw, ah) = AuthoredCells(widget);

        var sizes = new List<(int w, int h)>();

        // Last-placed size first, if it's within [min, authored].
        int cw = Max(1, widget.GridWidth.Value);
        int ch = Max(1, widget.GridHeight.Value);
        if (cw >= mw && cw <= aw && ch >= mh && ch <= ah)
            sizes.Add((cw, ch));

        // Then the authored footprint shrinking to min, biggest area first (ties keep the wider one).
        var rest = new List<(int w, int h)>();
        for (int w = aw; w >= mw; w--)
            for (int h = ah; h >= mh; h--)
                rest.Add((w, h));
        rest.Sort((a, b) =>
        {
            int byArea = (b.w * b.h).CompareTo(a.w * a.h);
            return byArea != 0 ? byArea : b.w.CompareTo(a.w);
        });
        foreach (var s in rest)
            if (!sizes.Contains(s))
                sizes.Add(s);

        return sizes;
    }

    // The widget's authored "intended" footprint: its captured PreferredGrid size if set, else its
    // current GridWidth/GridHeight (treated as authored until the first placement captures it).
    private static (int w, int h) AuthoredCells(Widget widget)
    {
        int aw = widget.PreferredGridWidth.Value > 0 ? widget.PreferredGridWidth.Value : Max(1, widget.GridWidth.Value);
        int ah = widget.PreferredGridHeight.Value > 0 ? widget.PreferredGridHeight.Value : Max(1, widget.GridHeight.Value);
        var (mw, mh) = MinCells(widget);
        return (Max(aw, mw), Max(ah, mh));
    }

    // The widget's minimum cell footprint, clamped so it never exceeds its authored footprint.
    private static (int w, int h) MinCells(Widget widget)
    {
        int aw = widget.PreferredGridWidth.Value > 0 ? widget.PreferredGridWidth.Value : Max(1, widget.GridWidth.Value);
        int ah = widget.PreferredGridHeight.Value > 0 ? widget.PreferredGridHeight.Value : Max(1, widget.GridHeight.Value);
        return (Clamp(widget.MinGridWidth.Value, 1, aw), Clamp(widget.MinGridHeight.Value, 1, ah));
    }

    /// <summary>
    /// Try to place <paramref name="widget"/> at grid cell (<paramref name="col"/>,<paramref name="row"/>)
    /// without overlapping other widgets. Honors the widget's footprint, growing/shrinking only to dodge
    /// collisions. Writes GridX/Y/Width/Height and returns true on success; leaves the widget put on failure.
    /// </summary>
    public bool TryPlace(Widget widget, int col, int row)
    {
        var placement = ComputePlacement(widget, col, row);
        if (!placement.HasValue)
            return false;
        ApplyPlacement(widget, placement.Value);
        return true;
    }

    /// <summary>Where a move to cell (col,row) would land (collision-aware), WITHOUT committing.</summary>
    public GridRect? ComputePlacement(Widget widget, int col, int row)
    {
        if (widget == null || widget.IsDestroyed)
            return null;
        var (cols, rows) = GridSize;
        var (mw, mh) = MinCells(widget);
        return WidgetGridPlacement.FindPlacement(col, row, cols, rows, PreferredCells(widget), mw, mh, OccupiedRects(widget));
    }

    // === Drag preview (destination highlight) ===
    // While a widget is dragged/resized in edit mode the handle stashes the cell rect it WOULD land in
    // here; WidgetGridEditVisual draws a green (valid) / red (blocked) overlay there, and the change
    // commits only on release - so you see a preview before the move. -xlinka
    public GridRect? DragPreviewRect { get; private set; }
    public bool DragPreviewValid { get; private set; }

    /// <summary>Preview a move to (col,row) without committing - stashes the destination rect for the overlay.</summary>
    public void PreviewPlacement(Widget widget, int col, int row)
    {
        var p = ComputePlacement(widget, col, row);
        DragPreviewRect = p;
        DragPreviewValid = p.HasValue;
    }

    /// <summary>Preview a resize-to-corner without committing.</summary>
    public void PreviewResize(Widget widget, int cornerCol, int cornerRow)
    {
        var p = ComputeResize(widget, cornerCol, cornerRow);
        DragPreviewRect = p;
        DragPreviewValid = p.HasValue;
    }

    /// <summary>Clear the drag preview (on release / drag end).</summary>
    public void ClearDragPreview()
    {
        DragPreviewRect = null;
        DragPreviewValid = false;
    }

    /// <summary>Auto-place a widget in the first free spot (used when adding a widget with no explicit cell).</summary>
    public bool AutoPlace(Widget widget)
    {
        if (widget == null || widget.IsDestroyed)
            return false;
        var (cols, rows) = GridSize;
        var (mw, mh) = MinCells(widget);
        var placement = WidgetGridPlacement.FindInsertion(
            0, 0, cols, rows, PreferredCells(widget), mw, mh, OccupiedRects(widget));
        if (!placement.HasValue)
            return false;
        ApplyPlacement(widget, placement.Value);
        return true;
    }

    /// <summary>
    /// Re-flow the grid after its cell dimensions change: keep every
    /// widget that still fits fully in-bounds without overlapping, and re-insert any that now fall off the
    /// edge or collide into the first free spot. Guarantees a valid, overlap-free layout when the container
    /// resizes (shrinks especially), instead of leaving widgets clipped past the edge. -xlinka
    /// </summary>
    public void Reflow()
    {
        var (cols, rows) = GridSize;
        if (cols <= 0 || rows <= 0)
            return;

        var widgets = new List<Widget>(Slot.GetComponentsInChildren<Widget>(false));
        var kept = new List<GridRect>();
        var displaced = new List<Widget>();

        // Keep widgets still valid where they are; everything else gets re-inserted below.
        foreach (var w in widgets)
        {
            if (w.IsDestroyed)
                continue;
            var rect = new GridRect(w.GridX.Value, w.GridY.Value, Max(1, w.GridWidth.Value), Max(1, w.GridHeight.Value));
            bool inBounds = rect.X >= 0 && rect.Y >= 0 && rect.Right <= cols && rect.Top <= rows;
            if (inBounds && !WidgetGridPlacement.IntersectsAny(in rect, kept))
                kept.Add(rect);
            else
                displaced.Add(w);
        }

        foreach (var w in displaced)
        {
            var placement = WidgetGridPlacement.FindInsertion(0, 0, cols, rows, PreferredCells(w), 1, 1, kept);
            if (!placement.HasValue)
                continue; // grid is full - leave it put; ArrangeWidgets will clamp the visual rect.
            ApplyPlacement(w, placement.Value);
            kept.Add(placement.Value);
        }
    }

    /// <summary>Place a widget at an explicit authored cell rect (e.g. parsed from "[X=0;Y=0;W=2;H=1]").</summary>
    public void PlaceAt(Widget widget, in GridRect rect)
    {
        if (widget == null || widget.IsDestroyed)
            return;
        ApplyPlacement(widget, rect);
    }

    /// <summary>Place a widget from an authored layout string like "[X=0;Y=0;W=2;H=1]". Returns false if unparseable.</summary>
    public bool PlaceFromString(Widget widget, string rectStr)
    {
        if (!GridRect.TryParse(rectStr, out var rect))
            return false;
        PlaceAt(widget, in rect);
        return true;
    }

    /// <summary>
    /// Resize a widget so its bottom-right corner reaches cell (<paramref name="cornerCol"/>,<paramref name="cornerRow"/>),
    /// collision-aware (shrinks to fit). A manual resize also becomes the widget's authored footprint so
    /// reflow remembers it. Returns true on success. -xlinka
    /// </summary>
    public bool ResizeTo(Widget widget, int cornerCol, int cornerRow)
    {
        var placement = ComputeResize(widget, cornerCol, cornerRow);
        if (!placement.HasValue)
            return false;
        // A deliberate resize redefines the authored footprint (so a later shrink can grow back to it).
        widget!.PreferredGridWidth.Value = placement.Value.Width;
        widget.PreferredGridHeight.Value = placement.Value.Height;
        ApplyPlacement(widget, placement.Value);
        return true;
    }

    /// <summary>
    /// Where a resize so the bottom-right corner reaches (cornerCol,cornerRow) would land - collision-aware
    /// (shrinks to fit) and negotiated through the widget's FitSize (min/max + allowed aspect ratios) -
    /// WITHOUT committing. -xlinka
    /// </summary>
    public GridRect? ComputeResize(Widget widget, int cornerCol, int cornerRow)
    {
        if (widget == null || widget.IsDestroyed)
            return null;
        var (cols, rows) = GridSize;
        int ox = widget.GridX.Value;
        int oy = widget.GridY.Value;
        int forcedW = Max(1, cornerCol - ox + 1);
        int forcedH = Max(1, cornerRow - oy + 1);

        // Negotiate the dragged footprint's pixel size through FitSize, then snap back to whole cells.
        var cell = EffectiveCellSize;
        var spacing = Spacing.Value;
        var offered = new float2(
            forcedW * cell.x + (forcedW - 1) * spacing.x,
            forcedH * cell.y + (forcedH - 1) * spacing.y);
        var fitted = widget.FitSize(offered);
        if (fitted.HasValue)
        {
            forcedW = CellsForExtent(fitted.Value.x, cell.x, spacing.x);
            forcedH = CellsForExtent(fitted.Value.y, cell.y, spacing.y);
        }

        return WidgetGridPlacement.FitForcedSize(ox, oy, cols, rows, forcedW, forcedH, OccupiedRects(widget));
    }

    private static void ApplyPlacement(Widget widget, in GridRect rect)
    {
        // Capture the authored footprint the first time we place, so later shrink-to-fit doesn't
        // permanently destroy it (the widget can grow back when room opens). -xlinka
        if (widget.PreferredGridWidth.Value <= 0)
        {
            widget.PreferredGridWidth.Value = Max(1, widget.GridWidth.Value);
            widget.PreferredGridHeight.Value = Max(1, widget.GridHeight.Value);
        }
        widget.GridX.Value = rect.X;
        widget.GridY.Value = rect.Y;
        widget.GridWidth.Value = rect.Width;
        widget.GridHeight.Value = rect.Height;
    }

    private static int Max(int a, int b) => a > b ? a : b;
    private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
}
