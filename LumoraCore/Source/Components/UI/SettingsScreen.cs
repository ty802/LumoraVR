// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Input;
using Lumora.Core.Input.Actions;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// category sidebar on the left, grouped section cards on the right, backed by EngineSettings
// (persisted; the platform layer applies vsync/window/audio). rows are chunk-isolated so slider
// drags only re-render their own row.
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
    private static readonly color SegmentDisabledFill = new color(0.16f, 0.15f, 0.20f, 0.55f);
    private static readonly color TextDisabled = new color(0.50f, 0.50f, 0.56f, 1f);

    private Dashboard? _dashboard;
    private readonly List<(Slot content, BorderedImage buttonBackground)> _categories = new();

    // LOCOMOTION ROW - live mirror of LocomotionController.ActiveModule; see LocomotionRow below.
    private Slot? _locomotionStrip;
    private readonly List<(Slot buttonSlot, BorderedImage background, Text label, LocomotionModule module)> _locomotionSegments = new();
    private LocomotionModule? _locomotionLastActive;
    private float _locomotionRefreshAccum;
    private const float LocomotionRefreshInterval = 0.25f;

    // CONTROLS PAGE - see BuildControlsPage.
    private const float BindLabelWidth = 168f;
    private const float BindDesktopWidth = 152f;
    private const float BindPadWidth = 116f;
    private const float BindVRWidth = 132f;
    private const float BindButtonWidth = 46f;
    private const float ControlsRowSpacing = 8f;

    private static readonly InputDeviceKind[] DesktopDevices = { InputDeviceKind.Keyboard, InputDeviceKind.Mouse };
    private static readonly InputDeviceKind[] PadDevices = { InputDeviceKind.Gamepad };
    private static readonly InputDeviceKind[] VRDevices = { InputDeviceKind.VRController };

    private readonly List<(InputAction action, InputDeviceKind[] devices, Text label)> _bindingCells = new();
    private Slot? _controlsPage;
    private RectTransform? _controlsContentRect;
    private Text? _padStatusText;
    private Text? _lastInputText;
    private Text? _controlsStatusText;
    private float _controlsRefreshAccum;
    private const float ControlsRefreshInterval = 0.1f;

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
            var locomotion = BeginSection(page, "Locomotion", rowCount: 1);
            LocomotionRow(locomotion);

            var mouse = BeginSection(page, "Mouse", rowCount: 2);
            SliderRow(mouse, "Sensitivity", 0.1f, 5f, EngineSettings.MouseSensitivity,
                v => { EngineSettings.MouseSensitivity = v; return $"{EngineSettings.MouseSensitivity:0.00}x"; });
            SliderRow(mouse, "Smoothing", 0f, 0.9f, EngineSettings.MouseSmoothing,
                v => { EngineSettings.MouseSmoothing = v; return EngineSettings.MouseSmoothing <= 0.001f ? "Off" : $"{EngineSettings.MouseSmoothing:0.00}"; });

            var movement = BeginSection(page, "Movement", rowCount: 1);
            SliderRow(movement, "Noclip Speed", 1f, 30f, EngineSettings.NoclipSpeed,
                v => { EngineSettings.NoclipSpeed = v; return $"{EngineSettings.NoclipSpeed:0.#} m/s"; });
        });

        BuildCategory(sidebar, contentHost, "Controls", BuildControlsPage);

        BuildCategory(sidebar, contentHost, "Audio", page =>
        {
            var section = BeginSection(page, "Volume", rowCount: 1);
            SliderRow(section, "Master Volume", 0f, 1f, EngineSettings.MasterVolume,
                v => { EngineSettings.MasterVolume = v; return $"{EngineSettings.MasterVolume * 100f:0}%"; });
        });

        BuildCategory(sidebar, contentHost, "Avatar", page =>
        {
            var section = BeginSection(page, "Calibration", rowCount: 1);
            // Your standing/eye height. The avatar auto-rescales so its eyes sit at this height (live).
            SliderRow(section, "Height", 0.5f, 2.5f, EngineSettings.UserHeight,
                v => { EngineSettings.UserHeight = v; return $"{EngineSettings.UserHeight:0.00} m"; });
        });

        BuildCategory(sidebar, contentHost, "Video", page =>
        {
            var display = BeginSection(page, "Display", rowCount: 4);
            ToggleRow(display, "VSync", EngineSettings.VSync, v => EngineSettings.VSync = v);
            ToggleRow(display, "Fullscreen", EngineSettings.Fullscreen, v => EngineSettings.Fullscreen = v);
            SliderRow(display, "FPS Limit", 0f, 240f, EngineSettings.MaxFps,
                v =>
                {
                    int fps = (int)MathF.Round(v / 10f) * 10;
                    EngineSettings.MaxFps = fps;
                    return EngineSettings.MaxFps == 0 ? "Off" : EngineSettings.MaxFps.ToString();
                });
            // Caps the loop while the window is unfocused or minimized (vsync stops throttling there); 0 = Off. -xlinka
            SliderRow(display, "Background FPS", 0f, 120f, EngineSettings.BackgroundFps,
                v =>
                {
                    int fps = (int)MathF.Round(v / 10f) * 10;
                    EngineSettings.BackgroundFps = fps;
                    return EngineSettings.BackgroundFps == 0 ? "Off" : EngineSettings.BackgroundFps.ToString();
                });

            var quality = BeginSection(page, "Quality", rowCount: 4);
            SliderRow(quality, "Render Scale", 0.5f, 1.5f, EngineSettings.RenderScale,
                v =>
                {
                    // Snap to 5% steps so the viewport isn't re-allocated per pixel of drag.
                    EngineSettings.RenderScale = MathF.Round(v * 20f) / 20f;
                    return $"{EngineSettings.RenderScale * 100f:0}%";
                });
            // Caps how large a loaded texture is allowed to get. Applies live: providers re-resolve
            // onto the variant for the new cap without a reload. Driven as a slider over the
            // generated buckets (index, not pixels) so a drag can only land on a size that actually
            // has a variant behind it. -xlinka
            SliderRow(quality, "Max Texture Size", 0f, EngineSettings.TextureSizeOptions.Length - 1,
                TextureSizeIndex(EngineSettings.MaxTextureSize),
                v =>
                {
                    int index = System.Math.Clamp((int)MathF.Round(v), 0, EngineSettings.TextureSizeOptions.Length - 1);
                    EngineSettings.MaxTextureSize = EngineSettings.TextureSizeOptions[index];
                    return EngineSettings.DescribeTextureSize(EngineSettings.MaxTextureSize);
                });
            // Off makes every reflection probe stop rendering, which removes its cost rather than
            // dimming it: a probe is six scene renders per bake. Applies live through the probe hooks. -xlinka
            ToggleRow(quality, "Reflections", EngineSettings.ReflectionsEnabled,
                v => EngineSettings.ReflectionsEnabled = v);
            // Multiplier on every LOD switch distance. Above 1 holds detailed levels further out. -xlinka
            SliderRow(quality, "LOD Bias", 0.25f, 4f, EngineSettings.LodBias,
                v =>
                {
                    EngineSettings.LodBias = MathF.Round(v * 20f) / 20f;
                    return $"{EngineSettings.LodBias:0.00}x";
                });
        });

        BuildCategory(sidebar, contentHost, "Network", page =>
        {
            var sync = BeginSection(page, "Synchronization", rowCount: 1);
            // Sync send/process rate. Applies live to the active session; higher = smoother replication
            // at more bandwidth/CPU. Snapped to 5 Hz steps. -xlinka
            SliderRow(sync, "Tick Rate", 10f, 120f, EngineSettings.NetworkTickRate,
                v =>
                {
                    int hz = (int)MathF.Round(v / 5f) * 5;
                    EngineSettings.NetworkTickRate = hz;
                    return $"{EngineSettings.NetworkTickRate} Hz";
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

    // CONTROLS
    //
    // Generated from the action map rather than hand-listed: a set added in code shows up here with
    // its bindings and its rebind buttons, and nothing has to be kept in step by hand.
    //
    // Keyboard and mouse share one column because they share one desk - a rebind there takes
    // whichever of the two you reach for, and clearing it clears both. Bindings save the moment they
    // change rather than on exit; a remap you cannot undo because you remapped the key that reaches
    // this screen is a trap. -xlinka
    private void BuildControlsPage(Slot page)
    {
        _controlsPage = page;
        _bindingCells.Clear();

        var map = Engine.Current?.InputInterface?.Actions;

        BuildControlsHeader(page, map);

        if (map == null)
            return;

        var area = page.AddSlot("Bindings");
        area.AttachComponent<RectTransform>();
        var areaElement = area.AttachComponent<LayoutElement>();
        areaElement.FlexibleWidth.Value = 1f;
        areaElement.FlexibleHeight.Value = 1f;
        areaElement.MinHeight.Value = 200f;
        area.AttachComponent<Mask>();
        var scroll = area.AttachComponent<ScrollRect>();
        scroll.ScrollSensitivity.Value = new float2(1f, 1f);

        var content = area.AddSlot("Content");
        _controlsContentRect = content.AttachComponent<RectTransform>();
        if (Canvas.ScrollRenderOffset)
            content.AttachComponent<GraphicChunkRoot>();
        _controlsContentRect.AnchorMin.Value = new float2(0f, 1f);
        _controlsContentRect.AnchorMax.Value = new float2(1f, 1f);
        _controlsContentRect.OffsetMin.Value = new float2(0f, -100f);
        _controlsContentRect.OffsetMax.Value = float2.Zero;
        var stack = content.AttachComponent<VerticalLayout>();
        stack.Spacing.Value = 12f;
        stack.ForceExpandWidth.Value = true;
        stack.ForceExpandHeight.Value = false;
        scroll.Content.Target = _controlsContentRect;

        float total = 0f;
        foreach (var set in map.Sets)
        {
            var rows = new List<InputAction>();
            foreach (var action in set.Actions)
            {
                if (action.Rebindable)
                    rows.Add(action);
            }
            if (rows.Count == 0)
                continue;

            var section = BeginSection(content, set.Label, rows.Count);
            foreach (var action in rows)
                BindingRow(section, action);

            total += SectionPad * 2f + SectionTitleHeight + rows.Count * (RowHeight + RowSpacing) + 12f;
        }

        _controlsContentRect.OffsetMin.Value = new float2(0f, -total);
        RefreshBindingCells();
    }

    private void BuildControlsHeader(Slot page, InputBindingMap? map)
    {
        var card = page.AddSlot("Devices");
        card.AttachComponent<RectTransform>();
        SetFixedHeight(card, SectionPad * 2f + SectionTitleHeight + 2f * (RowHeight + RowSpacing));
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
        titleText.Content.Value = "Devices";
        titleText.Font.Target = _dashboard?.Font.Target!;
        titleText.Size.Value = 20f;
        titleText.Color.Value = SectionTitleColor;
        titleText.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        titleText.VerticalAlignment.Value = TextVerticalAlignment.Middle;

        var deviceRow = BeginControlRow(card, "DeviceRow");
        var db = RowBuilder(deviceRow);
        db.MinWidth(260f).PreferredWidth(260f).FlexibleWidth(0f);
        _padStatusText = AddRowLabel(db, DescribePad(), 16f, TextPrimary, TextHorizontalAlignment.Left);
        db.MinWidth(200f).FlexibleWidth(1f);
        _lastInputText = AddRowLabel(db, "Last input: -", 16f, TextDim, TextHorizontalAlignment.Left);
        db.MinWidth(110f).PreferredWidth(110f).FlexibleWidth(0f);
        db.Button("Reset All", (_, _) => ResetAllBindings(), CategoryFill);

        var statusRow = BeginControlRow(card, "StatusRow");
        var sb = RowBuilder(statusRow);
        sb.MinWidth(200f).FlexibleWidth(1f);
        _controlsStatusText = AddRowLabel(
            sb,
            map == null ? "Input is not ready yet." : "Click a binding to rebind it. Escape cancels.",
            15f, TextDim, TextHorizontalAlignment.Left);
    }

    private void BindingRow(Slot section, InputAction action)
    {
        var row = BeginControlRow(section, action.Name);
        var b = RowBuilder(row);

        b.MinWidth(BindLabelWidth).PreferredWidth(BindLabelWidth).FlexibleWidth(0f);
        AddRowLabel(b, action.Label, 16f, TextPrimary, TextHorizontalAlignment.Left);

        AddBindingCell(row, action, DesktopDevices, BindDesktopWidth);
        AddBindingCell(row, action, PadDevices, BindPadWidth);
        AddBindingCell(row, action, VRDevices, BindVRWidth);

        var tail = RowBuilder(row);
        tail.MinWidth(BindButtonWidth).PreferredWidth(BindButtonWidth).FlexibleWidth(0f);
        tail.Button("X", (_, _) => ClearAllBindings(action), SegmentDisabledFill);
        tail.MinWidth(BindButtonWidth + 12f).PreferredWidth(BindButtonWidth + 12f).FlexibleWidth(0f);
        tail.Button("Reset", (_, _) => ResetBinding(action), CategoryFill);
    }

    private void AddBindingCell(Slot row, InputAction action, InputDeviceKind[] devices, float width)
    {
        var b = RowBuilder(row);
        b.MinWidth(width).PreferredWidth(width).FlexibleWidth(0f);
        var button = b.Button(action.DescribeBindings(devices), (_, _) => BeginRebind(action, devices), RowFill);
        var label = button.Slot.GetComponentInChildren<Text>();
        if (label != null)
        {
            label.Size.Value = 15f;
            _bindingCells.Add((action, devices, label));
        }
    }

    // Narrower gutters than an ordinary settings row: six cells have to fit across, and the binding
    // text is what needs the room.
    private Slot BeginControlRow(Slot section, string name)
    {
        var row = BeginRow(section, name);
        var layout = row.GetComponent<HorizontalLayout>();
        if (layout != null)
        {
            layout.Spacing.Value = ControlsRowSpacing;
            layout.PaddingLeft.Value = 10f;
            layout.PaddingRight.Value = 10f;
        }
        return row;
    }

    private void BeginRebind(InputAction action, InputDeviceKind[] devices)
    {
        var map = Engine.Current?.InputInterface?.Actions;
        if (map == null)
            return;
        map.ClearCapture();
        map.BeginCapture(action, devices);
        SetControlsStatus($"Press a control for \"{action.Label}\"... (Escape cancels)");
        RefreshBindingCells();
    }

    private void ClearAllBindings(InputAction action)
    {
        var map = Engine.Current?.InputInterface?.Actions;
        if (map == null)
            return;
        map.ClearBindings(action, InputDeviceKind.Keyboard, InputDeviceKind.Mouse, InputDeviceKind.Gamepad, InputDeviceKind.VRController);
        PersistBindings();
        SetControlsStatus($"Cleared every binding for \"{action.Label}\".");
        RefreshBindingCells();
    }

    private void ResetBinding(InputAction action)
    {
        var map = Engine.Current?.InputInterface?.Actions;
        if (map == null)
            return;
        map.ResetToDefaults(action);
        PersistBindings();
        SetControlsStatus($"Restored the stock binding for \"{action.Label}\".");
        RefreshBindingCells();
    }

    private void ResetAllBindings()
    {
        var map = Engine.Current?.InputInterface?.Actions;
        if (map == null)
            return;
        map.ResetAllToDefaults();
        PersistBindings();
        SetControlsStatus("Every control is back to stock.");
        RefreshBindingCells();
    }

    private static void PersistBindings() => Engine.Current?.InputInterface?.SaveBindingOverrides();

    private void CancelPendingRebind()
    {
        var map = Engine.Current?.InputInterface?.Actions;
        if (map == null || map.CaptureStatus != InputBindingMap.CaptureState.Listening)
            return;
        map.ClearCapture();
        SetControlsStatus("Rebind cancelled.");
        RefreshBindingCells();
    }

    private void SetControlsStatus(string text)
    {
        if (_controlsStatusText != null && !_controlsStatusText.IsDestroyed)
            _controlsStatusText.Content.Value = text;
    }

    private static string DescribePad()
    {
        var input = Engine.Current?.InputInterface;
        var pad = input?.Gamepad;
        if (pad == null || !pad.IsConnected)
            return "Gamepad: none connected";
        int count = input?.GetGamepadDriver()?.ConnectedPadCount ?? 1;
        return count > 1
            ? $"Gamepad: {pad.DeviceName} (+{count - 1} idle)"
            : $"Gamepad: {pad.DeviceName}";
    }

    private void RefreshBindingCells()
    {
        var map = Engine.Current?.InputInterface?.Actions;
        bool listening = map?.CaptureStatus == InputBindingMap.CaptureState.Listening;

        foreach (var (action, devices, label) in _bindingCells)
        {
            if (label == null || label.IsDestroyed)
                continue;

            bool waiting = listening && ReferenceEquals(map!.CaptureTarget, action) && SameDevices(map.CaptureDevices, devices);
            if (waiting)
            {
                label.Content.Value = "Press...";
                label.Color.Value = AccentColor;
                continue;
            }

            label.Content.Value = action.DescribeBindings(devices);
            label.Color.Value = action.HasBindingFor(devices) ? TextPrimary : TextDisabled;
        }

        _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    private static bool SameDevices(IReadOnlyList<InputDeviceKind> a, InputDeviceKind[] b)
    {
        if (a.Count != b.Length)
            return false;
        for (int i = 0; i < b.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    // Polls the map's listener rather than driving it: the capture itself happens inside the input
    // pass, where the raw devices live, so all this has to do is notice when it finished.
    private void UpdateControlsPage(float delta)
    {
        if (_controlsPage == null || _controlsPage.IsDestroyed || !_controlsPage.ActiveSelf.Value)
            return;

        var map = Engine.Current?.InputInterface?.Actions;
        if (map == null)
            return;

        switch (map.CaptureStatus)
        {
            case InputBindingMap.CaptureState.Captured:
                var bound = map.CaptureConflicts;
                if (bound.Count > 0)
                {
                    var names = new List<string>(bound.Count);
                    foreach (var conflict in bound)
                        names.Add(conflict.Label);
                    SetControlsStatus($"Bound. Also used by: {string.Join(", ", names)}.");
                }
                else
                {
                    SetControlsStatus("Bound.");
                }
                map.ClearCapture();
                PersistBindings();
                RefreshBindingCells();
                return;

            case InputBindingMap.CaptureState.Cancelled:
                map.ClearCapture();
                SetControlsStatus("Rebind cancelled.");
                RefreshBindingCells();
                return;
        }

        _controlsRefreshAccum += delta;
        if (_controlsRefreshAccum < ControlsRefreshInterval)
            return;
        _controlsRefreshAccum = 0f;

        if (_padStatusText != null && !_padStatusText.IsDestroyed)
        {
            var padText = DescribePad();
            if (_padStatusText.Content.Value != padText)
            {
                _padStatusText.Content.Value = padText;
                _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
            }
        }

        if (_lastInputText != null && !_lastInputText.IsDestroyed)
        {
            var last = map.LastActivatedControl;
            var lastText = "Last input: " + (last.IsValid ? last.Describe() : "-");
            if (_lastInputText.Content.Value != lastText)
            {
                _lastInputText.Content.Value = lastText;
                _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
            }
        }
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
        // Leaving the Controls page mid-rebind must not strand the listener: while it waits, every
        // action set is gated off, so an abandoned rebind would look like input had died.
        CancelPendingRebind();

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

    // Position of a texture cap within the generated buckets, for the slider's initial value.
    private static int TextureSizeIndex(int size)
    {
        var options = EngineSettings.TextureSizeOptions;
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i] == size)
                return i;
        }
        return 0;
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

    // Segmented row - one button per module registered on the local user's LocomotionController,
    // current one highlighted. Selecting activates it immediately (the same call the radial context
    // menu makes) and remembers it as the spawn preference. A module the world's permission gate
    // currently denies (e.g. Noclip in a locked world) stays visible but non-interactive rather than
    // disappearing, matching how the radial menu itself never hides a module. Built lazily: the
    // controller may not exist yet the first time this screen is opened (dashboard opened before the
    // avatar finished spawning), so the strip re-populates itself once one shows up. -xlinka
    private void LocomotionRow(Slot section)
    {
        var row = BeginRow(section, "Locomotion");
        var b = RowBuilder(row);

        b.MinWidth(240f).PreferredWidth(240f).FlexibleWidth(0f);
        AddRowLabel(b, "Mode", 18f, TextPrimary, TextHorizontalAlignment.Left);

        var strip = row.AddSlot("Segments");
        strip.AttachComponent<RectTransform>();
        var stripElement = strip.AttachComponent<LayoutElement>();
        stripElement.MinWidth.Value = 160f;
        stripElement.FlexibleWidth.Value = 1f;
        stripElement.FlexibleHeight.Value = 1f;
        var stripLayout = strip.AttachComponent<HorizontalLayout>();
        stripLayout.Spacing.Value = 8f;
        stripLayout.ForceExpandWidth.Value = true;
        stripLayout.ForceExpandHeight.Value = true;

        _locomotionStrip = strip;
        RebuildLocomotionSegments();
    }

    private void RebuildLocomotionSegments()
    {
        if (_locomotionStrip == null || _locomotionStrip.IsDestroyed)
            return;

        _locomotionStrip.DestroyChildren();
        _locomotionSegments.Clear();

        var locomotion = GetLocalLocomotionController();
        if (locomotion == null)
            return;

        foreach (var module in locomotion.Modules)
        {
            if (module == null || module.IsDestroyed)
                continue;
            // Matches the radial menu's own filter (LocomotionContextActions): the "no locomotion"
            // fallback is an implementation detail, not something a user picks.
            if (string.IsNullOrEmpty(module.DisplayName) || module.DisplayName == "None")
                continue;

            var captured = module;
            var buttonSlot = _locomotionStrip.AddSlot(module.DisplayName);
            buttonSlot.AttachComponent<RectTransform>();
            var buttonElement = buttonSlot.AttachComponent<LayoutElement>();
            buttonElement.MinWidth.Value = 70f;
            buttonElement.FlexibleWidth.Value = 1f;
            buttonElement.FlexibleHeight.Value = 1f;

            var background = buttonSlot.AttachComponent<BorderedImage>();
            background.BorderTint.Value = RowBorder;
            var roundedSprite = _dashboard?.RoundedSprite;
            if (roundedSprite != null)
            {
                background.Texture.Target = roundedSprite;
                background.NineSlice.Value = true;
                background.Borders.Value = new float4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
            }

            var button = buttonSlot.AttachComponent<Button>();
            button.Clicked += (_, _) => SelectLocomotionModule(captured);

            var labelSlot = buttonSlot.AddSlot("Label");
            var labelRect = labelSlot.AttachComponent<RectTransform>();
            labelRect.AnchorMin.Value = float2.Zero;
            labelRect.AnchorMax.Value = float2.One;
            labelRect.OffsetMin.Value = float2.Zero;
            labelRect.OffsetMax.Value = float2.Zero;
            var label = labelSlot.AttachComponent<Text>();
            label.Content.Value = module.DisplayName;
            label.Font.Target = _dashboard?.Font.Target!;
            label.Size.Value = 16f;
            label.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
            label.VerticalAlignment.Value = TextVerticalAlignment.Middle;

            _locomotionSegments.Add((buttonSlot, background, label, captured));
        }

        RefreshLocomotionSegments(locomotion);
        Slot.GetComponentInParents<Canvas>()?.MarkLayoutDirty();
    }

    private void SelectLocomotionModule(LocomotionModule module)
    {
        var locomotion = GetLocalLocomotionController();
        if (locomotion == null || !locomotion.IsModuleUsable(module))
            return;
        locomotion.ActivateModule(module);
        // A deliberate pick from Settings is a standing preference for future spawns, not just this
        // session - unlike the radial menu, which only ever changes the live module.
        EngineSettings.PreferredLocomotion = module.DisplayName;
        RefreshLocomotionSegments(locomotion);
    }

    // Repaints the segment highlight/enabled state from the controller's current ActiveModule; does not
    // touch layout. Called on selection, on show, and from the periodic OnUpdate poll so a switch made
    // through the radial context menu shows up here too.
    private void RefreshLocomotionSegments(LocomotionController? locomotion)
    {
        locomotion ??= GetLocalLocomotionController();
        _locomotionLastActive = locomotion?.ActiveModule;

        if (_locomotionSegments.Count == 0)
            return;

        for (int i = 0; i < _locomotionSegments.Count; i++)
        {
            var (buttonSlot, background, label, module) = _locomotionSegments[i];
            if (buttonSlot == null || buttonSlot.IsDestroyed)
                continue;

            bool usable = locomotion != null && locomotion.IsModuleUsable(module);
            bool active = usable && locomotion != null && ReferenceEquals(locomotion.ActiveModule, module);

            var button = buttonSlot.GetComponent<Button>();
            if (button != null)
                button.Interactable.Value = usable;
            if (background != null && !background.IsDestroyed)
                background.Tint.Value = !usable ? SegmentDisabledFill : active ? CategoryActiveFill : CategoryFill;
            if (label != null && !label.IsDestroyed)
                label.Color.Value = !usable ? TextDisabled : TextPrimary;
        }

        _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    private LocomotionController? GetLocalLocomotionController()
    {
        var userRoot = World?.LocalUser?.Root;
        if (userRoot == null)
            return null;
        return userRoot.GetRegisteredComponent<LocomotionController>() ?? userRoot.Slot?.GetComponent<LocomotionController>();
    }

    protected override void OnShow()
    {
        base.OnShow();
        // The controller might not have existed the first time this screen was built (dashboard opened
        // before the avatar finished spawning); catch up now rather than showing an empty strip forever.
        if (_locomotionSegments.Count == 0)
            RebuildLocomotionSegments();
        else
            RefreshLocomotionSegments(null);

        // A pad plugged in, or a rebind made elsewhere, while this screen sat closed.
        RefreshBindingCells();
    }

    protected override void OnHide()
    {
        CancelPendingRebind();
        base.OnHide();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (!Slot.ActiveSelf.Value)
            return;

        UpdateControlsPage(delta);

        if (_locomotionSegments.Count == 0)
            return;

        _locomotionRefreshAccum += delta;
        if (_locomotionRefreshAccum < LocomotionRefreshInterval)
            return;
        _locomotionRefreshAccum = 0f;

        var locomotion = GetLocalLocomotionController();
        if (!ReferenceEquals(locomotion?.ActiveModule, _locomotionLastActive))
            RefreshLocomotionSegments(locomotion);
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
