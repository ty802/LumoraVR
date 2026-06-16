// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Dashboard settings screen: category sidebar on the left, grouped section
/// cards on the right, backed by EngineSettings (persisted; the platform layer
/// applies vsync/window/audio). Rows are chunk-isolated so slider drags only
/// re-render their own row.
/// </summary>
public sealed class SettingsScreen : DashboardScreen
{
    private const float RowHeight = 40f;
    private const float RowSpacing = 6f;
    private const float SectionTitleHeight = 30f;
    private const float SectionPad = 14f;
    private const float CornerRadius = 12f;
    private const float SidebarWidth = 190f;

    private static readonly color CardFill = new color(0.14f, 0.13f, 0.21f, 0.96f);
    private static readonly color CardBorder = new color(0.52f, 0.46f, 0.82f, 0.50f);
    private static readonly color RowFill = new color(0.20f, 0.19f, 0.30f, 0.85f);
    private static readonly color RowBorder = new color(0.40f, 0.36f, 0.62f, 0.35f);
    private static readonly color CategoryFill = new color(0.22f, 0.20f, 0.34f, 0.70f);
    private static readonly color CategoryActiveFill = new color(0.45f, 0.38f, 0.80f, 0.90f);
    private static readonly color AccentColor = new color(0.62f, 0.55f, 0.95f, 1f);
    private static readonly color TextPrimary = new color(0.93f, 0.93f, 0.97f, 1f);
    private static readonly color TextDim = new color(0.72f, 0.72f, 0.80f, 1f);
    private static readonly color SectionTitleColor = new color(0.80f, 0.76f, 0.97f, 1f);

    private Dashboard? _dashboard;
    private readonly List<(Slot content, BorderedImage buttonBackground)> _categories = new();

    protected override void BuildContent(UIBuilder builder)
    {
        _dashboard = Slot.GetComponentInParents<Dashboard>();
        _categories.Clear();

        // FileBrowser-style structure: layout components attached directly to
        // filling slots; every layout child carries an explicit LayoutElement.
        var root = builder.Current;
        var split = root.AttachComponent<HorizontalLayout>();
        split.Spacing.Value = 14f;
        split.PaddingLeft.Value = 16f;
        split.PaddingRight.Value = 16f;
        split.PaddingTop.Value = 16f;
        split.PaddingBottom.Value = 16f;
        split.ForceExpandWidth.Value = false;
        split.ForceExpandHeight.Value = true;

        var sidebar = BuildSidebar(root);
        var contentHost = BuildContentHost(root);

        BuildCategory(sidebar, contentHost, "Input", page =>
        {
            var mouse = BeginSection(page, "Mouse", rowCount: 2);
            SliderRow(mouse, "Sensitivity", 0.1f, 5f, EngineSettings.MouseSensitivity,
                v => { EngineSettings.MouseSensitivity = v; return $"{EngineSettings.MouseSensitivity:0.00}x"; });
            SliderRow(mouse, "Smoothing", 0f, 0.9f, EngineSettings.MouseSmoothing,
                v => { EngineSettings.MouseSmoothing = v; return EngineSettings.MouseSmoothing <= 0.001f ? "Off" : $"{EngineSettings.MouseSmoothing:0.00}"; });

            var movement = BeginSection(page, "Movement", rowCount: 1);
            SliderRow(movement, "Noclip Speed", 1f, 30f, EngineSettings.NoclipSpeed,
                v => { EngineSettings.NoclipSpeed = v; return $"{EngineSettings.NoclipSpeed:0.#} m/s"; });
        });

        BuildCategory(sidebar, contentHost, "Audio", page =>
        {
            var section = BeginSection(page, "Volume", rowCount: 1);
            SliderRow(section, "Master Volume", 0f, 1f, EngineSettings.MasterVolume,
                v => { EngineSettings.MasterVolume = v; return $"{EngineSettings.MasterVolume * 100f:0}%"; });
        });

        BuildCategory(sidebar, contentHost, "Video", page =>
        {
            var display = BeginSection(page, "Display", rowCount: 3);
            ToggleRow(display, "VSync", EngineSettings.VSync, v => EngineSettings.VSync = v);
            ToggleRow(display, "Fullscreen", EngineSettings.Fullscreen, v => EngineSettings.Fullscreen = v);
            SliderRow(display, "FPS Limit", 0f, 240f, EngineSettings.MaxFps,
                v =>
                {
                    int fps = (int)MathF.Round(v / 10f) * 10;
                    EngineSettings.MaxFps = fps;
                    return EngineSettings.MaxFps == 0 ? "Off" : EngineSettings.MaxFps.ToString();
                });

            var quality = BeginSection(page, "Quality", rowCount: 1);
            SliderRow(quality, "Render Scale", 0.5f, 1.5f, EngineSettings.RenderScale,
                v =>
                {
                    // Snap to 5% steps so the viewport isn't re-allocated per pixel of drag.
                    EngineSettings.RenderScale = MathF.Round(v * 20f) / 20f;
                    return $"{EngineSettings.RenderScale * 100f:0}%";
                });
        });

        BuildCategory(sidebar, contentHost, "Dashboard", page =>
        {
            var placement = BeginSection(page, "Placement", rowCount: 1);
            // Freeform leaves the panel where you put it (grab to move) instead of
            // pinning it in front of your view. VR only; desktop is window-projected.
            ToggleRow(placement, "Freeform (place & stay)",
                UserspaceDashboard.LocalInstance?.Freeform.Value ?? false,
                v => UserspaceDashboard.LocalInstance?.SetFreeform(v));

            var widgets = BeginSection(page, "Widgets", rowCount: 1);
            // Edit mode boosts every spawned widget panel's grab above its canvas so
            // you can pick it up and place it; off, the canvas takes clicks again.
            ToggleRow(widgets, "Edit Widgets (grab to move)",
                WidgetPanel.EditMode,
                v => WidgetPanel.EditMode = v);
        });

        SelectCategory(0);
    }

