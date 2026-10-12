// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The layouts a window can be sent to, hung under its fill button: one pane, two side by side, two with a
// third stacked beside them, and four. Every pane is a target; pointing at one outlines where the window would
// go, pressing it sends the window there, and whatever stood there moves aside the way a drop does. It comes
// up when the pointer rests on the fill button, or when the button is held, and goes when the pointer has
// been off it and the button for a moment, when a pane is picked, or when the dash is pressed anywhere else.
// The component's own slot stays live so its clock runs; what shows and hides is the sheet under it. -xlinka
[ComponentCategory("Hidden")]
public sealed class DashSnapPicker : UIComponent
{
    private const float CellWidth = 34f;
    private const float CellHeight = 24f;
    private const float CellGap = 3f;
    private const float LayoutPad = 4f;
    private const float LayoutGap = 6f;
    private const float Pad = 8f;
    private const float Below = 6f;
    private const double Dwell = 0.3;
    private const double Linger = 0.35;
    private const float LayoutWidth = CellWidth * 2f + CellGap + LayoutPad * 2f;
    private const float LayoutHeight = CellHeight * 2f + CellGap + LayoutPad * 2f;
    public const float Width = Pad * 2f + LayoutWidth * 4f + LayoutGap * 3f;
    public const float Height = Pad * 2f + LayoutHeight;

    // Each layout as its panes: the tile, and where it sits in the two-by-two thumbnail.
    private static readonly (DashWindow.WindowSnap Tile, int X, int Y, int W, int H)[][] Layouts =
    {
        new[] { (DashWindow.WindowSnap.Fill, 0, 0, 2, 2) },
        new[] { (DashWindow.WindowSnap.Left, 0, 0, 1, 2), (DashWindow.WindowSnap.Right, 1, 0, 1, 2) },
        new[] { (DashWindow.WindowSnap.Left, 0, 0, 1, 2), (DashWindow.WindowSnap.TopRight, 1, 0, 1, 1), (DashWindow.WindowSnap.BottomRight, 1, 1, 1, 1) },
        new[]
        {
            (DashWindow.WindowSnap.TopLeft, 0, 0, 1, 1), (DashWindow.WindowSnap.TopRight, 1, 0, 1, 1),
            (DashWindow.WindowSnap.BottomLeft, 0, 1, 1, 1), (DashWindow.WindowSnap.BottomRight, 1, 1, 1, 1),
        },
    };

    private Dashboard? _dash;
    private Slot? _sheet;
    private RectTransform? _rect;
    private readonly List<(DashWindow.WindowSnap Tile, Button Button)> _panes = new();
    private DashWindow? _window;
    private DashWindow? _hoverWindow;
    private double _hoverSince = -1;
    private int _inside;
    private bool _onButton;
    private double _leftAt = -1;

    public bool IsOpen => _sheet != null && !_sheet.IsDestroyed && _sheet.ActiveSelf.Value && _window != null;
    public Slot? Sheet => _sheet;
    public DashWindow? Window => IsOpen ? _window : null;
    public IReadOnlyList<(DashWindow.WindowSnap Tile, Button Button)> Panes => _panes;

    internal void Bind(Dashboard dash)
    {
        _dash = dash;
        var own = RectTransform ?? Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
        own.AnchorMin.Value = float2.Zero;
        own.AnchorMax.Value = float2.One;
        own.OffsetMin.Value = float2.Zero;
        own.OffsetMax.Value = float2.Zero;
        _sheet = Slot.AddSlot("Sheet");
        _rect = _sheet.AttachComponent<RectTransform>();
        _rect.AnchorMin.Value = new float2(0f, 1f);
        _rect.AnchorMax.Value = new float2(0f, 1f);
        _sheet.AttachComponent<GraphicChunkRoot>();
        var panel = _sheet.AttachComponent<RoundedPanel>();
        panel.CornerRadius.Value = 12f;
        panel.Color.Value = DashTheme.GlassWindow;
        panel.OutlineColor.Value = DashTheme.OutlineStrong;
        panel.OutlineThickness.Value = DashTheme.OutlineWidth;
        panel.Material.Target = dash.BackingMaterial!;
        var ground = _sheet.AttachComponent<InteractionElement>();
        Track(ground);

        for (int i = 0; i < Layouts.Length; i++)
        {
            float left = Pad + i * (LayoutWidth + LayoutGap);
            var frame = SettingsUI.Child(_sheet, "Layout" + i, new float2(0f, 1f), new float2(0f, 1f),
                new float2(left, -(Pad + LayoutHeight)), new float2(left + LayoutWidth, -Pad));
            SettingsUI.Panel(frame, DashTheme.Field.WithAlpha(0.6f), DashTheme.Outline, 8f);
            foreach (var (tile, x, y, w, h) in Layouts[i])
            {
                float px = LayoutPad + x * (CellWidth + CellGap);
                float py = LayoutPad + y * (CellHeight + CellGap);
                float pw = w * CellWidth + (w - 1) * CellGap;
                float ph = h * CellHeight + (h - 1) * CellGap;
                var paneSlot = SettingsUI.Child(frame, tile.ToString(), new float2(0f, 1f), new float2(0f, 1f),
                    new float2(px, -(py + ph)), new float2(px + pw, -py));
                var paint = SettingsUI.Panel(paneSlot, DashTheme.GlassCard, color.Transparent, 5f);
                var button = paneSlot.AttachComponent<Button>();
                button.Tip.Value = TipFor(tile);
                var driver = button.AddColorDriver(paint.Color, DashTheme.GlassCard, InteractionColorMode.Direct);
                driver.NormalColor.Value = DashTheme.GlassCard;
                driver.HighlightColor.Value = DashTheme.Accent.WithAlpha(0.7f);
                driver.PressedColor.Value = DashTheme.Accent;
                driver.DisabledColor.Value = DashTheme.Field.WithAlpha(0.35f);
                driver.Apply();
                var pick = tile;
                button.Clicked += (_, _) => Pick(pick);
                button.HoverEntered += _ => Preview(pick);
                button.HoverExited += _ => Preview(DashWindow.WindowSnap.None);
                Track(button);
                _panes.Add((tile, button));
            }
        }
        _sheet.ActiveSelf.Value = false;
    }

