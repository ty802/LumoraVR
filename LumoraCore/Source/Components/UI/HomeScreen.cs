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
    private Slot? _createBackdrop;
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
            // -Z (Backward) is the view direction in our head convention; +Z is behind the user.
            position = userRoot.HeadPosition + userRoot.HeadRotation * (float3.Backward * 1.25f);
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
        // Host the modal on the CANVAS ROOT (the dashboard slot), not this screen's content slot.
        // The backdrop must cover the whole screen and the dialog must draw above the nav bar / chrome,
        // and those live above this screen in the tree - a modal parented inside one tab can only ever
        // stack within that tab (which is why it sat behind the nav bar and didn't dim everything).
        var host = _dashboard?.Slot ?? root;

        // Dim backdrop plane: fills the whole canvas, clicking it dismisses. Added last (and high
        // OrderOffset) so it draws over every screen + the nav bar. (A true frosted blur needs a blur
        // shader we don't have yet; this is the dim pass.)
        _createBackdrop = host.AddSlot("CreateBackdrop");
        var backRect = _createBackdrop.AttachComponent<RectTransform>();
        backRect.AnchorMin.Value = float2.Zero;
        backRect.AnchorMax.Value = float2.One;
        backRect.OffsetMin.Value = float2.Zero;
        backRect.OffsetMax.Value = float2.Zero;
        _createBackdrop.OrderOffset.Value = 9000L;
        // OverlayLevel 1: reserves a render band above all normal UI so the backdrop covers the
        // whole dashboard (nav bar, header, chrome) instead of fighting for order with them.
        _createBackdrop.AttachComponent<GraphicChunkRoot>().OverlayLevel = 1;
        // Plain translucent dim. It dims the dashboard CONTENT behind the dialog (this is inside the
        // dash's own render texture). A screen-read blur would sample the session world (sky/sun), not
        // the transparent dash UI, so it just pulled the world in - a dim is the right modal scrim.
        var backImage = _createBackdrop.AttachComponent<Image>();
        backImage.Tint.Value = new color(0.02f, 0.02f, 0.05f, 0.62f);
        _createBackdrop.AttachComponent<Button>().Clicked += (_, ctx) =>
        {
            // Dismiss only when the click lands OUTSIDE the dialog panel, so clicks on the panel or
            // its rows never close it even if the full-screen backdrop catches the hit.
            var panelRect = _createOverlay?.GetComponent<RectTransform>()?.LocalComputeRect;
            bool inside = panelRect.HasValue && panelRect.Value.Contains(ctx.LocalPoint);
            // DIAGNOSTIC (remove once the click-dismiss is confirmed): logs why the backdrop fired and
            // whether the panel rect actually contains the click point. - xlinka
            var pr = panelRect ?? default;
            Lumora.Core.Logging.Logger.Log(
                $"[CreateMenu] backdrop click pt=({ctx.LocalPoint.x:F1},{ctx.LocalPoint.y:F1}) " +
                $"panel=({pr.xMin:F1},{pr.yMin:F1},{pr.width:F1}x{pr.height:F1}) inside={inside} -> {(inside ? "keep" : "DISMISS")}");
            if (inside)
                return;
            CloseCreateMenu();
        };
        _createBackdrop.ActiveSelf.Value = false;

        // Centered dialog. Wider than tall; sized to fit inside the 720px-tall canvas with margin so
        // the two-column body never spills past the panel.
        _createOverlay = host.AddSlot("CreateOverlay");
        var rect = _createOverlay.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(-400f, -290f);
        rect.OffsetMax.Value = new float2(400f, 290f);
        _createOverlay.OrderOffset.Value = 10000L; // draw above the backdrop
        // OverlayLevel 2: the panel background band, above the backdrop (1) and all normal UI.
        _createOverlay.AttachComponent<GraphicChunkRoot>().OverlayLevel = 2;
        ApplyRoundedPanel(_createOverlay, OverlayFill, RowBorder);
        // Absorb clicks on the panel background so they don't fall through to the backdrop (dismiss).
        _createOverlay.AttachComponent<Button>();

        var col = _createOverlay.AttachComponent<VerticalLayout>();
        col.Spacing.Value = 8f;
        col.PaddingLeft.Value = 20f;
        col.PaddingRight.Value = 20f;
        col.PaddingTop.Value = 18f;
        col.PaddingBottom.Value = 18f;
        col.ForceExpandWidth.Value = true;
        col.ForceExpandHeight.Value = false;

        Header(_createOverlay, "New World");

        // Two-column body so the (otherwise tall) options fit without scrolling, and the dialog reads
        // wider. The body fills the space between the title and the action row; columns top-align.
        var body = _createOverlay.AddSlot("Body");
        body.AttachComponent<RectTransform>();
        var bodyElement = body.AttachComponent<LayoutElement>();
        bodyElement.FlexibleWidth.Value = 1f;
        bodyElement.FlexibleHeight.Value = 1f;
        var bodyLayout = body.AttachComponent<HorizontalLayout>();
        bodyLayout.Spacing.Value = 16f;
        bodyLayout.ForceExpandWidth.Value = true;
        bodyLayout.ForceExpandHeight.Value = true;

        var left = AddColumn(body);
        var right = AddColumn(body);

        Header(left, "Template");
        foreach (var template in WorldTemplates.AvailableTemplates)
        {
            var captured = template;
            RadioRow(left, "home-template", PrettyTemplate(template), template == _template,
                () => _template = captured);
        }

        Header(left, "Mode");
        foreach (WorldMode mode in Enum.GetValues<WorldMode>())
        {
            var captured = mode;
            RadioRow(left, "home-mode", PrettyMode(mode), mode == _mode, () => _mode = captured);
        }

        Header(right, "Session");
        SliderRow(right, "Max Users", 1f, 64f, _maxUsers,
            v => { _maxUsers = (int)MathF.Round(v); return _maxUsers.ToString(); });

        Header(right, "Who Can Join");
        foreach (SessionVisibility visibility in Enum.GetValues<SessionVisibility>())
        {
            var captured = visibility;
            RadioRow(right, "home-access", PrettyVisibility(visibility), visibility == _visibility,
                () => _visibility = captured);
        }

        ButtonRow(_createOverlay, "Create & Host", CreateFill, OnCreate);
        AddInfoRow(_createOverlay, "Pick a template, then create & host.", TextDim, out var statusText);
        _status = statusText;

        // The row helpers each add their own GraphicChunkRoot (per-row re-mesh). Promote them to
        // OverlayLevel 3 so the rows/content draw above the panel background (level 2) and backdrop.
        foreach (var rowChunk in _createOverlay.GetComponentsInChildren<GraphicChunkRoot>(false))
            rowChunk.OverlayLevel = 3;

        _createOverlay.ActiveSelf.Value = false;

        // Both live on the canvas root, not this screen's content - register them so the base HideScreen
        // force-hides them on any screen switch, belt-and-suspenders with our OnHide reset below. -xlinka
        RegisterOverlay(_createBackdrop);
        RegisterOverlay(_createOverlay);
    }

    // A flexible-width column for the two-column dialog body; rows stack top-down.
    private static Slot AddColumn(Slot body)
    {
        var column = body.AddSlot("Column");
        column.AttachComponent<RectTransform>();
        var element = column.AttachComponent<LayoutElement>();
        element.FlexibleWidth.Value = 1f;
        element.FlexibleHeight.Value = 1f;
        var layout = column.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = 6f;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;
        return column;
    }

    public override void OnDestroy()
    {
        // The modal lives on the canvas root now, not this screen's content slot, so it isn't torn
        // down with the screen - destroy it explicitly to avoid orphaned overlay slots.
        if (_createBackdrop != null && !_createBackdrop.IsDestroyed)
            _createBackdrop.Destroy();
        if (_createOverlay != null && !_createOverlay.IsDestroyed)
            _createOverlay.Destroy();
        base.OnDestroy();
    }

    // The create modal + backdrop live on the canvas ROOT (so the backdrop can cover the whole dash and the
    // dialog can draw above the nav chrome), which means hiding THIS screen's content slot does NOT hide them -
    // left open, they keep drawing over whatever screen you switch to (the "two screens at once" overlap). So
    // close the menu explicitly whenever we leave Home. -xlinka
    protected override void OnHide()
    {
        base.OnHide();
        CloseCreateMenu();
    }

    private void ToggleCreateMenu() => SetCreateMenuOpen(!_createOpen);

    private void CloseCreateMenu() => SetCreateMenuOpen(false);

    private void SetCreateMenuOpen(bool open)
    {
        _createOpen = open;
        if (_createOverlay != null && !_createOverlay.IsDestroyed)
            _createOverlay.ActiveSelf.Value = open;
        if (_createBackdrop != null && !_createBackdrop.IsDestroyed)
            _createBackdrop.ActiveSelf.Value = open;
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

        // Click Host -> the dialog + backdrop close (you drop into the new world).
        CloseCreateMenu();
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
