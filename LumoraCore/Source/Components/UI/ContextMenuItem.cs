// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Components.UI;

// polar coordinate layout: AngleStart + ArcLength (degrees) = arc position around the ring;
// RadiusStart + Thickness (pixels) = inner and outer radius of the arc
public class ContextMenuItem
{
    // Display

    public string Label { get; set; } = "";

    // res:// path to an icon Texture2D. null = no icon
    public string? IconPath { get; set; }

    // RGBA 0-1
    public float[] FillColor { get; set; } = { 0.12f, 0.12f, 0.12f, 0.9f };

    // null = derived from FillColor (or toggle state) by the menu; only set this to force a specific outline
    public float[]? OutlineColor { get; set; }

    public float[] LabelColor { get; set; } = { 1f, 1f, 1f, 1f };

    // State

    public bool IsEnabled { get; set; } = true;

    public bool IsToggle { get; set; } = false;

    // only meaningful when IsToggle is true
    public bool IsToggled { get; set; } = false;

    // Actions

    // null for label-only (non-interactive) items
    public Action<ContextMenuItem>? OnPressed { get; set; }

    // if set, pressing this item navigates into this sub-page instead of firing OnPressed
    public ContextMenuPage? SubPage { get; set; }

    // Polar coordinate layout (set by ContextMenuPage.LayoutItems)

    // degrees, 0 = right, increases clockwise
    public float AngleStart { get; internal set; }

    // degrees
    public float ArcLength { get; internal set; }

    // pixels, distance from center to the inner arc edge
    public float RadiusStart { get; set; } = 55f;

    // pixels, inner-to-outer arc width
    public float Thickness { get; set; } = 80f;

    // Computed helpers

    public float AngleEnd     => AngleStart + ArcLength;
    public float AngleMiddle  => AngleStart + ArcLength * 0.5f;
    public float RadiusEnd    => RadiusStart + Thickness;
    public float RadiusMiddle => RadiusStart + Thickness * 0.5f;
}

// pages can be stacked to create sub-menus
public class ContextMenuPage
{
    // shown in the center circle
    public string Title { get; set; } = "";

    public List<ContextMenuItem> Items { get; } = new();

    // degrees, gap between adjacent items
    public float SeparationAngle { get; set; } = 2f;

    public ContextMenuPage(string title = "") => Title = title;

    // Fluent builder

    public ContextMenuPage AddItem(ContextMenuItem item)
    {
        Items.Add(item);
        return this;
    }

    // sub-page behind the root item labelled `label`, created on first use. several sources
    // contribute tool actions to one page; each asks for the same submenu instead of stacking its
    // own items on the root ring, so the root stays one entry per topic.
    public ContextMenuPage GetOrAddSubPage(string label, float[] fillColor)
    {
        foreach (var existing in Items)
        {
            if (existing.SubPage != null && existing.Label == label)
                return existing.SubPage;
        }
        var subPage = new ContextMenuPage(label);
        Items.Add(new ContextMenuItem
        {
            Label = label,
            FillColor = fillColor,
            SubPage = subPage,
        });
        return subPage;
    }

    public ContextMenuPage AddItem(string label, Action<ContextMenuItem> onPressed,
                                   string? iconPath = null)
    {
        Items.Add(new ContextMenuItem
        {
            Label     = label,
            OnPressed = onPressed,
            IconPath  = iconPath,
        });
        return this;
    }

    // Layout

    // distributes all items evenly around 360 degrees, respecting SeparationAngle gaps.
    // call this before passing a page to ContextMenuView.
    public void LayoutItems()
    {
        if (Items.Count == 0) return;

        float totalGap  = SeparationAngle * Items.Count;
        float available = 360f - totalGap;
        float slice     = available / Items.Count;

        // Start at top (-90 degrees = 12 o'clock)
        float angle = -90f;
        foreach (var item in Items)
        {
            item.AngleStart = angle + SeparationAngle * 0.5f;
            item.ArcLength  = slice;
            angle += slice + SeparationAngle;
        }
    }
}