    // LAYOUT SCAFFOLDING

    private Slot BuildSidebar(Slot root)
    {
        var sidebar = root.AddSlot("Sidebar");
        sidebar.AttachComponent<RectTransform>();
        var element = sidebar.AttachComponent<LayoutElement>();
        element.MinWidth.Value = SidebarWidth;
        element.PreferredWidth.Value = SidebarWidth;
        element.FlexibleWidth.Value = 0f;
        element.FlexibleHeight.Value = 1f;

        ApplyRoundedPanel(sidebar, CardFill, CardBorder);

        var v = sidebar.AttachComponent<VerticalLayout>();
        v.Spacing.Value = 6f;
        v.PaddingLeft.Value = 10f;
        v.PaddingRight.Value = 10f;
        v.PaddingTop.Value = 12f;
        v.PaddingBottom.Value = 12f;
        v.ForceExpandWidth.Value = true;
        v.ForceExpandHeight.Value = false;

        return sidebar;
    }

    private static Slot BuildContentHost(Slot root)
    {
        var host = root.AddSlot("Content");
        host.AttachComponent<RectTransform>();
        var element = host.AttachComponent<LayoutElement>();
        element.FlexibleWidth.Value = 1f;
        element.FlexibleHeight.Value = 1f;
        return host;
    }

