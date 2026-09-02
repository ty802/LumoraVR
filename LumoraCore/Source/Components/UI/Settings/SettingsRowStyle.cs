// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Geometry every settings row shares. Rows anchor their own columns instead of running a layout
// controller: the label share has to be a fraction of the row (the panel width moves with the dash)
// and a LayoutElement only speaks in absolute units, which is how the old rows ended up wrapping
// their labels into two lines. -xlinka
internal static class SettingsMetrics
{
    public const float RowHeight = 44f;
    public const float SectionHeight = 34f;
    public const float NavHeight = 34f;
    public const float NavSpacing = 4f;

    public const float RowInset = 14f;
    public const float LabelSplit = 0.34f;
    public const float LabelGutter = 10f;
    public const float HintHeight = 15f;
    // Long labels shrink rather than wrap or bleed into the control column. Below this they would stop
    // being readable, so anything that still does not fit is simply cut off by the label column.
    public const float LabelMinSize = 11f;

    public const float ValueWidth = 88f;
    public const float ValueGap = 12f;

    // Controls sit on a 28-unit band centred in the row, so a pill strip and a slider line up with each
    // other and with the switch.
    public const float ControlBand = 28f;
    public const float SliderBand = 24f;

    public const float SegmentGap = 6f;
    public const float ActionWidth = 132f;

    public const float TitleHeight = 52f;
    public const float SidebarWidth = 172f;
    public const float PanelInset = 14f;

    // Binding rows carry six cells across, so they get their own column table instead of the shared
    // label share. The binding text is what needs the room.
    public const float BindLabelWidth = 200f;
    public const float BindDesktopWidth = 200f;
    public const float BindPadWidth = 176f;
    public const float BindVRWidth = 150f;
    public const float BindClearWidth = 58f;
    public const float BindResetWidth = 68f;
}

// The three weights the dashboard resolves, with a fallback chain. A Text with a null font renders
// NOTHING, not a default face, so a screen built before the bold provider resolved would come up blank
// rather than in the wrong weight. -xlinka
internal sealed class SettingsFonts
{
    public IAssetProvider<FontSet>? Regular;
    public IAssetProvider<FontSet>? Semibold;
    public IAssetProvider<FontSet>? Bold;

    public IAssetProvider<FontSet>? Body => Regular ?? Semibold ?? Bold;
    public IAssetProvider<FontSet>? Medium => Semibold ?? Regular ?? Bold;
    public IAssetProvider<FontSet>? Strong => Bold ?? Semibold ?? Regular;
}

// Construction and paint helpers for the settings rows.
//
// Every setter here compares before it writes. The screen rebinds each visible row ten times a second
// and a Sync write carries no equality gate of its own: an unchanged write still dirties the rect and
// re-meshes the row's chunk, so an ungated repaint is a re-tessellation per tick for values that never
// moved. -xlinka
internal static class SettingsUI
{
    public static RectTransform Rect(Slot slot)
        => slot.GetComponent<RectTransform>() ?? slot.AttachComponent<RectTransform>();

    public static Slot Child(Slot parent, string name, in float2 anchorMin, in float2 anchorMax,
        in float2 offsetMin, in float2 offsetMax)
    {
        var slot = parent.AddSlot(name);
        var rect = slot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = anchorMin;
        rect.AnchorMax.Value = anchorMax;
        rect.OffsetMin.Value = offsetMin;
        rect.OffsetMax.Value = offsetMax;
        return slot;
    }

    public static Slot Fill(Slot parent, string name, float left = 0f, float bottom = 0f, float right = 0f, float top = 0f)
        => Child(parent, name, float2.Zero, float2.One, new float2(left, bottom), new float2(-right, -top));

    // A horizontal band of fixed height centred in the parent.
    public static Slot Band(Slot parent, string name, float height, float left, float right)
        => Child(parent, name, new float2(0f, 0.5f), new float2(1f, 0.5f),
            new float2(left, -height * 0.5f), new float2(-right, height * 0.5f));

    // A fixed-width column measured from the parent's left edge.
    public static Slot Column(Slot parent, string name, float left, float width, float inset = 0f)
        => Child(parent, name, new float2(0f, 0f), new float2(0f, 1f),
            new float2(left, inset), new float2(left + width, -inset));

    public static RoundedPanel Panel(Slot slot, in color fill, in color outline, float radius)
    {
        var panel = slot.GetComponent<RoundedPanel>() ?? slot.AttachComponent<RoundedPanel>();
        panel.Color.Value = fill;
        panel.OutlineColor.Value = outline;
        panel.CornerRadius.Value = radius;
        panel.OutlineThickness.Value = outline.a > 0f ? DashTheme.OutlineWidth : 0f;
        return panel;
    }

