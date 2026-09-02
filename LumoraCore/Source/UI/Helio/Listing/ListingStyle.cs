// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Helio.UI.Listing;

// Palette + metrics handed to every template so a screen's rows all come out of one place. Defaults
// are the dashboard house dark theme; a screen overrides what it needs before building.
//
// Font is not optional in practice: text with no font renders NOTHING, so a template that forgets to
// pull it off here produces an invisible row rather than an ugly one. -xlinka
public sealed class ListingStyle
{
    public IAssetProvider<FontSet>? Font;
    public IAssetProvider<TextureAsset>? RoundedSprite;

    public color RowFill = new color(0.20f, 0.19f, 0.30f, 0.85f);
    public color RowBorder = new color(0.40f, 0.36f, 0.62f, 0.35f);
    public color RowSelectedFill = new color(0.30f, 0.27f, 0.46f, 0.95f);
    public color ControlFill = new color(0.33f, 0.30f, 0.46f, 1f);
    public color NeutralFill = new color(0.22f, 0.20f, 0.34f, 0.70f);
    public color AccentFill = new color(0.45f, 0.38f, 0.80f, 0.90f);
    public color WarningFill = new color(0.70f, 0.24f, 0.28f, 0.95f);
    public color DisabledFill = new color(0.16f, 0.15f, 0.20f, 0.55f);

    public color Accent = new color(0.62f, 0.55f, 0.95f, 1f);
    public color TextPrimary = new color(0.93f, 0.93f, 0.97f, 1f);
    public color TextDim = new color(0.72f, 0.72f, 0.80f, 1f);
    public color TextDisabled = new color(0.50f, 0.50f, 0.56f, 1f);
    public color HeaderText = new color(0.80f, 0.76f, 0.97f, 1f);

    public float CornerRadius = 12f;
    public float RowHeight = 40f;
    public float RowSpacing = 6f;
    public float RowPadding = 0f;
    public float LabelWidth = 240f;
    public float ValueWidth = 100f;
    public float LabelSize = 18f;
    public float ValueSize = 16f;

    public UIBuilder RowBuilder(Slot row)
    {
        var builder = new UIBuilder(row);
        builder.Font(Font)
            .TextColor(TextPrimary)
            .ForegroundColor(Accent)
            .BackgroundColor(ControlFill)
            .RoundedSprite(RoundedSprite);
        return builder;
    }

    public BorderedImage ApplyPanel(Slot slot, color fill, color border)
    {
        var image = slot.GetComponent<BorderedImage>() ?? slot.AttachComponent<BorderedImage>();
        image.Tint.Value = fill;
        image.BorderTint.Value = border;
        if (RoundedSprite != null)
        {
            image.Texture.Target = RoundedSprite;
            image.NineSlice.Value = true;
            image.Borders.Value = new float4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
        }
        return image;
    }

    public Text Label(UIBuilder builder, string content, float size, color textColor, TextHorizontalAlignment alignment)
    {
        var text = builder.Text(content, size, textColor);
        text.HorizontalAlignment.Value = alignment;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        return text;
    }

    public Text FillLabel(Slot parent, string content, float size, color textColor)
    {
        var labelSlot = parent.AddSlot("Label");
        var rect = labelSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
        var text = labelSlot.AttachComponent<Text>();
        text.Content.Value = content;
        text.Font.Target = Font!;
        text.Size.Value = size;
        text.Color.Value = textColor;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        return text;
    }

    // EQUALITY-GATED WRITES
    //
    // Sync fields have no equality gate of their own: an unchanged write still dirties the rect and
    // re-meshes the owning chunk. A listing rebinds every visible row whenever anything asks it to
    // (a periodic value refresh, a selection change), so an ungated Bind would re-tessellate the
    // whole visible page several times a second for values that never moved. Templates write through
    // these. -xlinka
    public static void SetText(Text text, string value)
    {
        if (text.Content.Value != value)
            text.Content.Value = value;
    }

    public static void SetTextColor(Text text, in color value)
    {
        if (!text.Color.Value.Equals(value))
            text.Color.Value = value;
    }

    public static void SetTint(BorderedImage image, in color value)
    {
        if (!image.Tint.Value.Equals(value))
            image.Tint.Value = value;
    }

    public static void SetActive(Slot slot, bool active)
    {
        if (slot.ActiveSelf.Value != active)
            slot.ActiveSelf.Value = active;
    }

    public static void SetInteractable(InteractionElement? element, bool value)
    {
        if (element != null && element.Interactable.Value != value)
            element.Interactable.Value = value;
    }

    public static void SetSlider(Slider slider, float min, float max, float value)
    {
        bool changed = false;
        if (slider.Min.Value != min) { slider.Min.Value = min; changed = true; }
        if (slider.Max.Value != max) { slider.Max.Value = max; changed = true; }
        if (slider.Value.Value != value) { slider.Value.Value = value; changed = true; }
        if (changed)
            slider.UpdateHandleDrives();
    }

    public static void SetFixedWidth(Slot slot, float width)
    {
        var element = slot.GetComponent<Layout.LayoutElement>() ?? slot.AttachComponent<Layout.LayoutElement>();
        element.MinWidth.Value = width;
        element.PreferredWidth.Value = width;
        element.FlexibleWidth.Value = 0f;
        element.FlexibleHeight.Value = 1f;
    }
}
