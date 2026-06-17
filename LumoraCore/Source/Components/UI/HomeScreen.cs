// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Math;
using Lumora.Core.Networking.Session;
using Lumora.Core.Templates;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Dashboard Home screen: a widget grid of quick actions and toggles. Each item is
/// a widget placed by grid cell; turning on Edit Widgets reveals the grid lines and
/// lets you drag them. "Create New World" is a widget that opens a menu overlay on
/// click and closes it once you host.
/// </summary>
public sealed class HomeScreen : WidgetScreen
{
    private static readonly color CreateFill = new color(0.28f, 0.60f, 0.40f, 0.95f);
    private static readonly color WidgetFill = new color(0.20f, 0.19f, 0.30f, 0.92f);
    private static readonly color ToggleOnFill = new color(0.28f, 0.52f, 0.42f, 0.95f);
    private static readonly color OverlayFill = new color(0.10f, 0.10f, 0.16f, 0.98f);
    private static readonly color AvatarFill = new color(0.45f, 0.34f, 0.62f, 0.95f);

    private string _template = "Empty";
    private SessionVisibility _visibility = SessionVisibility.Private;
    private WorldMode _mode = WorldMode.Builder;
    private int _maxUsers = 16;
    private Text? _status;
    private Slot? _createOverlay;
    private bool _createOpen;

    protected override void BuildContent(UIBuilder builder)
    {
        ResolveDashboard();
        if (WorldTemplates.AvailableTemplates.Count > 0)
            _template = WorldTemplates.AvailableTemplates[0];

        var root = builder.Current;
        _ = root.GetComponent<RectTransform>() ?? root.AttachComponent<RectTransform>();

        // The home screen IS a widget grid (each item below is a widget placed by
        // cell). The edit overlay + drag handles come from WidgetGrid itself.
        var grid = root.AttachComponent<WidgetGrid>();
        grid.CellSize.Value = new float2(150f, 56f);
        grid.Spacing.Value = new float2(10f, 10f);
        grid.Padding.Value = new float2(16f, 16f);

        AddButtonWidget(root, "+ Create New World", CreateFill, 0, 0, 2, 1, ToggleCreateMenu);
        AddToggleWidget(root, "Freeform Dash", 0, 1, 2, 1,
            () => UserspaceDashboard.LocalInstance?.Freeform.Value ?? false,
            v => UserspaceDashboard.LocalInstance?.SetFreeform(v));
        AddToggleWidget(root, "Edit Widgets", 0, 2, 2, 1,
            () => WidgetPanel.EditMode,
            v => WidgetPanel.EditMode = v);
        AddButtonWidget(root, "Avatar Creator", AvatarFill, 0, 3, 2, 1, OpenAvatarCreator);

        BuildCreateOverlay(root);
    }

    private void OpenAvatarCreator()
    {
        // Spawn the in-world creator tool in front of you in the FOCUSED world, and toggle - remove it
        // if one's already up.
        var world = Lumora.Core.Engine.Current?.WorldManager?.FocusedWorld;
        if (world?.RootSlot == null)
            return;

        var existing = world.RootSlot.GetComponentInChildren<Lumora.Core.Components.Avatar.AvatarCreator>();
        if (existing != null && !existing.IsDestroyed)
        {
            existing.Slot.Destroy();
            return;
        }

        float3 position;
        floatQ rotation;
        var userRoot = world.LocalUser?.Root;
        if (userRoot?.HeadSlot != null)
        {
            position = userRoot.HeadPosition + userRoot.HeadRotation * (float3.Forward * 1.25f);
            rotation = userRoot.HeadRotation;
        }
        else
        {
            position = new float3(0f, 1f, 1.25f);
            rotation = floatQ.Identity;
        }

        var slot = world.RootSlot.AddSlot("Avatar Creator");
        slot.GlobalPosition = position;
        slot.GlobalRotation = rotation;
        slot.AttachComponent<Lumora.Core.Components.Avatar.AvatarCreator>();
    }

    // WIDGETS

    private Slot AddWidgetSlot(Slot grid, string name, color fill, int gx, int gy, int gw, int gh)
    {
        var slot = grid.AddSlot(name);
        slot.AttachComponent<GraphicChunkRoot>();
        var widget = slot.AttachComponent<Widget>();
        widget.GridX.Value = gx;
        widget.GridY.Value = gy;
        widget.GridWidth.Value = gw;
        widget.GridHeight.Value = gh;
        ApplyRoundedPanel(slot, fill, RowBorder);
        return slot;
    }