    private void BuildCategory(Slot sidebar, Slot contentHost, string name, Action<Slot> buildPage)
    {
        int index = _categories.Count;

        // Sidebar button
        var buttonSlot = sidebar.AddSlot(name);
        buttonSlot.AttachComponent<RectTransform>();
        SetFixedHeight(buttonSlot, 42f);
        var background = buttonSlot.AttachComponent<BorderedImage>();
        background.Tint.Value = CategoryFill;
        background.BorderTint.Value = RowBorder;
        var roundedSprite = _dashboard?.RoundedSprite;
        if (roundedSprite != null)
        {
            background.Texture.Target = roundedSprite;
            background.NineSlice.Value = true;
            background.Borders.Value = new float4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
        }

        var button = buttonSlot.AttachComponent<Button>();
        button.Clicked += (_, _) => SelectCategory(index);

        var labelSlot = buttonSlot.AddSlot("Label");
        var labelRect = labelSlot.AttachComponent<RectTransform>();
        labelRect.AnchorMin.Value = float2.Zero;
        labelRect.AnchorMax.Value = float2.One;
        labelRect.OffsetMin.Value = float2.Zero;
        labelRect.OffsetMax.Value = float2.Zero;
        var label = labelSlot.AttachComponent<Text>();
        label.Content.Value = name;
        label.Font.Target = _dashboard?.Font.Target!;
        label.Size.Value = 18f;
        label.Color.Value = TextPrimary;
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        label.VerticalAlignment.Value = TextVerticalAlignment.Middle;

        // Page filling the host; toggled by SelectCategory. Offsets must be
        // zeroed - default RectTransform offsets are +/-50, which would make the
        // page 100px larger than the host and overflow the panel.
        var page = contentHost.AddSlot(name);
        var pageRect = page.AttachComponent<RectTransform>();
        pageRect.AnchorMin.Value = float2.Zero;
        pageRect.AnchorMax.Value = float2.One;
        pageRect.OffsetMin.Value = float2.Zero;
        pageRect.OffsetMax.Value = float2.Zero;
        page.ActiveSelf.Value = false;

        var v = page.AttachComponent<VerticalLayout>();
        v.Spacing.Value = 12f;
        v.ForceExpandWidth.Value = true;
        v.ForceExpandHeight.Value = false;

        _categories.Add((page, background));
        buildPage(page);
    }

