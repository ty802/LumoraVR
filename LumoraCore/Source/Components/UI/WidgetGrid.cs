// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public enum GridPointerPhase
{
    None,
    Begin,
    Stay,
    End,
}

// A cell grid that hosts widgets. Two jobs: lay every widget out from its stored cell rect, and run
// the edit gesture that moves widgets between grids. Editing is pick up, carry, put down, not a drag
// inside one grid: grabbing a widget in edit mode lifts it off as a standalone panel in the hand, the
// grid under the pointer previews where it would land (its preferred size on a tap, a custom size once
// the press is dragged out), and letting go over a grid puts it down there. Any grid takes it, the
// header strip included, so widgets travel between Home and the top bar. -xlinka
[ComponentCategory("Hidden")]
public class WidgetGrid : UIComponent
{
    public readonly Sync<float2> CellSize;
    public readonly Sync<float2> Spacing;
    public readonly Sync<float2> Padding;
    public readonly Sync<bool> EditMode;

    // Both set: a fixed FixedColumns x FixedRows whose cells stretch to fill the rect. Otherwise the
    // cells stay CellSize and the counts come from how many fit. -xlinka
    public readonly Sync<int> FixedColumns;
    public readonly Sync<int> FixedRows;

    // Center the placed cell block within the rect's leftover space. -xlinka
    public readonly Sync<bool> CenterContent;

    // The host styles a widget that lands here off a carried panel (fonts, pill chrome) before it
    // builds. Plain delegate: the dash is local userspace, nothing here replicates.
    public Action<WidgetPreset>? PlacedStyler;

    // Edit visuals, read by WidgetGridEditVisual every frame. Top-down pixel rects: x right and y down
    // from the grid rect's top-left corner, the space ArrangeWidgets lays widgets out in.
    public Rect? CursorRect { get; private set; }
    public Rect? HoverRect { get; private set; }
    public Rect? PreviewRect { get; private set; }
    public bool PreviewValid { get; private set; }

    // Last integer grid dimensions we reflowed against; reflow only when the cell count actually
    // changes, not every frame the rect jitters. -xlinka
    private (int cols, int rows) _lastGridSize = (-1, -1);

    // One gesture at a time: the panel being carried over this grid and the pointer carrying it.
    private WidgetPanel? _gesturePanel;
    private UIInteractionSource _gestureSource;
    private int _gesturePointerId;
    private float2? _dragOrigin;

    private readonly List<Widget> _arrangeBuffer = new();
    private readonly List<Widget> _hitBuffer = new();
    private int _arrangeSignature;
    private bool _hasArrangeSignature;

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

    public override void OnStart()
    {
        base.OnStart();
        // The grid owns its edit overlay and its pointer element, so neither can be forgotten by a host.
        WidgetGridEditVisual.Setup(this);
        _ = Slot.GetComponent<WidgetGridInteraction>() ?? Slot.AttachComponent<WidgetGridInteraction>();
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        // One global edit flag drives every grid and every popped-out panel.
        if (EditMode.Value != WidgetPanel.EditMode)
        {
            EditMode.Value = WidgetPanel.EditMode;
            if (!EditMode.Value)
            {
                ClearVisuals();
                ResetGesture();
            }
        }

        var size = GridSize;
        if (size.cols > 0 && size.rows > 0 && size != _lastGridSize)
        {
            _lastGridSize = size;
            Reflow();
        }

        ArrangeWidgets();
    }

    // LAYOUT

    // The arrange is idempotent: it recomputes every rect from the grid metrics and each widget's stored
    // cell. Re-running it on an unchanged grid rewrites the same numbers, so fold the inputs into a
    // signature and skip the pass when nothing moved. Callers that mutate a widget's cell outside the
    // sync members call InvalidateArrange. -xlinka
    public void InvalidateArrange()
    {
        _hasArrangeSignature = false;
    }

