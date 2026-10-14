// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Assets;

namespace Lumora.Core.Components.UI;

// polar coordinate layout: AngleStart + ArcLength (degrees) = arc position around the ring;
// RadiusStart + Thickness (pixels) = inner and outer radius of the arc
public class ContextMenuItem
{
    // Display

    public string Label { get; set; } = "";

    // What the hub says under the name while the item is hovered, in place of the ring's own word for it
    // (On, Off, Open, Select). Plain text, one short line.
    public string? Detail { get; set; }

    // res:// path to an icon Texture2D. null = no icon
    public string? IconPath { get; set; }

    // A texture in the world for the icon, ahead of IconPath.
    public IAssetProvider<TextureAsset>? IconAsset { get; set; }

    // The label went through ContextMenuEntry.Sanitize: its colour and emphasis tags are read.
    public bool SafeMarkup { get; set; }

    // RGBA 0-1
    public float[] FillColor { get; set; } = { 0.12f, 0.12f, 0.12f, 0.9f };

    // null = derived from FillColor (or toggle state) by the menu; only set this to force a specific outline
    public float[]? OutlineColor { get; set; }

    public float[] LabelColor { get; set; } = { 1f, 1f, 1f, 1f };

    // Where it sits on the root ring; see ContextMenuGroup.
    public ContextMenuGroup Group { get; set; } = ContextMenuGroup.Content;

    // State

    public bool IsEnabled { get; set; } = true;

    public bool IsToggle { get; set; } = false;

    // only meaningful when IsToggle is true
    public bool IsToggled { get; set; } = false;

    // A value from 0 to 1 that is turned rather than pressed. With both set, pressing the item opens a dial
    // that writes the value live while the pointer or stick goes round it.
    public Func<float>? ReadValue { get; set; }

    public Action<float>? WriteValue { get; set; }

    // Where the dial marks the value the thing comes with, or null for no mark.
    public float? DefaultValue { get; set; }

    public bool IsValue => ReadValue != null && WriteValue != null;

    // Actions

    // null for label-only (non-interactive) items
    public Action<ContextMenuItem>? OnPressed { get; set; }

    // if set, pressing this item navigates into this sub-page instead of firing OnPressed
    public ContextMenuPage? SubPage { get; set; }

    // A press as the ring's own button press, for items whose slot has things listening for presses. Ahead of
    // OnPressed.
    public Action<ContextMenuSystem, Button, UIInteractionContext>? OnButton { get; set; }

    // The ring stays open after a press.
    public bool KeepsOpen { get; set; }

    // A press leads to another page that is built only then, so there is no SubPage to show it.
    public bool OpensPage { get; set; }

    // Called every frame while the item shows; true when its label, colour, icon or state changed.
    public Func<ContextMenuItem, bool>? Refresh { get; set; }

    // Polar coordinate layout (set by ContextMenuPage.LayoutItems)

    // degrees, 0 = right, increases clockwise
    public float AngleStart { get; internal set; }

    // degrees
    public float ArcLength { get; internal set; }

    // pixels, distance from center to the inner arc edge
    public float RadiusStart { get; set; } = 42.5f;

    // pixels, inner-to-outer arc width
    public float Thickness { get; set; } = 42.5f;

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

    // What the page lists, when it lists something in the world: opening a page with the same key as one
    // further back goes back to it.
    public object? Key { get; set; }

    public List<ContextMenuItem> Items { get; } = new();

    // A page that is the dial of this value item instead of a ring of items.
    public ContextMenuItem? Dial { get; set; }

    // degrees, gap between adjacent items
    public float SeparationAngle { get; set; } = 6f;

    public ContextMenuPage(string title = "") => Title = title;

    // Fluent builder

    public ContextMenuPage AddItem(ContextMenuItem item)
    {
        Items.Add(item);
        return this;
    }

    // sub-page behind the item labelled `label`, created on first use, so sources adding to one topic
    // share its entry.
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

        // One item is the whole ring.
        float separation = Items.Count > 1 ? SeparationAngle : 0f;
        float totalGap  = separation * Items.Count;
        float available = 360f - totalGap;
        float slice     = available / Items.Count;

        // From nine o'clock, clockwise: left, top, right, bottom.
        float angle = 180f;
        foreach (var item in Items)
        {
            item.AngleStart = angle + separation * 0.5f;
            item.ArcLength  = slice;
            angle += slice + separation;
        }
    }
}
