// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI.Worlds;

// The filter rail down the left of the world browser: a list of words, a live count beside each, and
// one accent button pinned to the bottom.
//
// Same rail as the Settings screen, on purpose, down to the numbers: 172 wide, 34-high rows, a 14 inset
// on the label, and only the selected row carrying a wash. Two screens that both put a category column
// on the left have to look like the same product. -xlinka
internal sealed class FilterSidebar
{
    public const float Width = 172f;
    public const float RowHeight = 34f;
    public const float RowSpacing = 4f;
    public const float RowInset = 14f;
    // Room for the count on the right so a long filter name runs into it instead of over it.
    private const float CountGutter = 44f;

    public readonly Slot Root;
    public Action<int>? Picked;

    private readonly BrowserParts _parts;
    private readonly Slot _list;
    private readonly List<Row> _rows = new();
    private int _selected = -1;

    public FilterSidebar(BrowserParts parts, Slot parent, string actionLabel, Action onAction)
    {
        _parts = parts;
        Root = parent.AddSlot("Sidebar");
        Root.AttachComponent<RectTransform>();
        var element = Root.AttachComponent<LayoutElement>();
        element.MinWidth.Value = Width;
        element.PreferredWidth.Value = Width;
        element.FlexibleWidth.Value = 0f;
        element.FlexibleHeight.Value = 1f;

        var column = Root.AttachComponent<VerticalLayout>();
        column.Spacing.Value = RowSpacing;
        column.ForceExpandWidth.Value = true;
        column.ForceExpandHeight.Value = false;

        // The rows live in their own column so the spacer, the rule and the button can be built here
        // and still sit under whatever gets added later.
        _list = Root.AddSlot("Filters");
        _list.AttachComponent<RectTransform>();
        var listColumn = _list.AttachComponent<VerticalLayout>();
        listColumn.Spacing.Value = RowSpacing;
        listColumn.ForceExpandWidth.Value = true;
        listColumn.ForceExpandHeight.Value = false;

        // Everything above the action button is pushed up by this: the rows keep their own heights and
        // the leftover height lands here, which is what pins the button to the bottom edge.
        var spacer = Root.AddSlot("Spacer");
        spacer.AttachComponent<RectTransform>();
        var spacerElement = spacer.AttachComponent<LayoutElement>();
        spacerElement.MinHeight.Value = RowSpacing;
        spacerElement.PreferredHeight.Value = RowSpacing;
        spacerElement.FlexibleHeight.Value = 1f;
        spacerElement.FlexibleWidth.Value = 1f;

        var divider = Root.AddSlot("Divider");
        divider.AttachComponent<RectTransform>();
        var dividerElement = BrowserParts.Height(divider, 1f);
        dividerElement.Margin.Value = new float4(0f, 6f, 0f, 6f);
        divider.AttachComponent<Image>().Tint.Value = DashTheme.Divider;

        var action = _parts.AddButton(Root, actionLabel, 0f, DashTheme.ControlHeight, DashTheme.Accent,
            DashTheme.AccentHover, DashTheme.AccentPressed, DashTheme.OnAccent,
            BrowserParts.Weight.Semibold, onAction);
        action.Text.Size.Value = DashTheme.FontBody;
    }

    public void AddFilter(int id, string label)
    {
        var slot = _list.AddSlot(label);
        slot.AttachComponent<RectTransform>();
        BrowserParts.Height(slot, RowHeight);
        var panel = BrowserParts.Panel(slot, color.Transparent, DashTheme.RadiusControl);

        var name = _parts.Label(slot, label, DashTheme.FontBody, DashTheme.TextDim,
            BrowserParts.Weight.Semibold, TextHorizontalAlignment.Left, RowInset, CountGutter);
        var count = _parts.Label(slot, string.Empty, DashTheme.FontSmall, DashTheme.TextMuted,
            BrowserParts.Weight.Regular, TextHorizontalAlignment.Right, 0f, RowInset);

        var row = new Row(id, panel, name, count);
        _rows.Add(row);

        var button = slot.AttachComponent<Button>();
        // Painted by hand rather than through a ColorDriver: the driver would own Panel.Color and refuse
        // the selection write, which is the trap the create page's pills already hit. -xlinka
        button.HoverEntered += _ => row.SetHovered(true);
        button.HoverExited += _ => row.SetHovered(false);
        button.Clicked += (_, _) => Picked?.Invoke(id);
        row.Repaint();
    }

    public void SetCount(int id, int count)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Id != id)
                continue;
            // A zero is left blank. It is true, but eight rows of "0" is noise, and the empty grid
            // already says what nothing means for the filter you are on.
            BrowserParts.SetText(_rows[i].Count, count > 0 ? count.ToString() : string.Empty);
            return;
        }
    }

    public void Select(int id)
    {
        _selected = id;
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].SetSelected(_rows[i].Id == id);
    }

    public int Selected => _selected;

    private sealed class Row
    {
        public readonly int Id;
        public readonly Text Count;

        private readonly RoundedPanel _panel;
        private readonly Text _name;
        private bool _hovered;
        private bool _selected;

        public Row(int id, RoundedPanel panel, Text name, Text count)
        {
            Id = id;
            _panel = panel;
            _name = name;
            Count = count;
        }

        public void SetHovered(bool value)
        {
            if (_hovered == value)
                return;
            _hovered = value;
            Repaint();
        }

        public void SetSelected(bool value)
        {
            if (_selected == value)
                return;
            _selected = value;
            Repaint();
        }

        public void Repaint()
        {
            BrowserParts.SetColor(_panel.Color, _selected
                ? DashTheme.AccentSoft
                : _hovered ? DashTheme.SurfaceHover : color.Transparent);
            BrowserParts.SetColor(_name.Color, _selected ? DashTheme.Accent : DashTheme.TextDim);
            BrowserParts.SetColor(Count.Color, _selected ? DashTheme.Accent : DashTheme.TextMuted);
        }
    }
}
