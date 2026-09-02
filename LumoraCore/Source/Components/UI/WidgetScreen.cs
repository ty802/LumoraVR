// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Base for dashboard screens that build pill-style rows: shares the palette and the low-level
// row / label / panel / button builders so each screen doesn't re-declare them. Screen-specific
// composites (radios, sliders, sections, custom buttons) live on the screens and call these.
//
// The color fields below are aliases onto DashTheme, kept as names because every screen already
// calls them. Retint here, not per screen. -xlinka
public abstract class WidgetScreen : DashboardScreen
{
    protected const float CornerRadius = DashTheme.RadiusCard;

    protected static readonly color RowFill = DashTheme.Surface;
    protected static readonly color RowBorder = DashTheme.Outline;
    protected static readonly color AccentColor = DashTheme.Accent;
    protected static readonly color ControlFill = DashTheme.Field;
    protected static readonly color TabFill = DashTheme.Surface;
    protected static readonly color TextPrimary = DashTheme.Text;
    protected static readonly color TextDim = DashTheme.TextDim;
    // TextDim, not TextMuted: screens use this at heading size for real headings, and muted at 17
    // reads as disabled. AddSectionLabel below is the small uppercase muted label. -xlinka
    protected static readonly color SectionTitleColor = DashTheme.TextDim;

    protected Dashboard? _dashboard;

    protected virtual float RowHeight => DashTheme.ControlHeight;

    // Body, button/nav, and title faces. Null until the dash resolves, and Text with a null font
    // renders nothing, so always go through the dash rather than leaving a Text unfonted.
    protected IAssetProvider<FontSet>? BodyFont => _dashboard?.Font.Target;
    protected IAssetProvider<FontSet>? SemiboldFont => _dashboard?.FontSemibold.Target ?? _dashboard?.Font.Target;
    protected IAssetProvider<FontSet>? BoldFont => _dashboard?.FontBold.Target ?? _dashboard?.Font.Target;

    protected Dashboard? ResolveDashboard() => _dashboard = Slot.GetComponentInParents<Dashboard>();

    protected void MarkDirty() => _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();

    // A pill row with a horizontal layout, its own graphic chunk, and the standard fill/border.
    protected Slot BeginRow(Slot page, string name)
    {
        var row = page.AddSlot(name);
        row.AttachComponent<RectTransform>();
        row.AttachComponent<GraphicChunkRoot>();
        SetFixedHeight(row, RowHeight);
        ApplyRoundedPanel(row, RowFill, RowBorder);
        var h = row.AttachComponent<HorizontalLayout>();
        h.Spacing.Value = 12f;
        h.PaddingLeft.Value = 12f;
        h.PaddingRight.Value = 12f;
        h.ForceExpandWidth.Value = false;
        h.ForceExpandHeight.Value = true;
        return row;
    }

    protected UIBuilder RowBuilder(Slot row)
    {
        var b = new UIBuilder(row);
        b.Font(_dashboard?.Font.Target)
            .TextColor(TextPrimary)
            .ForegroundColor(AccentColor)
            .BackgroundColor(ControlFill)
            .RoundedSprite(_dashboard?.RoundedSprite);
        return b;
    }

    protected static Text AddRowLabel(UIBuilder builder, string content, float size, color textColor, TextHorizontalAlignment alignment)
    {
        var text = builder.Text(content, size, textColor);
        text.HorizontalAlignment.Value = alignment;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        return text;
    }

    protected Text AddFillLabel(Slot parent, string content, float size, color textColor)
        => AddFillLabel(parent, content, size, textColor, null);

    protected Text AddFillLabel(Slot parent, string content, float size, color textColor, IAssetProvider<FontSet>? font)
    {
        var labelSlot = parent.AddSlot("Label");
        var labelRect = labelSlot.AttachComponent<RectTransform>();
        labelRect.AnchorMin.Value = float2.Zero;
        labelRect.AnchorMax.Value = float2.One;
        labelRect.OffsetMin.Value = float2.Zero;
        labelRect.OffsetMax.Value = float2.Zero;
        var text = labelSlot.AttachComponent<Text>();
        text.Content.Value = content;
        text.Font.Target = font ?? _dashboard?.Font.Target!;
        text.Size.Value = size;
        text.Color.Value = textColor;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        return text;
    }

    // Uppercase muted micro-label that separates groups of rows. Not a row: no fill, no outline,
    // it just sits in the column flow above the thing it names.
    protected Text AddSectionLabel(Slot parent, string title)
    {
        var row = parent.AddSlot(title + "SectionLabel");
        row.AttachComponent<RectTransform>();
        SetFixedHeight(row, 22f);
        var text = AddFillLabel(row, title.ToUpperInvariant(), DashTheme.FontLabel, DashTheme.TextMuted, SemiboldFont);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        return text;
    }