    public void ArrangeWidgets()
    {
        var cell = EffectiveCellSize;
        var spacing = Spacing.Value;
        var origin = Padding.Value + CenteringOffset;

        _arrangeBuffer.Clear();
        Slot.GetComponentsInChildren(_arrangeBuffer, false);

        int signature = 17;
        unchecked
        {
            signature = signature * 31 + cell.GetHashCode();
            signature = signature * 31 + spacing.GetHashCode();
            signature = signature * 31 + origin.GetHashCode();
            signature = signature * 31 + _arrangeBuffer.Count;
            for (int i = 0; i < _arrangeBuffer.Count; i++)
            {
                var w = _arrangeBuffer[i];
                signature = signature * 31 + w.ReferenceID.GetHashCode();
                signature = signature * 31 + w.GridX.Value;
                signature = signature * 31 + w.GridY.Value;
                signature = signature * 31 + w.GridWidth.Value;
                signature = signature * 31 + w.GridHeight.Value;
            }
        }

        if (_hasArrangeSignature && signature == _arrangeSignature)
            return;
        _arrangeSignature = signature;
        _hasArrangeSignature = true;

        foreach (var widget in _arrangeBuffer)
        {
            var rect = widget.RectTransform ?? widget.Slot.GetComponent<RectTransform>() ?? widget.Slot.AttachComponent<RectTransform>();
            ApplyTopDown(rect, CellRect(CellsOf(widget)));
        }
    }

    // Grid dimensions (columns, rows): the fixed FixedColumns x FixedRows, else how many CellSize cells
    // fit the rect. (0,0) until the rect has a size, so nothing reflows against a grid that is not
    // laid out yet.
    public (int cols, int rows) GridSize
    {
        get
        {
            int fc = FixedColumns.Value, fr = FixedRows.Value;
            if (fc > 0 && fr > 0)
                return (fc, fr);
            var r = RectTransform?.LocalComputeRect;
            if (r == null || r.Value.width <= 0f || r.Value.height <= 0f)
                return (0, 0);
            var cell = CellSize.Value;
            var spacing = Spacing.Value;
            var pad = Padding.Value;
            return (CountAlong(r.Value.width - 2f * pad.x, cell.x, spacing.x),
                    CountAlong(r.Value.height - 2f * pad.y, cell.y, spacing.y));
        }
    }

    // Per-cell pixel size: the fixed CellSize, or in fixed-count mode the size that makes
    // FixedColumns x FixedRows cells plus spacing exactly fill the rect. -xlinka
    public float2 EffectiveCellSize
    {
        get
        {
            int fc = FixedColumns.Value, fr = FixedRows.Value;
            var cell = CellSize.Value;
            if (fc <= 0 || fr <= 0)
                return cell;
            var r = RectTransform?.LocalComputeRect;
            if (r == null || r.Value.width <= 0f || r.Value.height <= 0f)
                return cell;
            var spacing = Spacing.Value;
            var pad = Padding.Value;
            float w = (r.Value.width - 2f * pad.x - (fc - 1) * spacing.x) / fc;
            float h = (r.Value.height - 2f * pad.y - (fr - 1) * spacing.y) / fr;
            return new float2(w > 1f ? w : 1f, h > 1f ? h : 1f);
        }
    }

    // Cell plus the gap after it: the distance from one cell's edge to the next's.
    public float2 Pitch => EffectiveCellSize + Spacing.Value;