    private void AddButtonWidget(Slot grid, string label, color fill, int gx, int gy, int gw, int gh, Action onClick)
    {
        var slot = AddWidgetSlot(grid, label, fill, gx, gy, gw, gh);
        slot.AttachComponent<Button>().Clicked += (_, _) => onClick();
        AddFillLabel(slot, label, 16f, TextPrimary);
    }

    private void AddToggleWidget(Slot grid, string label, int gx, int gy, int gw, int gh, Func<bool> get, Action<bool> set)
    {
        var slot = AddWidgetSlot(grid, label, WidgetFill, gx, gy, gw, gh);
        var background = slot.GetComponent<BorderedImage>();
        Text? text = null;

        void Refresh()
        {
            bool on = get();
            if (text != null && !text.IsDestroyed)
                text.Content.Value = $"{label}: {(on ? "On" : "Off")}";
            if (background != null && !background.IsDestroyed)
                background.Tint.Value = on ? ToggleOnFill : WidgetFill;
        }

        slot.AttachComponent<Button>().Clicked += (_, _) =>
        {
            set(!get());
            Refresh();
            MarkDirty();
        };
        text = AddFillLabel(slot, label, 16f, TextPrimary);
        Refresh();
    }

    // CREATE-WORLD MENU OVERLAY

    private void BuildCreateOverlay(Slot root)
    {
        _createOverlay = root.AddSlot("CreateOverlay");
        var rect = _createOverlay.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(-220f, -240f);
        rect.OffsetMax.Value = new float2(220f, 240f);
        _createOverlay.OrderOffset.Value = 10000L; // draw above the grid
        _createOverlay.AttachComponent<GraphicChunkRoot>();
        ApplyRoundedPanel(_createOverlay, OverlayFill, RowBorder);

        var col = _createOverlay.AttachComponent<VerticalLayout>();
        col.Spacing.Value = 6f;
        col.PaddingLeft.Value = 16f;
        col.PaddingRight.Value = 16f;
        col.PaddingTop.Value = 16f;
        col.PaddingBottom.Value = 16f;
        col.ForceExpandWidth.Value = true;
        col.ForceExpandHeight.Value = false;

        Header(_createOverlay, "New World");

        Header(_createOverlay, "Template");
        foreach (var template in WorldTemplates.AvailableTemplates)
        {
            var captured = template;
            RadioRow(_createOverlay, "home-template", PrettyTemplate(template), template == _template,
                () => _template = captured);
        }

        Header(_createOverlay, "Session");
        SliderRow(_createOverlay, "Max Users", 1f, 64f, _maxUsers,
            v => { _maxUsers = (int)MathF.Round(v); return _maxUsers.ToString(); });
        foreach (SessionVisibility visibility in Enum.GetValues<SessionVisibility>())
        {
            var captured = visibility;
            RadioRow(_createOverlay, "home-access", PrettyVisibility(visibility), visibility == _visibility,
                () => _visibility = captured);
        }

        Header(_createOverlay, "Mode");
        foreach (WorldMode mode in Enum.GetValues<WorldMode>())
        {
            var captured = mode;
            RadioRow(_createOverlay, "home-mode", PrettyMode(mode), mode == _mode, () => _mode = captured);
        }

        ButtonRow(_createOverlay, "Create & Host", CreateFill, OnCreate);
        AddInfoRow(_createOverlay, "Pick a template, then create & host.", TextDim, out var statusText);
        _status = statusText;

        _createOverlay.ActiveSelf.Value = false;
    }

    private void ToggleCreateMenu()
    {
        _createOpen = !_createOpen;
        if (_createOverlay != null && !_createOverlay.IsDestroyed)
            _createOverlay.ActiveSelf.Value = _createOpen;
        MarkDirty();
    }