    protected BorderedImage ApplyRoundedPanel(Slot slot, color fill, color border)
    {
        var image = slot.AttachComponent<BorderedImage>();
        image.Tint.Value = fill;
        image.BorderTint.Value = border;
        // Hairline. The old 2-unit default is what turned every nested box into a double outline.
        image.BorderThickness.Value = DashTheme.OutlineWidth;
        var rounded = _dashboard?.RoundedSprite;
        if (rounded != null)
        {
            image.Texture.Target = rounded;
            image.NineSlice.Value = true;
            image.Borders.Value = new float4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
        }
        return image;
    }

    // Card chrome at an arbitrary corner radius: fill + one hairline outline, nine-sliced off a
    // sprite cut for that exact radius. RoundedPanel renders fine now (its corner fan winding was the
    // bug); this stays on BorderedImage because every card in the dash shares one sprite per radius
    // and re-cutting them all for no visual gain is not worth it. -xlinka
    protected BorderedImage ApplyCard(Slot slot, color fill, color outline, float radius)
    {
        var image = slot.GetComponent<BorderedImage>() ?? slot.AttachComponent<BorderedImage>();
        bool hasOutline = outline.a > 0.001f;
        image.Tint.Value = fill;
        image.BorderTint.Value = hasOutline ? outline : new color(0f, 0f, 0f, 0f);
        image.BorderThickness.Value = hasOutline ? DashTheme.OutlineWidth : 0f;
        var rounded = _dashboard?.RoundedSpriteFor(radius);
        if (rounded != null)
        {
            image.Texture.Target = rounded;
            image.NineSlice.Value = true;
            image.Borders.Value = new float4(radius, radius, radius, radius);
        }
        return image;
    }

    // Hover/press states off the token ramp. AddColorDriver derives its own ramp from the base color,
    // which lands somewhere arbitrary for near-black fills, so all four get written.
    //
    // DisabledColor matters more than it looks: a screen builds its content while its slot is still
    // INACTIVE, CanInteract is false until it is shown, so the first Apply paints the disabled color and
    // nothing re-applies until you hover the thing. Leave the derived grey in there and every card on
    // every screen opens as a flat grey slab. None of these buttons is ever really disabled, so the
    // disabled color is the normal one. -xlinka
    protected static ColorDriver DriveCardStates(Button button, BorderedImage panel,
        color normal, color hover, color pressed)
    {
        var driver = button.AddColorDriver(panel.Tint, normal, InteractionColorMode.Direct);
        driver.NormalColor.Value = normal;
        driver.HighlightColor.Value = hover;
        driver.PressedColor.Value = pressed;
        driver.DisabledColor.Value = normal;
        driver.Apply();
        return driver;
    }

    // A fixed-width pill button cell inside a row.
    protected Slot AddInlineButton(Slot row, string label, color fill, float width, Action onClick)
    {
        var cell = row.AddSlot(label);
        cell.AttachComponent<RectTransform>();
        var element = cell.AttachComponent<LayoutElement>();
        element.MinWidth.Value = width;
        element.PreferredWidth.Value = width;
        element.FlexibleWidth.Value = 0f;
        element.FlexibleHeight.Value = 1f;
        ApplyRoundedPanel(cell, fill, RowBorder);
        var button = cell.AttachComponent<Button>();
        button.Clicked += (_, _) => onClick();
        AddFillLabel(cell, label, DashTheme.FontBody, OnFill(fill), SemiboldFont);
        return cell;
    }

    // Text that has to sit on a filled button or chip, picked off the fill's luminance instead of
    // hardcoded. White on the violet accent is fine, white on the amber warning is nearly invisible,
    // and one rule beats guessing per call site.
    //
    // DashTheme tokens are already linear (decoded once at definition), and relative luminance is
    // defined on linear channels, so the sum takes them as they are. 0.35 linear is roughly 0.63 in
    // sRGB: the accent (0.27) keeps white text, Positive and Warning (0.5+) flip to dark. -xlinka
    public static color OnFill(in color fill)
        => 0.2126f * fill.r + 0.7152f * fill.g + 0.0722f * fill.b > 0.35f ? DashTheme.TextOnLight : DashTheme.Text;

    protected static void SetFixedHeight(Slot slot, float height)
    {
        var element = slot.GetComponent<LayoutElement>() ?? slot.AttachComponent<LayoutElement>();
        element.MinHeight.Value = height;
        element.PreferredHeight.Value = height;
        element.FlexibleHeight.Value = 0f;
    }
}