    private void SelectCategory(int index)
    {
        for (int i = 0; i < _categories.Count; i++)
        {
            var (content, buttonBackground) = _categories[i];
            if (content != null && !content.IsDestroyed)
                content.ActiveSelf.Value = i == index;
            if (buttonBackground != null && !buttonBackground.IsDestroyed)
                buttonBackground.Tint.Value = i == index ? CategoryActiveFill : CategoryFill;
        }

        // Activating a page doesn't dirty the canvas by itself, leaving its
        // chunks unrendered until something else (hover) does.
        _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    // SECTIONS AND ROWS

    private Slot BeginSection(Slot page, string title, int rowCount)
    {
        float height = SectionPad * 2f + SectionTitleHeight + rowCount * (RowHeight + RowSpacing);

        var card = page.AddSlot(title);
        card.AttachComponent<RectTransform>();
        SetFixedHeight(card, height);
        ApplyRoundedPanel(card, CardFill, CardBorder);

        var v = card.AttachComponent<VerticalLayout>();
        v.Spacing.Value = RowSpacing;
        v.PaddingLeft.Value = SectionPad;
        v.PaddingRight.Value = SectionPad;
        v.PaddingTop.Value = SectionPad;
        v.PaddingBottom.Value = SectionPad;
        v.ForceExpandWidth.Value = true;
        v.ForceExpandHeight.Value = false;

        var titleSlot = card.AddSlot("Title");
        titleSlot.AttachComponent<RectTransform>();
        SetFixedHeight(titleSlot, SectionTitleHeight);
        var titleText = titleSlot.AttachComponent<Text>();
        titleText.Content.Value = title;
        titleText.Font.Target = _dashboard?.Font.Target!;
        titleText.Size.Value = 20f;
        titleText.Color.Value = SectionTitleColor;
        titleText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        titleText.VerticalAlignment.Value = TextVerticalAlignment.Middle;

        return card;
    }

    private void SliderRow(Slot section, string label, float min, float max, float value, Func<float, string> applyAndFormat)
    {
        var row = BeginRow(section, label);
        var b = RowBuilder(row);

        b.MinWidth(240f).PreferredWidth(240f).FlexibleWidth(0f);
        AddRowLabel(b, label, 18f, TextPrimary, TextHorizontalAlignment.Left);

        Text? valueText = null;
        var slider = b.Slider(value, min, max, (_, v) =>
        {
            var formatted = applyAndFormat(v);
            if (valueText != null && !valueText.IsDestroyed)
                valueText.Content.Value = formatted;
        });
        // Slider() hard-sets a fixed 96px width (FlexibleWidth=0); override so the
        // track fills the row cell instead of rendering as a short stub.
        var sliderLayout = slider.Slot.GetComponent<LayoutElement>() ?? slider.Slot.AttachComponent<LayoutElement>();
        sliderLayout.MinWidth.Value = 120f;
        sliderLayout.PreferredWidth.Value = 240f;
        sliderLayout.FlexibleWidth.Value = 1f;

        b.MinWidth(100f).PreferredWidth(100f).FlexibleWidth(0f);
        valueText = AddRowLabel(b, applyAndFormat(value), 16f, TextDim, TextHorizontalAlignment.Right);
    }

    private void ToggleRow(Slot section, string label, bool value, Action<bool> apply)
    {
        var row = BeginRow(section, label);
        var b = RowBuilder(row);

        b.MinWidth(240f).PreferredWidth(240f).FlexibleWidth(0f);
        AddRowLabel(b, label, 18f, TextPrimary, TextHorizontalAlignment.Left);

        Text? stateText = null;
        b.MinWidth(28f).PreferredWidth(28f).FlexibleWidth(0f);
        b.Checkbox(value, (_, isChecked) =>
        {
            apply(isChecked);
            if (stateText != null && !stateText.IsDestroyed)
                stateText.Content.Value = isChecked ? "On" : "Off";
        });

        b.MinWidth(100f).FlexibleWidth(1f);
        stateText = AddRowLabel(b, value ? "On" : "Off", 16f, TextDim, TextHorizontalAlignment.Left);
    }

    private Slot BeginRow(Slot section, string name)
    {
        var row = section.AddSlot(name);
        row.AttachComponent<RectTransform>();
        SetFixedHeight(row, RowHeight);

        // No per-row GraphicChunkRoot: the settings screen renders as one root
        // chunk (like the file browser). Per-row chunks only mattered for
        // continuous-update widgets (live widgets keep theirs); here they caused
        // lazily-built rows to stay invisible until a stray dirty event, and
        // slider drags are occasional so a root rebuild is fine.
        ApplyRoundedPanel(row, RowFill, RowBorder);

        var h = row.AttachComponent<HorizontalLayout>();
        h.Spacing.Value = 14f;
        h.PaddingLeft.Value = 12f;
        h.PaddingRight.Value = 12f;
        h.ForceExpandWidth.Value = false;
        h.ForceExpandHeight.Value = true;

        return row;
    }

    private UIBuilder RowBuilder(Slot row)
    {
        var b = new UIBuilder(row);
        b.Font(_dashboard?.Font.Target)
            .TextColor(TextPrimary)
            .ForegroundColor(AccentColor)
            .RoundedSprite(_dashboard?.RoundedSprite);
        return b;
    }

    private static Text AddRowLabel(UIBuilder builder, string content, float size, color textColor, TextHorizontalAlignment alignment)
    {
        var text = builder.Text(content, size, textColor);
        text.HorizontalAlignment.Value = alignment;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        return text;
    }

    private void ApplyRoundedPanel(Slot slot, color fill, color border)
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
    }

    private static void SetFixedHeight(Slot slot, float height)
    {
        var element = slot.GetComponent<LayoutElement>() ?? slot.AttachComponent<LayoutElement>();
        element.MinHeight.Value = height;
        element.PreferredHeight.Value = height;
        element.FlexibleHeight.Value = 0f;
    }
}
