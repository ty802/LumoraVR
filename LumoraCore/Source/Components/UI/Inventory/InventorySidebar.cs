// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Components.UI.Worlds;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The inventory's left rail. On top, the way down from the root to the folder being looked at, one
// row per step and each step indented under the one before, so the tree you are standing in is
// always readable and any step is a press away. At the bottom, the kind filters and the new folder
// button, pinned by a spacer the way the world browser's rail does it. -xlinka
internal sealed class InventorySidebar
{
    public const float Width = 176f;
    private const float RowHeight = 27f;
    private const float RowSpacing = 3f;
    private const float RowInset = 12f;
    private const float Indent = 14f;
    private const float CountGutter = 40f;

    public readonly Slot Root;
    public Action<string>? PathPicked;
    public Action<int>? FilterPicked;

    private readonly BrowserParts _parts;
    private readonly Slot _tree;
    private readonly Slot _filters;
    private readonly List<Row> _filterRows = new();
    private readonly List<(RectTransform rect, string id)> _treeRects = new();
    private int _selectedFilter = -1;

    public IReadOnlyList<(RectTransform rect, string id)> TreeRects => _treeRects;

    public InventorySidebar(BrowserParts parts, Slot parent, string actionLabel, Action onAction)
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

        var heading = Root.AddSlot("Heading");
        heading.AttachComponent<RectTransform>();
        BrowserParts.Height(heading, 22f);
        var headingText = _parts.Label(heading, "WHERE YOU ARE", DashTheme.FontSmall, DashTheme.TextMuted,
            BrowserParts.Weight.Semibold, TextHorizontalAlignment.Left, RowInset, 0f);
        _ = headingText;

        _tree = Root.AddSlot("Tree");
        _tree.AttachComponent<RectTransform>();
        var treeColumn = _tree.AttachComponent<VerticalLayout>();
        treeColumn.Spacing.Value = RowSpacing;
        treeColumn.ForceExpandWidth.Value = true;
        treeColumn.ForceExpandHeight.Value = false;

        var spacer = Root.AddSlot("Spacer");
        spacer.AttachComponent<RectTransform>();
        var spacerElement = spacer.AttachComponent<LayoutElement>();
        spacerElement.MinHeight.Value = RowSpacing;
        spacerElement.PreferredHeight.Value = RowSpacing;
        spacerElement.FlexibleHeight.Value = 1f;
        spacerElement.FlexibleWidth.Value = 1f;

        var showHeading = Root.AddSlot("ShowHeading");
        showHeading.AttachComponent<RectTransform>();
        BrowserParts.Height(showHeading, 22f);
        _parts.Label(showHeading, "SHOW", DashTheme.FontSmall, DashTheme.TextMuted,
            BrowserParts.Weight.Semibold, TextHorizontalAlignment.Left, RowInset, 0f);

        _filters = Root.AddSlot("Filters");
        _filters.AttachComponent<RectTransform>();
        var filterColumn = _filters.AttachComponent<VerticalLayout>();
        filterColumn.Spacing.Value = RowSpacing;
        filterColumn.ForceExpandWidth.Value = true;
        filterColumn.ForceExpandHeight.Value = false;

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

    // The chain from the root to the current folder. The last step is the one you are in and is lit;
    // every other step is a place to go back to.
    public void SetPath(IReadOnlyList<(string id, string name)> chain)
    {
        _tree.DestroyChildren();
        _treeRects.Clear();
        for (int i = 0; i < chain.Count; i++)
        {
            var (id, name) = chain[i];
            bool last = i == chain.Count - 1;
            var slot = _tree.AddSlot(name);
            var rect = slot.AttachComponent<RectTransform>();
            BrowserParts.Height(slot, RowHeight);
            var panel = BrowserParts.Panel(slot, last ? DashTheme.Accent : color.Transparent, DashTheme.RadiusControl);
            float inset = RowInset + i * Indent;
            string glyph = i == 0 ? "" : "› ";
            var label = _parts.Label(slot, glyph + BrowserParts.Truncate(name, 20 - i), DashTheme.FontBody,
                last ? DashTheme.OnAccent : DashTheme.TextDim, last ? BrowserParts.Weight.Semibold : BrowserParts.Weight.Regular,
                TextHorizontalAlignment.Left, inset, 0f);
            _ = label;
            _treeRects.Add((rect, id));
            if (last)
                continue;
            var row = new Row(0, panel, null, null);
            var button = slot.AttachComponent<Button>();
            button.HoverEntered += _ => row.SetHovered(true);
            button.HoverExited += _ => row.SetHovered(false);
            string target = id;
            button.Clicked += (_, _) => PathPicked?.Invoke(target);
        }
    }

    public void AddFilter(int id, string label)
    {
        var slot = _filters.AddSlot(label);
        slot.AttachComponent<RectTransform>();
        BrowserParts.Height(slot, RowHeight);
        var panel = BrowserParts.Panel(slot, color.Transparent, DashTheme.RadiusControl);
        var name = _parts.Label(slot, label, DashTheme.FontBody, DashTheme.TextDim,
            BrowserParts.Weight.Semibold, TextHorizontalAlignment.Left, RowInset, CountGutter);
        var count = _parts.Label(slot, string.Empty, DashTheme.FontSmall, DashTheme.TextMuted,
            BrowserParts.Weight.Regular, TextHorizontalAlignment.Right, 0f, RowInset);
        var row = new Row(id, panel, name, count);
        _filterRows.Add(row);
        var button = slot.AttachComponent<Button>();
        button.HoverEntered += _ => row.SetHovered(true);
        button.HoverExited += _ => row.SetHovered(false);
        button.Clicked += (_, _) => FilterPicked?.Invoke(id);
        row.Repaint();
    }

    public void SetCount(int id, int count)
    {
        for (int i = 0; i < _filterRows.Count; i++)
        {
            if (_filterRows[i].Id != id)
                continue;
            BrowserParts.SetText(_filterRows[i].Count, count > 0 ? count.ToString() : string.Empty);
            return;
        }
    }

    public void SelectFilter(int id)
    {
        _selectedFilter = id;
        for (int i = 0; i < _filterRows.Count; i++)
            _filterRows[i].SetSelected(_filterRows[i].Id == id);
    }

    public int SelectedFilter => _selectedFilter;

    // Painted by hand rather than through a ColorDriver, which would own the panel colour and refuse
    // the selection write. Same trap the world browser's rail already stepped around. -xlinka
    private sealed class Row
    {
        public readonly int Id;
        public readonly Text? Count;
        private readonly RoundedPanel _panel;
        private readonly Text? _name;
        private bool _hovered;
        private bool _selected;

        public Row(int id, RoundedPanel panel, Text? name, Text? count)
        {
            Id = id;
            _panel = panel;
            _name = name;
            Count = count;
        }

        public void SetHovered(bool value)
        {
            _hovered = value;
            Repaint();
        }

        public void SetSelected(bool value)
        {
            _selected = value;
            Repaint();
        }

        public void Repaint()
        {
            var fill = _selected ? DashTheme.Accent : _hovered ? DashTheme.SurfaceHover : color.Transparent;
            if (_selected && _name == null)
                return;
            BrowserParts.SetColor(_panel.Color, fill);
            if (_name != null)
                BrowserParts.SetColor(_name.Color, _selected ? DashTheme.OnAccent : DashTheme.TextDim);
        }
    }
}
