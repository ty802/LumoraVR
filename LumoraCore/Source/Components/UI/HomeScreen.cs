// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Components.Import;
using Lumora.Core.Math;
using Lumora.Core.Networking.Session;
using Lumora.Core.Templates;
using Lumora.Nexus.Cloud;

namespace Lumora.Core.Components.UI;

// The home dash: a grid of widgets, not a screen of inline cards. Everything on it is a WidgetPreset, so
// every one of them can be dragged off onto its own panel in the world and dropped back on the top bar,
// which is the whole point of the grid. The account tile owns the top-left corner with the actions running
// down under it, the two view toggles sit top-right with the session directory below them, and the middle
// is deliberately empty: that is where a popped-out widget lands when you put it back. -xlinka
[ComponentCategory("Hidden")]
public sealed class HomeScreen : WidgetScreen, IDashboardKeyInput
{
    // A fixed cell count, not a fixed cell size: the dash width follows the display aspect, so cells that
    // stretch keep the right-hand column against the right edge instead of leaving a growing gutter.
    // 64 across and 27 down comes out at about 15 px cells on the 16:9 dash, fine enough that a widget
    // put down by hand lands close to where it was let go instead of snapping half a card away. -xlinka
    private const int Columns = 64;
    private const int Rows = 27;

    private const int CardWidth = 13;
    private const int CardHeight = 3;
    private const int RightColumn = Columns - CardWidth;
    private const int AccountWidth = 20;
    private const int AccountHeight = 7;

    private static readonly color OverlayFill = DashTheme.Panel;
    private static readonly color ScrimFill = new color(0.02f, 0.02f, 0.05f, 0.62f);

    private string _template = "LocalHome";
    private SessionVisibility _visibility = SessionVisibility.Private;
    private WorldMode _mode = WorldMode.Builder;
    private int _maxUsers = 16;
    private Text? _status;
    private Slot? _createOverlay;
    private Slot? _createBackdrop;
    private bool _createOpen;
    private AccountWidgetPreset? _account;

    // HOST FOR
    // Same model the Worlds create page carries: 0 is Nobody and the dialog is what it has always been,
    // anything above indexes _hostGroups and swaps the access radios for the two tiers a group world can
    // be on. Fetched when the dialog opens and kept for the life of the screen; it names a pill and
    // decides nothing, because the door reads the HOST's roster. -xlinka
    private readonly List<HostGroupOption> _hostGroups = new();
    private int _hostGroupIndex;
    private bool _groupMembersOnly = true;
    private bool _hostGroupsFetching;
    private bool _hostGroupsLoaded;
    private Slot? _hostForSection;
    private Text? _hostForLabel;
    private Slot? _visibilityStack;
    private Slot? _groupAccessStack;


    // The account tile's sign-in dialog is the only thing here that takes typed input, and only while the
    // tile is docked on this screen: key routing is per dash screen, so a popped-out card gets nothing.
    // The tile can be dragged off and dropped back, and that builds a brand new preset, so the one cached
    // at build time goes stale: re-find it rather than leaving the keyboard and the modal wired to a
    // corpse. -xlinka
    private AccountWidgetPreset? Account
    {
        get
        {
            if (_account != null && !_account.IsDestroyed)
                return _account;
            _account = Slot.GetComponentInChildren<AccountWidgetPreset>();
            return _account != null && !_account.IsDestroyed ? _account : null;
        }
    }

    protected override void BuildContent(UIBuilder builder)
    {
        ResolveDashboard();
        if (WorldTemplates.AvailableTemplates.Count > 0)
            _template = WorldTemplates.AvailableTemplates[0];

        var root = builder.Current;
        _ = root.GetComponent<RectTransform>() ?? root.AttachComponent<RectTransform>();

        var grid = root.AttachComponent<WidgetGrid>();
        grid.FixedColumns.Value = Columns;
        grid.FixedRows.Value = Rows;
        grid.Spacing.Value = new float2(4f, 4f);
        grid.Padding.Value = new float2(DashTheme.Inset, DashTheme.Inset);
        grid.PlacedStyler = preset => _dashboard?.StyleDroppedWidget(preset);

        _account = AddWidget<AccountWidgetPreset>(root, "Account", 0, 0, AccountWidth, AccountHeight);

        // The action cards sit at the BOTTOM of the left column, the width of the account tile above
        // them, so the column reads as two blocks with the empty middle between: the tile you sign in
        // with, and the things you do. Stacked from the floor up, so Paste being absent never leaves a
        // hole. -xlinka
        int row = Rows - CardHeight;
        // No platform clipboard bridge means a paste can never do anything, so the card does not exist
        // rather than sitting there greyed out forever.
        if (ImportHandlers.Clipboard != null)
        {
            AddWidget<PasteWidgetPreset>(root, "Paste", 0, row, AccountWidth, CardHeight);
            row -= CardHeight;
        }
        AddWidget<AvatarStudioWidgetPreset>(root, "AvatarStudio", 0, row, AccountWidth, CardHeight);
        row -= CardHeight;
        AddWidget<NewWorldWidgetPreset>(root, "NewWorld", 0, row, AccountWidth, CardHeight);

        AddWidget<FreeformDashWidgetPreset>(root, "FreeformDash", RightColumn, 0, CardWidth, CardHeight);
        AddWidget<EditWidgetsWidgetPreset>(root, "EditWidgets", RightColumn, CardHeight, CardWidth, CardHeight);

        // Placed at its open height against the bottom row. From here the card owns its own footprint: it
        // collapses to a title row when the services are unreachable, off the top edge, so the bottom of
        // the card stays on this row either way.
        AddWidget<DirectoryWidgetPreset>(root, "Directory", RightColumn, Rows - 7, CardWidth, 7);

        BuildCreateOverlay(root);
    }