    public static Text Label(Slot parent, string name, IAssetProvider<FontSet>? font, float size, in color tint,
        TextHorizontalAlignment align, in float2 anchorMin, in float2 anchorMax, in float2 offsetMin, in float2 offsetMax,
        bool shrinkToFit = false)
    {
        var slot = Child(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
        var text = slot.AttachComponent<Text>();
        text.Font.Target = font!;
        text.Size.Value = size;
        text.Color.Value = tint;
        text.HorizontalAlignment.Value = align;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        text.WordWrap.Value = false;
        if (shrinkToFit)
        {
            text.AutoSizeHorizontal.Value = true;
            text.AutoSizeMin.Value = SettingsMetrics.LabelMinSize;
        }
        return text;
    }

    public static Text FillLabel(Slot parent, string name, IAssetProvider<FontSet>? font, float size, in color tint,
        TextHorizontalAlignment align = TextHorizontalAlignment.Center, bool shrinkToFit = false)
        => Label(parent, name, font, size, tint, align, float2.Zero, float2.One, float2.Zero, float2.Zero, shrinkToFit);

    // Hairline rule. A plain Image is one quad; a rounded panel one unit tall would tessellate corner
    // arcs for nothing.
    public static Image Divider(Slot parent, string name, float height = 1f)
    {
        var slot = Child(parent, name, new float2(0f, 0f), new float2(1f, 0f), float2.Zero, new float2(0f, height));
        var image = slot.AttachComponent<Image>();
        image.Tint.Value = DashTheme.Divider;
        return image;
    }

    public static void SetPaint(RoundedPanel panel, in color fill, in color outline)
        => SetPaint(panel, fill, outline, DashTheme.OutlineWidth);

    // ring is the outline width to use when the outline is actually visible. Anything above the hairline
    // is a deliberate "this one is the answer" marker, not chrome.
    public static void SetPaint(RoundedPanel panel, in color fill, in color outline, float ring)
    {
        if (!panel.Color.Value.Equals(fill))
            panel.Color.Value = fill;
        if (!panel.OutlineColor.Value.Equals(outline))
            panel.OutlineColor.Value = outline;
        float thickness = outline.a > 0f ? ring : 0f;
        if (panel.OutlineThickness.Value != thickness)
            panel.OutlineThickness.Value = thickness;
    }

    public static void SetFill(RoundedPanel panel, in color fill)
    {
        if (!panel.Color.Value.Equals(fill))
            panel.Color.Value = fill;
    }

    public static void SetTint(Image image, in color value)
    {
        if (!image.Tint.Value.Equals(value))
            image.Tint.Value = value;
    }

    public static void SetOffsets(RectTransform rect, in float2 offsetMin, in float2 offsetMax)
    {
        if (!rect.OffsetMin.Value.Equals(offsetMin))
            rect.OffsetMin.Value = offsetMin;
        if (!rect.OffsetMax.Value.Equals(offsetMax))
            rect.OffsetMax.Value = offsetMax;
    }

    public static void SetAnchors(RectTransform rect, in float2 anchorMin, in float2 anchorMax)
    {
        if (!rect.AnchorMin.Value.Equals(anchorMin))
            rect.AnchorMin.Value = anchorMin;
        if (!rect.AnchorMax.Value.Equals(anchorMax))
            rect.AnchorMax.Value = anchorMax;
    }
}

// A pill: rounded fill, hairline outline, one line of text, a click target. Used for the segmented
// enum strips, the locomotion strip, the binding cells and the row buttons, so all four pick up the
// same hover and disabled treatment.
internal sealed class SettingsChip
{
    public readonly Slot Slot;
    public readonly RoundedPanel Panel;
    public readonly Text Text;
    public readonly Button Button;

    private color _fill = DashTheme.Surface;
    private color _hoverFill = DashTheme.SurfaceHover;
    private color _outline = DashTheme.Outline;
    private float _ring = DashTheme.OutlineWidth;
    private bool _hovered;

    private SettingsChip(Slot slot, RoundedPanel panel, Text text, Button button)
    {
        Slot = slot;
        Panel = panel;
        Text = text;
        Button = button;
    }

    public static SettingsChip Build(Slot host, IAssetProvider<FontSet>? font, float fontSize, float radius)
    {
        // Panel before Button: Button.OnAttach adopts an Image on its slot into a color driver, and a
        // driven tint cannot then be written from Bind. RoundedPanel is not an Image, so the paint
        // stays ours. -xlinka
        var panel = SettingsUI.Panel(host, DashTheme.Surface, DashTheme.Outline, radius);
        var button = host.AttachComponent<Button>();
        // Shrink to fit rather than bleed past the pill: a binding cell can hold "D-Pad Up, D-Pad Down".
        var text = SettingsUI.FillLabel(host, "Text", font, fontSize, DashTheme.Text,
            TextHorizontalAlignment.Center, shrinkToFit: true);
        var chip = new SettingsChip(host, panel, text, button);
        button.HoverEntered += _ => chip.SetHovered(true);
        button.HoverExited += _ => chip.SetHovered(false);
        return chip;
    }

    public void SetPaint(in color fill, in color hoverFill, in color outline, in color textColor,
        float ring = DashTheme.OutlineWidth)
    {
        _fill = fill;
        _hoverFill = hoverFill;
        _outline = outline;
        _ring = ring;
        ListingStyle.SetTextColor(Text, textColor);
        Repaint();
    }

    public void SetHovered(bool value)
    {
        if (_hovered == value)
            return;
        _hovered = value;
        Repaint();
    }

    public void SetInteractable(bool value)
    {
        ListingStyle.SetInteractable(Button, value);
        if (!value && _hovered)
        {
            _hovered = false;
            Repaint();
        }
    }

    private void Repaint()
        => SettingsUI.SetPaint(Panel, _hovered && Button.Interactable.Value ? _hoverFill : _fill, _outline, _ring);
}
