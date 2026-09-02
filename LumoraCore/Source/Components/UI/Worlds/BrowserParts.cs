// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI.Worlds;

// The handful of primitives the world browser draws with: a rounded panel, a line of text, a chip,
// a pill button. They live here rather than on the shared UIBuilder because the browser is the only
// screen that wants them in this shape and the builder is common ground - a card here must not be
// able to move an inspector row. Colors come from DashTheme, fonts from the dashboard.
//
// Everything the browser writes AFTER build goes through the Set* helpers: Sync fields have no
// equality gate, so writing the same string back still dirties the rect and re-meshes the chunk, and
// this screen rewrites every visible value about once a second off session discovery. -xlinka
internal sealed class BrowserParts
{
    public enum Weight
    {
        Regular,
        Semibold,
        Bold,
    }

    public IAssetProvider<FontSet>? Regular;
    public IAssetProvider<FontSet>? Semibold;
    public IAssetProvider<FontSet>? Bold;

    public IAssetProvider<FontSet>? FontFor(Weight weight) => weight switch
    {
        Weight.Bold => Bold ?? Semibold ?? Regular,
        Weight.Semibold => Semibold ?? Regular,
        _ => Regular,
    };

    // SLOTS

    public static Slot Child(Slot parent, string name)
    {
        var slot = parent.AddSlot(name);
        Fill(slot.AttachComponent<RectTransform>());
        return slot;
    }