    private T AddWidget<T>(Slot grid, string name, int x, int y, int width, int height)
        where T : HomeWidgetPreset, new()
    {
        var slot = grid.AddSlot(name);
        // Its own chunk: a widget that repaints on its own clock (the directory poll, a focused field)
        // re-meshes itself instead of the whole screen.
        slot.AttachComponent<GraphicChunkRoot>();
        var preset = slot.AttachComponent<T>();
        preset.GridX.Value = x;
        preset.GridY.Value = y;
        preset.GridWidth.Value = width;
        preset.GridHeight.Value = height;
        return preset;
    }

    // KEY INPUT

    public bool ConsumeChar(char c) => Account?.ConsumeChar(c) ?? false;

    public bool ConsumeBackspace() => Account?.ConsumeBackspace() ?? false;

    public bool ConsumeEnter() => Account?.ConsumeEnter() ?? false;

    public bool ConsumeEscape()
    {
        if (Account?.ConsumeEscape() == true)
            return true;
        // Escape out of the create dialog before it falls through to closing the dash.
        if (_createOpen)
        {
            CloseCreateMenu();
            return true;
        }
        return false;
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
        backImage.Tint.Value = ScrimFill;
        _createBackdrop.AttachComponent<Button>().Clicked += (_, ctx) =>
        {
            // Dismiss only when the click lands OUTSIDE the dialog panel, so clicks on the panel or
            // its rows never close it even if the full-screen backdrop catches the hit.
            var panelRect = _createOverlay?.GetComponent<RectTransform>()?.LocalComputeRect;
            bool inside = panelRect.HasValue && panelRect.Value.Contains(ctx.PointIn(_createOverlay));
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
        ApplyCard(_createOverlay, OverlayFill, DashTheme.OutlineStrong, DashTheme.RadiusPanel);
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

        DialogTitle(_createOverlay, "New World");

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

        // HOST FOR sits above the access rows and only appears when the caller actually has a group they
        // may host for; signed out, or in no such group, the dialog is exactly what it was.
        _hostForSection = right.AddSlot("HostFor");
        _hostForSection.AttachComponent<RectTransform>();
        var hostForColumn = _hostForSection.AttachComponent<VerticalLayout>();
        hostForColumn.Spacing.Value = 6f;
        hostForColumn.ForceExpandWidth.Value = true;
        hostForColumn.ForceExpandHeight.Value = false;
        Header(_hostForSection, HostForGroups.Label.Resolve());
        _hostForLabel = CycleRow(_hostForSection, HostForGroups.Label.Resolve(),
            HostForGroups.Nobody.Resolve(), CycleHostGroup);
        _hostForSection.ActiveSelf.Value = false;

        Header(right, "Who Can Join");

        _visibilityStack = AddStack(right, "Visibility");
        foreach (SessionVisibility visibility in Enum.GetValues<SessionVisibility>())
        {
            var captured = visibility;
            RadioRow(_visibilityStack, "home-access", PrettyVisibility(visibility), visibility == _visibility,
                () => _visibility = captured);
        }

        // Members only or anyone, and nothing else. Group+ would be the same world as members only while
        // there are no contacts, so it is not offered. -xlinka
        _groupAccessStack = AddStack(right, "GroupVisibility");
        foreach (bool membersOnly in new[] { true, false })
        {
            var captured = membersOnly;
            RadioRow(_groupAccessStack, "home-group-access", GroupAccessLabel(membersOnly),
                membersOnly == _groupMembersOnly, () => _groupMembersOnly = captured);
        }
        _groupAccessStack.ActiveSelf.Value = false;

        ButtonRow(_createOverlay, "Create & Host", DashTheme.Accent, OnCreate);
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
        Account?.CloseSignInDialog();
    }

    // Entry point for the New World widget, which can be sitting on a panel in the world rather than on
    // this screen.
    public void OpenCreateMenu() => SetCreateMenuOpen(true);

    // Entry point for the account tile's Login / Register pill. Both modals hang off the same dashboard
    // slot with the same backdrop, so two of them up at once is two dims deep and a panel behind a panel.
    // This screen raised both, so this screen is the one that keeps them apart. -xlinka
    public void OpenSignIn()
    {
        SetCreateMenuOpen(false);
        Account?.OpenSignInDialog();
    }

    private void CloseCreateMenu() => SetCreateMenuOpen(false);

    private void SetCreateMenuOpen(bool open)
    {
        if (open)
        {
            Account?.CloseSignInDialog();
            EnsureHostGroups();
        }
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

        // This dialog has no name well, so the name is the template's - or the group's, when the world is
        // being hosted for one. -xlinka
        var picked = SelectedHostGroup;
        var name = picked.HasValue && picked.Value.Name.Length > 0
            ? HostForGroups.DefaultWorldName(picked.Value.Name).Resolve()
            : PrettyTemplate(_template);
        // Clamp to the modes this world allows (a published world may be e.g. social-only).
        var mode = WorldTemplates.DefaultMode(_template);
        foreach (var allowed in WorldTemplates.AllowedModes(_template))
        {
            if (allowed == _mode) { mode = _mode; break; }
        }
        var hosting = picked.HasValue
            ? new GroupHosting(picked.Value.Id, picked.Value.Tag, _groupMembersOnly)
            : null;
        SetStatus($"Hosting '{name}'…");
        var world = manager.HostNewWorld(_template, name, _visibility, _maxUsers, mode, hosting);
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
        "Scratch" => "Scratch Space",
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

    // HOST FOR

    private HostGroupOption? SelectedHostGroup
        => _hostGroupIndex > 0 && _hostGroupIndex <= _hostGroups.Count ? _hostGroups[_hostGroupIndex - 1] : null;

    private static string GroupAccessLabel(bool membersOnly)
        => (membersOnly ? HostForGroups.Members : HostForGroups.Public).Resolve();

    // 0 is Nobody, then one step per group, wrapping. A radio per group would grow the dialog by however
    // many groups somebody is in.
    private void CycleHostGroup()
    {
        if (_hostGroups.Count == 0)
            return;
        _hostGroupIndex = (_hostGroupIndex + 1) % (_hostGroups.Count + 1);
        ApplyHostForRow();
        MarkDirty();
    }

    private void ApplyHostForRow()
    {
        if (_hostGroups.Count == 0)
            _hostGroupIndex = 0;

        var picked = SelectedHostGroup;
        if (_hostForSection != null && !_hostForSection.IsDestroyed)
            _hostForSection.ActiveSelf.Value = _hostGroups.Count > 0;
        if (_hostForLabel != null && !_hostForLabel.IsDestroyed)
        {
            _hostForLabel.Content.Value = picked.HasValue ? picked.Value.Name : HostForGroups.Nobody.Resolve();
            _hostForLabel.Color.Value = picked.HasValue ? TextPrimary : TextDim;
        }
        if (_visibilityStack != null && !_visibilityStack.IsDestroyed)
            _visibilityStack.ActiveSelf.Value = !picked.HasValue;
        if (_groupAccessStack != null && !_groupAccessStack.IsDestroyed)
            _groupAccessStack.ActiveSelf.Value = picked.HasValue;
    }

    private void EnsureHostGroups()
    {
        if (_hostGroupsFetching || _hostGroupsLoaded || !HostForGroups.SignedIn)
            return;

        _hostGroupsFetching = true;
        StartTask(async () =>
        {
            var options = await HostForGroups.FetchAsync();
            await WorldContext.ToWorld();
            _hostGroupsFetching = false;
            if (IsDestroyed)
                return;
            _hostGroupsLoaded = true;
            _hostGroups.Clear();
            for (int i = 0; i < options.Count; i++)
                _hostGroups.Add(options[i]);
            _hostGroupIndex = 0;
            ApplyHostForRow();
            MarkDirty();
        });
    }

    // ROW COMPOSITES (used inside the overlay; build on the shared WidgetScreen helpers)

    private void Header(Slot parent, string title) => AddSectionLabel(parent, title);

    // A bare vertical stack inside a column, so a whole set of rows can be shown or hidden at once.
    private static Slot AddStack(Slot parent, string name)
    {
        var stack = parent.AddSlot(name);
        stack.AttachComponent<RectTransform>();
        var layout = stack.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = 6f;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;
        return stack;
    }

    // A row whose right-hand cell is a pill you press to step through the answers. Everything else in
    // this dialog is a radio, but "one of the groups you are in, or none" is not a fixed set. -xlinka
    private Text CycleRow(Slot parent, string name, string initial, Action onPress)
    {
        var row = BeginRow(parent, name);
        var b = RowBuilder(row);
        b.MinWidth(110f).FlexibleWidth(1f);
        AddRowLabel(b, name, DashTheme.FontBody, TextPrimary, TextHorizontalAlignment.Left);

        var cell = row.AddSlot("Value");
        cell.AttachComponent<RectTransform>();
        var element = cell.AttachComponent<LayoutElement>();
        element.MinWidth.Value = 190f;
        element.PreferredWidth.Value = 190f;
        element.FlexibleWidth.Value = 0f;
        element.FlexibleHeight.Value = 1f;
        var panel = ApplyRoundedPanel(cell, DashTheme.Surface, RowBorder);
        var button = cell.AttachComponent<Button>();
        button.Clicked += (_, _) => onPress();
        DriveCardStates(button, panel, DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.SurfacePressed);
        var text = AddFillLabel(cell, initial, DashTheme.FontBody, TextDim, SemiboldFont);
        return text;
    }

    private void DialogTitle(Slot parent, string title)
    {
        var row = parent.AddSlot(title + "Title");
        row.AttachComponent<RectTransform>();
        SetFixedHeight(row, 34f);
        var label = AddFillLabel(row, title, DashTheme.FontTitle, DashTheme.Text, BoldFont);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }

    // Status line under the action, not a row: it is a sentence, and boxing it made the dialog read
    // as one more list item. -xlinka
    private Slot AddInfoRow(Slot parent, string text, color textColor, out Text label)
    {
        var row = parent.AddSlot("Info");
        row.AttachComponent<RectTransform>();
        SetFixedHeight(row, 24f);
        label = AddFillLabel(row, text, DashTheme.FontSmall, textColor);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        return row;
    }

    private Slot RadioRow(Slot parent, string group, string label, bool isChecked, Action onSelect)
    {
        var row = BeginRow(parent, label);
        var b = RowBuilder(row);
        b.MinWidth(180f).FlexibleWidth(1f);
        AddRowLabel(b, label, DashTheme.FontBody, TextPrimary, TextHorizontalAlignment.Left);
        b.MinWidth(26f).PreferredWidth(26f).FlexibleWidth(0f);
        b.Radio(group, isChecked, (_, on) => { if (on) onSelect(); });
        return row;
    }

    private Slot SliderRow(Slot parent, string label, float min, float max, float value, Func<float, string> applyAndFormat)
    {
        var row = BeginRow(parent, label);
        var b = RowBuilder(row);

        b.MinWidth(150f).PreferredWidth(150f).FlexibleWidth(0f);
        AddRowLabel(b, label, DashTheme.FontBody, TextPrimary, TextHorizontalAlignment.Left);

        Text? valueText = null;
        b.MinWidth(120f).PreferredWidth(240f).FlexibleWidth(1f);
        b.Slider(value, min, max, (_, v) =>
        {
            var formatted = applyAndFormat(v);
            if (valueText != null && !valueText.IsDestroyed)
                valueText.Content.Value = formatted;
        });

        b.MinWidth(70f).PreferredWidth(70f).FlexibleWidth(0f);
        valueText = AddRowLabel(b, applyAndFormat(value), DashTheme.FontBody, TextDim, TextHorizontalAlignment.Right);
        return row;
    }

    private Slot ButtonRow(Slot parent, string label, color fill, Action onClick)
    {
        var row = parent.AddSlot(label);
        row.AttachComponent<RectTransform>();
        row.AttachComponent<GraphicChunkRoot>();
        SetFixedHeight(row, 40f);
        var panel = ApplyCard(row, fill, new color(0f, 0f, 0f, 0f), DashTheme.RadiusControl);
        var button = row.AttachComponent<Button>();
        button.Clicked += (_, _) => onClick();
        DriveCardStates(button, panel, fill, DashTheme.AccentHover, DashTheme.AccentPressed);
        AddFillLabel(row, label, DashTheme.FontBody, OnFill(fill), SemiboldFont);
        return row;
    }
}