    // Pixel offset that centers the placed cell block in the rect's leftover space (zero when
    // CenterContent is off or the grid fills the rect).
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
            return 0;
        return (int)MathF.Floor((usable + spacing) / stride);
    }

    // Cells a pixel extent needs. A widget sized to n cells measures n*pitch - spacing, so the division
    // rounds up to the cell that contains the extent; the epsilon keeps that exact case from spilling
    // into one more cell on float noise.
    private static int CellsFor(float extent, float pitch)
    {
        if (pitch <= 0f)
            return 1;
        int n = (int)MathF.Ceiling(extent / pitch - 0.001f);
        return n < 1 ? 1 : n;
    }

    private static GridRect CellsOf(Widget w)
        => new GridRect(w.GridX.Value, w.GridY.Value, Max(1, w.GridWidth.Value), Max(1, w.GridHeight.Value));

    // Pixel rect of a cell rect, measured from the grid rect's top-left corner with y growing downward.
    public Rect CellRect(in GridRect cells)
    {
        var cell = EffectiveCellSize;
        var spacing = Spacing.Value;
        var origin = Padding.Value + CenteringOffset;
        int w = Max(1, cells.Width);
        int h = Max(1, cells.Height);
        return new Rect(
            origin.x + cells.X * (cell.x + spacing.x),
            origin.y + cells.Y * (cell.y + spacing.y),
            w * cell.x + (w - 1) * spacing.x,
            h * cell.y + (h - 1) * spacing.y);
    }

    // A point in this grid's layout space (context.PointIn(Slot)) as top-down pixels from the top-left.
    public float2 TopDown(in float2 localPoint)
    {
        var r = RectTransform?.LocalComputeRect ?? default;
        return new float2(localPoint.x - r.xMin, r.yMax - localPoint.y);
    }

    // Grid cell (column, row) under a point in this grid's layout space, row 0 at the top. Unclamped.
    public (int col, int row) CellAt(in float2 localPoint)
    {
        var p = TopDown(localPoint) - Padding.Value - CenteringOffset;
        var pitch = Pitch;
        return ((int)MathF.Floor(p.x / pitch.x), (int)MathF.Floor(p.y / pitch.y));
    }

    private (int col, int row) ClampedCellAt(in float2 localPoint, int cols, int rows)
    {
        var (c, r) = CellAt(localPoint);
        return (Clamp(c, 0, cols - 1), Clamp(r, 0, rows - 1));
    }

    private static GridRect ClampInside(in GridRect rect, int cols, int rows)
    {
        int w = Clamp(rect.Width, 1, cols);
        int h = Clamp(rect.Height, 1, rows);
        return new GridRect(Clamp(rect.X, 0, cols - w), Clamp(rect.Y, 0, rows - h), w, h);
    }

    // Anchor a child rect to the grid's top-left corner and give it a top-down pixel rect.
    public static void ApplyTopDown(RectTransform rect, in Rect r)
    {
        rect.AnchorMin.Value = new float2(0f, 1f);
        rect.AnchorMax.Value = new float2(0f, 1f);
        rect.OffsetMin.Value = new float2(r.x, -(r.y + r.height));
        rect.OffsetMax.Value = new float2(r.x + r.width, -r.y);
    }

    // The widget under a point in this grid's layout space. Half the gap around each widget counts as
    // that widget, so a pick on the seam between two never falls through to the grid.
    public Widget? WidgetAt(in float2 localPoint)
    {
        var p = TopDown(localPoint);
        var half = Spacing.Value * 0.5f;
        _hitBuffer.Clear();
        Slot.GetComponentsInChildren(_hitBuffer, false);
        foreach (var w in _hitBuffer)
        {
            if (w.IsDestroyed)
                continue;
            var r = CellRect(CellsOf(w));
            if (p.x >= r.x - half.x && p.x <= r.x + r.width + half.x &&
                p.y >= r.y - half.y && p.y <= r.y + r.height + half.y)
                return w;
        }
        return null;
    }

    // EDIT GESTURE

    // Custom sizing starts once the press has travelled about half a cell, so a tap places at the
    // preferred size and a twitch during the tap still counts as a tap.
    private float CustomSizingThreshold => Pitch.Length * 0.5f;

    public bool IsCarrying(UIInteractionSource source) => HeldPanelFor(source) != null;

    private static WidgetPanel? HeldPanelFor(UIInteractionSource source)
        => UserspacePointer.ForSide(source == UIInteractionSource.VRLeft ? Chirality.Left : Chirality.Right)?.HeldPanel;

    // The edit gesture, fed by WidgetGridInteraction with what happened to the hover and the press this
    // frame. Nothing in hand: show the cell under the pointer and outline the widget it would pick up.
    // A panel in hand: preview where it lands at the pointer, switch to a custom size once the press is
    // dragged past the threshold, and put it down when that press ends over the grid. The hover ending
    // or the grip opening ends the preview. -xlinka
    public void ProcessPointer(in UIInteractionContext context, GridPointerPhase hover, GridPointerPhase touch)
    {
        if (!EditMode.Value)
        {
            ClearVisuals();
            ResetGesture();
            return;
        }

        if (_gesturePanel != null && _gesturePanel.IsDestroyed)
            _gesturePanel = null;

        var held = HeldPanelFor(context.Source);
        // Another pointer is mid-gesture over this grid with a different panel; this one waits.
        if (_gesturePanel != null && held != null && !ReferenceEquals(_gesturePanel, held))
            return;

        bool gripOpened = false;
        if (held == null && _gesturePanel != null && _gestureSource == context.Source && _gesturePointerId == context.PointerId)
        {
            gripOpened = true;
            held = _gesturePanel;
        }

        var point = context.PointIn(Slot);
        if (held == null)
        {
            bool hovering = hover != GridPointerPhase.End;
            CursorRect = hovering ? CellRect(CursorCell(point)) : null;
            var under = hovering ? WidgetAt(point) : null;
            HoverRect = under != null ? CellRect(CellsOf(under)) : null;
            PreviewRect = null;
            ResetGesture();
            return;
        }

        _gesturePanel = held;
        _gestureSource = context.Source;
        _gesturePointerId = context.PointerId;
        HoverRect = null;

        bool finished = false;
        if (touch != GridPointerPhase.None)
        {
            float2? forceSize = null;
            var anchor = point;
            if (touch == GridPointerPhase.Begin || !_dragOrigin.HasValue)
            {
                _dragOrigin = point;
            }
            else
            {
                var delta = point - _dragOrigin.Value;
                if (delta.Length >= CustomSizingThreshold)
                {
                    CursorRect = CellRect(CursorCell(point));
                    anchor = _dragOrigin.Value;
                    forceSize = delta;
                    Preview(held, anchor, forceSize);
                }
            }

            if (touch == GridPointerPhase.End)
            {
                _dragOrigin = null;
                PreviewRect = null;
                if (hover != GridPointerPhase.End)
                {
                    finished = true;
                    PlacePanel(held, anchor, forceSize);
                }
                else
                {
                    CursorRect = null;
                }
            }
        }
        else if (hover == GridPointerPhase.End || gripOpened)
        {
            finished = true;
            PreviewRect = null;
            CursorRect = null;
        }
        else
        {
            CursorRect = CellRect(CursorCell(point));
            Preview(held, point, null);
        }

        if (finished)
            ResetGesture();
    }

    private GridRect CursorCell(in float2 point)
    {
        var (cols, rows) = GridSize;
        if (cols <= 0 || rows <= 0)
            return new GridRect(0, 0, 1, 1);
        var (c, r) = ClampedCellAt(point, cols, rows);
        return new GridRect(c, r, 1, 1);
    }

    private void ResetGesture()
    {
        _gesturePanel = null;
        _gestureSource = UIInteractionSource.Unknown;
        _gesturePointerId = 0;
        _dragOrigin = null;
    }

    private void ClearVisuals()
    {
        CursorRect = null;
        HoverRect = null;
        PreviewRect = null;
        PreviewValid = false;
    }

    // PICK UP

    // The grab action over this grid in edit mode: lift the widget under the pointer off the grid as a
    // panel the pointer can carry. Null when there is no widget there or the grid is not editing.
    public IGrabbable? TryPickUp(in UIInteractionContext context)
    {
        if (!EditMode.Value)
            return null;
        var widget = WidgetAt(context.PointIn(Slot));
        return widget != null ? PickUp(widget, in context) : null;
    }

    // Re-host the widget's preset on a standalone panel posed exactly where the widget sits on the dash
    // surface, at the size it had, then remove it from the grid. The grid only knows the preset's
    // runtime type, so the panel rebuilds the preset rather than moving the canvas subtree. -xlinka
    public IGrabbable? PickUp(Widget widget, in UIInteractionContext context)
    {
        if (widget == null || widget.IsDestroyed)
            return null;
        var presetType = widget.Slot.GetComponent<WidgetPreset>()?.GetType();
        if (presetType == null)
            return null;

        var rect = widget.RectTransform?.LocalComputeRect ?? default;
        var size = rect.Size;
        if (size.x <= 0f || size.y <= 0f)
            size = widget.PreferredSize.Value;

        float3 position;
        floatQ rotation;
        float metersPerPixel;
        var dash = UserspaceDashboard.LocalInstance;
        if (dash == null || dash.IsDestroyed || !ReferenceEquals(dash.World, World)
            || !dash.TryCanvasToSurface(rect.Center, out position, out rotation, out metersPerPixel))
        {
            // Not the dash: the canvas slot itself is where the widget is in the world.
            var canvasSlot = context.Canvas?.Slot ?? Slot;
            position = canvasSlot.LocalPointToGlobal(new float3(rect.Center.x, rect.Center.y, 0f));
            rotation = canvasSlot.GlobalRotation;
            metersPerPixel = canvasSlot.GlobalScale.x;
        }

        var panel = WidgetPanel.Spawn(presetType, position, rotation, size);
        if (panel == null)
            return null;
        panel.SetPixelScale(metersPerPixel);
        panel.LastPlacedSize.Value = size;

        widget.Slot.Destroy();
        InvalidateArrange();
        return panel.GrabHandle;
    }

    // PUT DOWN

    // Items let go over this grid: the first carried widget panel that fits at the pointer lands here at
    // its preferred size. Works in any mode, the same as dropping onto the dash from anywhere.
    public bool TryReceive(IReadOnlyList<IGrabbable> items, in UIInteractionContext context)
    {
        var point = context.PointIn(Slot);
        for (int i = 0; i < items.Count; i++)
        {
            var panel = WidgetPanel.From(items[i]);
            if (panel != null && PlacePanel(panel, point, null))
                return true;
        }
        return false;
    }

    private bool PlacePanel(WidgetPanel panel, in float2 point, float2? forceSize)
    {
        if (panel == null || panel.IsDestroyed)
            return false;
        var presetType = panel.PresetType;
        var widget = panel.ContentWidget;
        if (presetType == null || widget == null)
            return false;

        var cells = ComputeCarriedPlacement(widget, panel.LastPlacedSize.Value, point, forceSize, out _);
        if (!cells.HasValue)
            return false;

        var grab = panel.GrabHandle;
        if (grab != null)
            grab.Grabber?.Release(grab);
        var preset = AddWidget(presetType, cells.Value);
        if (preset == null)
            return false;
        panel.Slot.Destroy();
        return true;
    }

    // A fresh widget of the given preset type at a cell rect. Its own chunk root: a widget that repaints
    // on its own clock re-meshes itself instead of the whole screen.
    public WidgetPreset? AddWidget(Type presetType, in GridRect cells)
    {
        var slot = Slot.AddSlot(presetType.Name);
        slot.AttachComponent<GraphicChunkRoot>();
        if (slot.AttachComponent(presetType) is not WidgetPreset preset)
        {
            slot.Destroy();
            return null;
        }
        preset.GridX.Value = cells.X;
        preset.GridY.Value = cells.Y;
        preset.GridWidth.Value = cells.Width;
        preset.GridHeight.Value = cells.Height;
        PlacedStyler?.Invoke(preset);
        InvalidateArrange();
        return preset;
    }

    private void Preview(WidgetPanel panel, in float2 point, float2? forceSize)
    {
        var widget = panel.ContentWidget;
        if (widget == null)
        {
            PreviewRect = null;
            return;
        }
        var cells = ComputeCarriedPlacement(widget, panel.LastPlacedSize.Value, point, forceSize, out var fallback);
        PreviewValid = cells.HasValue;
        PreviewRect = CellRect(cells ?? fallback);
    }

    // Where a carried widget lands. Forced (the press was dragged out): the cells between the origin and
    // the drag end, the size negotiated through the widget's own limits, anchored at the origin corner
    // and growing the way the drag went; blocked outright if it overlaps anything. Unforced (a tap or a
    // drop): the cell under the pointer must be free, then its last placed size and its preferred size
    // are tried centered there, then whatever preferred size fits the free area around that cell, and
    // last the widget is offered the free area itself and negotiates down to what it accepts. The
    // fallback is what the blocked preview outlines. -xlinka
    private GridRect? ComputeCarriedPlacement(Widget widget, float2 lastPlaced, in float2 point, float2? forceSize, out GridRect fallback)
    {
        var (cols, rows) = GridSize;
        fallback = new GridRect(0, 0, 1, 1);
        if (cols <= 0 || rows <= 0)
            return null;

        var occupied = OccupiedRects(null);
        var spacing = Spacing.Value;
        var pitch = Pitch;

        if (forceSize.HasValue)
        {
            var (ox, oy) = ClampedCellAt(point, cols, rows);
            var (ex, ey) = ClampedCellAt(point + forceSize.Value, cols, rows);
            int rawW = Abs(ex - ox) + 1;
            int rawH = Abs(ey - oy) + 1;
            fallback = new GridRect(Min(ox, ex), Min(oy, ey), rawW, rawH);

            var fitted = widget.FitSize(new float2(rawW * pitch.x - spacing.x, rawH * pitch.y - spacing.y));
            if (!fitted.HasValue)
                return null;
            int w = Min(CellsFor(fitted.Value.x, pitch.x), rawW);
            int h = Min(CellsFor(fitted.Value.y, pitch.y), rawH);
            int x = ex >= ox ? ox : ox - (w - 1);
            int y = ey >= oy ? oy : oy - (h - 1);
            var forced = ClampInside(new GridRect(x, y, w, h), cols, rows);
            return WidgetGridPlacement.IntersectsAny(in forced, occupied) ? null : forced;
        }

        var (cx, cy) = ClampedCellAt(point, cols, rows);
        var seed = new GridRect(cx, cy, 1, 1);
        fallback = seed;
        if (WidgetGridPlacement.IntersectsAny(in seed, occupied))
            return null;

        var preferred = new List<(int w, int h)>(2);
        if (lastPlaced.x > 0f && lastPlaced.y > 0f)
            preferred.Add((CellsFor(lastPlaced.x, pitch.x), CellsFor(lastPlaced.y, pitch.y)));
        var pref = widget.PreferredSize.Value;
        var prefCells = (CellsFor(pref.x, pitch.x), CellsFor(pref.y, pitch.y));
        if (!preferred.Contains(prefCells))
            preferred.Add(prefCells);

        foreach (var (w, h) in preferred)
        {
            if (w > cols || h > rows)
                continue;
            var r = WidgetGridPlacement.CenterClamp(cx, cy, w, h, cols, rows);
            if (!WidgetGridPlacement.IntersectsAny(in r, occupied))
                return r;
        }
        var (fw, fh) = preferred[0];
        fallback = WidgetGridPlacement.CenterClamp(cx, cy, Min(fw, cols), Min(fh, rows), cols, rows);

        var grown = WidgetGridPlacement.GrowFree(seed, cols, rows, occupied);
        foreach (var (w, h) in preferred)
        {
            if (w <= grown.Width && h <= grown.Height)
                return WidgetGridPlacement.FitWithin(grown, w, h, cx, cy);
        }

        float maxPrefW = 0f, maxPrefH = 0f;
        foreach (var (w, h) in preferred)
        {
            maxPrefW = MathF.Max(maxPrefW, w * pitch.x);
            maxPrefH = MathF.Max(maxPrefH, h * pitch.y);
        }
        var offered = new float2(
            MathF.Min(maxPrefW, grown.Width * pitch.x) - spacing.x,
            MathF.Min(maxPrefH, grown.Height * pitch.y) - spacing.y);
        var fit = widget.FitSize(offered);
        if (!fit.HasValue)
            return null;
        int gw = Min(CellsFor(fit.Value.x, pitch.x), grown.Width);
        int gh = Min(CellsFor(fit.Value.y, pitch.y), grown.Height);
        return WidgetGridPlacement.FitWithin(grown, gw, gh, cx, cy);
    }

    // REFLOW

    // Cell rects of every widget except `exclude`, for collision tests.
    private List<GridRect> OccupiedRects(Widget? exclude)
    {
        var list = new List<GridRect>();
        foreach (var w in Slot.GetComponentsInChildren<Widget>(false))
        {
            if (ReferenceEquals(w, exclude) || w.IsDestroyed)
                continue;
            list.Add(CellsOf(w));
        }
        return list;
    }

    // Footprints the widget will accept, in the order the placement engine should try them: its
    // last-placed size FIRST (stability across reflows), then its authored footprint down to its
    // minimum (largest area first). So a widget keeps its current size when it still fits, can grow back
    // toward its authored size when room opens, and shrinks to fit when tight instead of failing to
    // place. -xlinka
    private static List<(int w, int h)> PreferredCells(Widget widget)
    {
        var (mw, mh) = MinCells(widget);
        var (aw, ah) = AuthoredCells(widget);

        var sizes = new List<(int w, int h)>();

        int cw = Max(1, widget.GridWidth.Value);
        int ch = Max(1, widget.GridHeight.Value);
        if (cw >= mw && cw <= aw && ch >= mh && ch <= ah)
            sizes.Add((cw, ch));

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

    // The widget's authored footprint: its captured PreferredGrid size if set, else its current
    // GridWidth/GridHeight (treated as authored until the first placement captures it).
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

    // Re-flow after the cell dimensions change: keep every widget that still fits fully in-bounds
    // without overlapping, and re-insert any that now fall off the edge or collide into the first free
    // spot. Guarantees a valid, overlap-free layout when the container resizes instead of leaving
    // widgets clipped past the edge. -xlinka
    public void Reflow()
    {
        var (cols, rows) = GridSize;
        if (cols <= 0 || rows <= 0)
            return;

        var widgets = new List<Widget>(Slot.GetComponentsInChildren<Widget>(false));
        var kept = new List<GridRect>();
        var displaced = new List<Widget>();

        foreach (var w in widgets)
        {
            if (w.IsDestroyed)
                continue;
            var rect = CellsOf(w);
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
                continue; // grid is full: leave it put, ArrangeWidgets still draws it where it says
            ApplyPlacement(w, placement.Value);
            kept.Add(placement.Value);
        }
    }

    private static void ApplyPlacement(Widget widget, in GridRect rect)
    {
        // Capture the authored footprint the first time we place, so a later shrink-to-fit does not
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
    private static int Min(int a, int b) => a < b ? a : b;
    private static int Abs(int a) => a < 0 ? -a : a;
    private static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
}