    private static string TipFor(DashWindow.WindowSnap tile) => tile switch
    {
        DashWindow.WindowSnap.Left => "Left half",
        DashWindow.WindowSnap.Right => "Right half",
        DashWindow.WindowSnap.Fill => "Fill",
        DashWindow.WindowSnap.TopLeft => "Top left",
        DashWindow.WindowSnap.TopRight => "Top right",
        DashWindow.WindowSnap.BottomLeft => "Bottom left",
        DashWindow.WindowSnap.BottomRight => "Bottom right",
        _ => string.Empty,
    };

    private void Track(InteractionElement element)
    {
        element.HoverEntered += _ =>
        {
            _inside++;
            _leftAt = -1;
        };
        element.HoverExited += _ =>
        {
            _inside = System.Math.Max(0, _inside - 1);
            if (_inside == 0)
                _leftAt = Now;
        };
    }

    private double Now => World?.Time.TotalTime ?? 0.0;

    // The pointer resting on a fill button brings the layouts up after a moment; leaving it lets them go
    // unless the pointer went on to them.
    public void FillHover(DashWindow window, bool hovered)
    {
        _onButton = hovered;
        if (hovered)
        {
            _hoverWindow = window;
            _hoverSince = Now;
            _leftAt = -1;
        }
        else
        {
            _hoverSince = -1;
            if (IsOpen && _inside == 0)
                _leftAt = Now;
        }
    }

    public void Open(DashWindow window)
    {
        if (_dash == null || _rect == null || window.State != DashWindow.WindowState.Open)
            return;
        _window = window;
        foreach (var (tile, pane) in _panes)
            Helio.UI.Listing.ListingStyle.SetInteractable(pane, window.Workspace?.CanSnap(window, tile) == true);
        var button = window.FillButtonRect;
        var canvas = _dash.Size.Value;
        float left = MathF.Max(Dashboard.Outer, MathF.Min(button.x + button.width - Width, canvas.x - Dashboard.Outer - Width));
        float top = button.y + button.height + Below;
        if (top + Height > canvas.y - Dashboard.Outer)
            top = button.y - Below - Height;
        SettingsUI.SetOffsets(_rect, new float2(left, -(top + Height)), new float2(left + Width, -top));
        _leftAt = _onButton || _inside > 0 ? -1 : Now;
        if (_sheet != null && !_sheet.ActiveSelf.Value)
            _sheet.ActiveSelf.Value = true;
    }

    public void Close()
    {
        Preview(DashWindow.WindowSnap.None);
        var window = _window;
        _window = null;
        if (window != null && !window.IsDestroyed)
            window.LayoutsClosed();
        _inside = 0;
        _leftAt = -1;
        if (_sheet != null && _sheet.ActiveSelf.Value)
            _sheet.ActiveSelf.Value = false;
    }

    public void Pick(DashWindow.WindowSnap tile)
    {
        var window = _window;
        Close();
        if (window == null || window.IsDestroyed)
            return;
        window.Workspace?.SendTo(window, tile);
    }

    private void Preview(DashWindow.WindowSnap tile)
    {
        var workspace = _window?.Workspace;
        if (_dash == null || workspace == null)
            return;
        _dash.ShowGuide(tile != DashWindow.WindowSnap.None && _window != null && workspace.CanSnap(_window, tile)
            ? workspace.SnapRect(tile) : null);
    }

    // A press anywhere on the dash but the layouts puts them away.
    internal void OnDashPress(Slot? slot)
    {
        if (!IsOpen)
            return;
        if (slot != null && _sheet != null && (ReferenceEquals(slot, _sheet) || slot.IsDescendantOf(_sheet)))
            return;
        Close();
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (_hoverSince >= 0 && _onButton && Now - _hoverSince >= Dwell)
        {
            _hoverSince = -1;
            if (_hoverWindow != null && !_hoverWindow.IsDestroyed && (!IsOpen || !ReferenceEquals(_window, _hoverWindow)))
                Open(_hoverWindow);
        }
        if (!IsOpen)
            return;
        if (_window == null || _window.IsDestroyed || _window.State != DashWindow.WindowState.Open)
        {
            Close();
            return;
        }
        if (_leftAt >= 0 && !_onButton && _inside == 0 && Now - _leftAt >= Linger)
            Close();
    }
}