    public static RectTransform Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
        return rect;
    }

    public static RectTransform Inset(RectTransform rect, float amount)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = new float2(amount, amount);
        rect.OffsetMax.Value = new float2(-amount, -amount);
        return rect;
    }

    // Anchored to the parent's bottom edge, spanning its width, sitting between y and y + height.
    public static RectTransform PinBottom(RectTransform rect, float y, float height, float sideInset = 0f)
    {
        rect.AnchorMin.Value = new float2(0f, 0f);
        rect.AnchorMax.Value = new float2(1f, 0f);
        rect.OffsetMin.Value = new float2(sideInset, y);
        rect.OffsetMax.Value = new float2(-sideInset, y + height);
        return rect;
    }

    // Anchored to the parent's top edge, spanning its width, y measured DOWN from that edge.
    public static RectTransform PinTop(RectTransform rect, float y, float height, float sideInset = 0f)
    {
        rect.AnchorMin.Value = new float2(0f, 1f);
        rect.AnchorMax.Value = new float2(1f, 1f);
        rect.OffsetMin.Value = new float2(sideInset, -(y + height));
        rect.OffsetMax.Value = new float2(-sideInset, -y);
        return rect;
    }

    // Anchored to the parent's right edge, spanning its height, x measured LEFT from that edge. A
    // NEGATIVE x puts it outside, which is how a rule sits in the gap between two columns instead of
    // tight against one of them.
    public static RectTransform PinRight(RectTransform rect, float x, float width, float endInset = 0f)
    {
        rect.AnchorMin.Value = new float2(1f, 0f);
        rect.AnchorMax.Value = new float2(1f, 1f);
        rect.OffsetMin.Value = new float2(-(x + width), endInset);
        rect.OffsetMax.Value = new float2(-x, -endInset);
        return rect;
    }

    // Anchored to a bottom corner. fromRight flips it to the bottom-right; xOffset walks it inward
    // along the edge, which is how a second chip sits beside the first.
    public static RectTransform PinBottomCorner(RectTransform rect, float inset, float width, float height,
        bool fromRight, float xOffset = 0f)
    {
        float x = fromRight ? 1f : 0f;
        float near = inset + xOffset;
        rect.AnchorMin.Value = new float2(x, 0f);
        rect.AnchorMax.Value = new float2(x, 0f);
        rect.OffsetMin.Value = fromRight
            ? new float2(-(near + width), inset)
            : new float2(near, inset);
        rect.OffsetMax.Value = fromRight
            ? new float2(-near, inset + height)
            : new float2(near + width, inset + height);
        return rect;
    }

    // Anchored to a top corner. fromRight flips it to the top-right.
    public static RectTransform PinTopCorner(RectTransform rect, float inset, float width, float height, bool fromRight)
    {
        float x = fromRight ? 1f : 0f;
        rect.AnchorMin.Value = new float2(x, 1f);
        rect.AnchorMax.Value = new float2(x, 1f);
        rect.OffsetMin.Value = fromRight
            ? new float2(-(inset + width), -(inset + height))
            : new float2(inset, -(inset + height));
        rect.OffsetMax.Value = fromRight
            ? new float2(-inset, -inset)
            : new float2(inset + width, -inset);
        return rect;
    }

    public static LayoutElement Size(Slot slot, float width, float height)
    {
        var element = slot.GetComponent<LayoutElement>() ?? slot.AttachComponent<LayoutElement>();
        element.MinWidth.Value = width;
        element.PreferredWidth.Value = width;
        element.FlexibleWidth.Value = 0f;
        element.MinHeight.Value = height;
        element.PreferredHeight.Value = height;
        element.FlexibleHeight.Value = 0f;
        return element;
    }

    public static LayoutElement Height(Slot slot, float height, float flexibleWidth = 1f)
    {
        var element = slot.GetComponent<LayoutElement>() ?? slot.AttachComponent<LayoutElement>();
        element.MinHeight.Value = height;
        element.PreferredHeight.Value = height;
        element.FlexibleHeight.Value = 0f;
        element.FlexibleWidth.Value = flexibleWidth;
        return element;
    }

    public static LayoutElement Flex(Slot slot, float width, float height)
    {
        var element = slot.GetComponent<LayoutElement>() ?? slot.AttachComponent<LayoutElement>();
        element.FlexibleWidth.Value = width;
        element.FlexibleHeight.Value = height;
        return element;
    }

    // GRAPHICS

    public static RoundedPanel Panel(Slot slot, in color fill, float radius, color? outline = null,
        float thickness = DashTheme.OutlineWidth)
    {
        var panel = slot.AttachComponent<RoundedPanel>();
        panel.Color.Value = fill;
        panel.CornerRadius.Value = radius;
        panel.OutlineColor.Value = outline ?? color.Transparent;
        panel.OutlineThickness.Value = outline.HasValue ? thickness : 0f;
        return panel;
    }

    public Text Label(Slot parent, string content, float size, in color tint, Weight weight,
        TextHorizontalAlignment align = TextHorizontalAlignment.Left,
        float padLeft = 0f, float padRight = 0f, bool wrap = false)
    {
        var slot = parent.AddSlot("Label");
        var rect = slot.AttachComponent<RectTransform>();
        Fill(rect);
        if (padLeft != 0f || padRight != 0f)
        {
            rect.OffsetMin.Value = new float2(padLeft, 0f);
            rect.OffsetMax.Value = new float2(-padRight, 0f);
        }
        var text = slot.AttachComponent<Text>();
        text.Content.Value = content;
        text.Font.Target = FontFor(weight)!;
        text.Size.Value = size;
        text.Color.Value = tint;
        text.HorizontalAlignment.Value = align;
        text.VerticalAlignment.Value = wrap ? TextVerticalAlignment.Top : TextVerticalAlignment.Middle;
        text.WordWrap.Value = wrap;
        return text;
    }

    // A fixed-size state chip. It is never interactive: it says what something IS, and a thing that
    // looks like a button but does nothing is worse than a label.
    public Chip AddChip(Slot parent, string content, in color fill, in color textColor, float width,
        float height = DashTheme.ChipHeight)
    {
        var slot = parent.AddSlot("Chip");
        slot.AttachComponent<RectTransform>();
        Size(slot, width, height);
        var panel = Panel(slot, fill, DashTheme.RadiusChip);
        var label = Label(slot, content, DashTheme.FontLabel, textColor, Weight.Semibold,
            TextHorizontalAlignment.Center);
        return new Chip(slot, panel, label);
    }

    public PillButton AddButton(Slot parent, string content, float width, float height, in color fill,
        in color hover, in color pressed, in color textColor, Weight weight, Action onClick,
        float radius = DashTheme.RadiusControl)
    {
        var slot = parent.AddSlot(content.Length == 0 ? "Button" : content);
        slot.AttachComponent<RectTransform>();
        if (width > 0f)
            Size(slot, width, height);
        else
            Height(slot, height);
        var panel = Panel(slot, fill, radius);
        var button = slot.AttachComponent<Button>();
        var pill = new PillButton(slot, panel, null!, button, fill, hover, pressed);
        button.Clicked += (_, _) => { if (pill.Enabled) onClick(); };
        var driver = button.AddColorDriver(panel.Color, fill, InteractionColorMode.Direct);
        driver.HighlightColor.Value = hover;
        driver.PressedColor.Value = pressed;
        driver.DisabledColor.Value = fill;
        pill.Driver = driver;
        pill.Text = Label(slot, content, DashTheme.FontSmall, textColor, weight, TextHorizontalAlignment.Center);
        pill.NormalTextColor = textColor;
        return pill;
    }

    // WRITES (all equality-gated, see the class note)

    public static void SetText(Text? text, string value)
    {
        if (text == null || text.IsDestroyed)
            return;
        if (!string.Equals(text.Content.Value, value, StringComparison.Ordinal))
            text.Content.Value = value;
    }

    public static void SetColor(Sync<color>? field, in color value)
    {
        if (field == null)
            return;
        if (field.Value != value)
            field.Value = value;
    }

    public static void SetFloat(Sync<float>? field, float value)
    {
        if (field == null)
            return;
        if (field.Value != value)
            field.Value = value;
    }

    public static void SetActive(Slot? slot, bool active)
    {
        if (slot == null || slot.IsDestroyed)
            return;
        if (slot.ActiveSelf.Value != active)
            slot.ActiveSelf.Value = active;
    }

    public static void SetOutline(RoundedPanel? panel, in color outline, float thickness)
    {
        if (panel == null || panel.IsDestroyed)
            return;
        SetColor(panel.OutlineColor, outline);
        SetFloat(panel.OutlineThickness, thickness);
    }

    public static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max - 1) + "…";
}

