// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Base for dashboard screens that build pill-style rows: shares the color palette and the
/// low-level row / label / panel / button builders so each screen doesn't re-declare them.
/// Screen-specific composites (radios, sliders, sections, custom buttons) live on the screens
/// and call these. Override <see cref="RowHeight"/> to change the default row height.
/// </summary>
public abstract class WidgetScreen : DashboardScreen
{
    protected const float CornerRadius = 12f;

    protected static readonly color RowFill = new color(0.20f, 0.19f, 0.30f, 0.85f);
    protected static readonly color RowBorder = new color(0.40f, 0.36f, 0.62f, 0.35f);
    protected static readonly color AccentColor = new color(0.62f, 0.55f, 0.95f, 1f);
    protected static readonly color ControlFill = new color(0.33f, 0.30f, 0.46f, 1f);
    protected static readonly color TabFill = new color(0.22f, 0.20f, 0.34f, 0.70f);
    protected static readonly color TextPrimary = new color(0.93f, 0.93f, 0.97f, 1f);
    protected static readonly color TextDim = new color(0.72f, 0.72f, 0.80f, 1f);
    protected static readonly color SectionTitleColor = new color(0.80f, 0.76f, 0.97f, 1f);

    protected Dashboard? _dashboard;

    /// <summary>Default fixed height for <see cref="BeginRow"/> rows.</summary>
    protected virtual float RowHeight => 34f;

    /// <summary>Resolve and cache the owning dashboard (call once at the top of BuildContent).</summary>
    protected Dashboard? ResolveDashboard() => _dashboard = Slot.GetComponentInParents<Dashboard>();

    /// <summary>Mark the dashboard canvas dirty so a rebuild re-renders.</summary>
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
    {
        var labelSlot = parent.AddSlot("Label");
        var labelRect = labelSlot.AttachComponent<RectTransform>();
        labelRect.AnchorMin.Value = float2.Zero;
        labelRect.AnchorMax.Value = float2.One;
        labelRect.OffsetMin.Value = float2.Zero;
        labelRect.OffsetMax.Value = float2.Zero;
        var text = labelSlot.AttachComponent<Text>();
        text.Content.Value = content;
        text.Font.Target = _dashboard?.Font.Target!;
        text.Size.Value = size;
        text.Color.Value = textColor;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        return text;
    }

    protected BorderedImage ApplyRoundedPanel(Slot slot, color fill, color border)
    {
        var image = slot.AttachComponent<BorderedImage>();
        image.Tint.Value = fill;
        image.BorderTint.Value = border;
        var rounded = _dashboard?.RoundedSprite;
        if (rounded != null)
        {
            image.Texture.Target = rounded;
            image.NineSlice.Value = true;
            image.Borders.Value = new float4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
        }
        return image;
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
        AddFillLabel(cell, label, 14f, TextPrimary);
        return cell;
    }

    protected static void SetFixedHeight(Slot slot, float height)
    {
        var element = slot.GetComponent<LayoutElement>() ?? slot.AttachComponent<LayoutElement>();
        element.MinHeight.Value = height;
        element.PreferredHeight.Value = height;
        element.FlexibleHeight.Value = 0f;
    }
}