    private void OnCreate()
    {
        var manager = Lumora.Core.Engine.Current?.WorldManager;
        if (manager == null)
        {
            SetStatus("No world manager available.");
            return;
        }

        var name = PrettyTemplate(_template);
        // Clamp to the modes this world allows (a published world may be e.g. social-only).
        var mode = WorldTemplates.DefaultMode(_template);
        foreach (var allowed in WorldTemplates.AllowedModes(_template))
        {
            if (allowed == _mode) { mode = _mode; break; }
        }
        SetStatus($"Hosting '{name}'…");
        var world = manager.HostNewWorld(_template, name, _visibility, _maxUsers, mode);
        SetStatus(world != null ? $"Now hosting '{name}' ({PrettyMode(mode)})." : "Failed to host world.");

        // Click Host -> the menu closes (you drop into the new world).
        _createOpen = false;
        if (_createOverlay != null && !_createOverlay.IsDestroyed)
            _createOverlay.ActiveSelf.Value = false;
        MarkDirty();
    }

    private void SetStatus(string text)
    {
        if (_status != null && !_status.IsDestroyed)
            _status.Content.Value = text;
    }

    private static string PrettyTemplate(string template) => template switch
    {
        "LocalHome" => "Home Space",
        "Grid" => "Grid Space",
        "ShaderTest" => "Shader Test",
        _ => template,
    };

    private static string PrettyVisibility(SessionVisibility visibility) => visibility switch
    {
        SessionVisibility.Private => "Private (invite only)",
        SessionVisibility.Contacts => "Contacts",
        SessionVisibility.Public => "Anyone",
        _ => visibility.ToString(),
    };

    private static string PrettyMode(WorldMode mode) => mode switch
    {
        WorldMode.Builder => "Builder (full editing)",
        WorldMode.Social => "Social (no editing)",
        WorldMode.Event => "Event (view only)",
        _ => mode.ToString(),
    };

    // ROW COMPOSITES (used inside the overlay; build on the shared WidgetScreen helpers)

    private Slot Header(Slot parent, string title)
    {
        var row = parent.AddSlot(title + "Hdr");
        row.AttachComponent<RectTransform>();
        SetFixedHeight(row, 26f);
        var label = AddFillLabel(row, title, 17f, SectionTitleColor);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        return row;
    }

    private Slot AddInfoRow(Slot parent, string text, color textColor, out Text label)
    {
        var row = BeginRow(parent, "Info");
        label = AddFillLabel(row, text, 15f, textColor);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        return row;
    }

    private Slot RadioRow(Slot parent, string group, string label, bool isChecked, Action onSelect)
    {
        var row = BeginRow(parent, label);
        var b = RowBuilder(row);
        b.MinWidth(180f).FlexibleWidth(1f);
        AddRowLabel(b, label, 15f, TextPrimary, TextHorizontalAlignment.Left);
        b.MinWidth(26f).PreferredWidth(26f).FlexibleWidth(0f);
        b.Radio(group, isChecked, (_, on) => { if (on) onSelect(); });
        return row;
    }

    private Slot SliderRow(Slot parent, string label, float min, float max, float value, Func<float, string> applyAndFormat)
    {
        var row = BeginRow(parent, label);
        var b = RowBuilder(row);

        b.MinWidth(150f).PreferredWidth(150f).FlexibleWidth(0f);
        AddRowLabel(b, label, 15f, TextPrimary, TextHorizontalAlignment.Left);

        Text? valueText = null;
        b.MinWidth(120f).PreferredWidth(240f).FlexibleWidth(1f);
        b.Slider(value, min, max, (_, v) =>
        {
            var formatted = applyAndFormat(v);
            if (valueText != null && !valueText.IsDestroyed)
                valueText.Content.Value = formatted;
        });

        b.MinWidth(70f).PreferredWidth(70f).FlexibleWidth(0f);
        valueText = AddRowLabel(b, applyAndFormat(value), 15f, TextDim, TextHorizontalAlignment.Right);
        return row;
    }

    private Slot ButtonRow(Slot parent, string label, color fill, Action onClick)
    {
        var row = parent.AddSlot(label);
        row.AttachComponent<RectTransform>();
        row.AttachComponent<GraphicChunkRoot>();
        SetFixedHeight(row, 40f);
        ApplyRoundedPanel(row, fill, RowBorder);
        row.AttachComponent<Button>().Clicked += (_, _) => onClick();
        AddFillLabel(row, label, 16f, TextPrimary);
        return row;
    }
}