internal sealed class Chip
{
    public readonly Slot Root;
    public readonly RoundedPanel Panel;
    public readonly Text Text;

    public Chip(Slot root, RoundedPanel panel, Text text)
    {
        Root = root;
        Panel = panel;
        Text = text;
    }

    public void Set(string content, in color fill, in color textColor)
    {
        BrowserParts.SetText(Text, content);
        BrowserParts.SetColor(Panel.Color, fill);
        BrowserParts.SetColor(Text.Color, textColor);
    }
}

// A pill button that can be greyed out. Disabled is a look AND a gate: the click closure checks
// Enabled itself, because a ColorDriver only changes the colour, it does not stop the press.
internal sealed class PillButton
{
    public readonly Slot Root;
    public readonly RoundedPanel Panel;
    public readonly Button Button;
    public Text Text = null!;
    public ColorDriver Driver = null!;
    public color NormalTextColor;

    private color _fill;
    private color _hover;
    private color _pressed;
    private bool _enabled = true;

    public PillButton(Slot root, RoundedPanel panel, Text text, Button button,
        in color fill, in color hover, in color pressed)
    {
        Root = root;
        Panel = panel;
        Text = text;
        Button = button;
        _fill = fill;
        _hover = hover;
        _pressed = pressed;
    }

    public bool Enabled => _enabled;

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled)
            return;
        _enabled = enabled;
        if (Driver != null && !Driver.IsDestroyed)
        {
            BrowserParts.SetColor(Driver.NormalColor, enabled ? _fill : DashTheme.Surface);
            BrowserParts.SetColor(Driver.HighlightColor, enabled ? _hover : DashTheme.Surface);
            BrowserParts.SetColor(Driver.PressedColor, enabled ? _pressed : DashTheme.Surface);
            Driver.Apply();
        }
        BrowserParts.SetColor(Text?.Color, enabled ? NormalTextColor : DashTheme.TextMuted);
    }

    // Recolour a pill (selected / not selected). This goes through the ColorDriver rather than writing
    // Panel.Color, because the driver OWNS that field: a direct write is refused and the pill silently
    // keeps whatever the driver last applied. Only the text colour can be written straight. -xlinka
    public void SetPaint(in color normal, in color hover, in color pressed, in color textColor)
    {
        _fill = normal;
        _hover = hover;
        _pressed = pressed;
        NormalTextColor = textColor;
        if (!_enabled)
            return;
        if (Driver != null && !Driver.IsDestroyed)
        {
            BrowserParts.SetColor(Driver.NormalColor, normal);
            BrowserParts.SetColor(Driver.HighlightColor, hover);
            BrowserParts.SetColor(Driver.PressedColor, pressed);
            BrowserParts.SetColor(Driver.DisabledColor, normal);
            Driver.Apply();
        }
        BrowserParts.SetColor(Text?.Color, textColor);
    }

    public void SetLabel(string content) => BrowserParts.SetText(Text, content);

    public void SetActive(bool active) => BrowserParts.SetActive(Root, active);
}
