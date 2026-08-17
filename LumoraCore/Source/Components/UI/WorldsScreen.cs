// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Components.Network;
using Lumora.Core.Math;
using Lumora.Core.Networking.Session;
using Lumora.Core.Templates;
using Lumora.Nexus.Cloud;

namespace Lumora.Core.Components.UI;

// Dashboard "Worlds" screen: the world browser.
// Layout - one scrolling list, three clearly separated sections, plus a create page:
// [ Worlds ]        [All][Open n][Sessions n][Saved n][+ New]      [Refresh]
// [ filter field .......................................... ][Clear]
// ┌ viewport ─────────────────────────────────────────────────┐ ┃
// │  OPEN WORLDS (n)                                          │ ┃ scrollbar
// │  | Grid Space                            [Current][Close] │ ┃
// │    3 users · Builder · Hosting · LAN                      │ ┃
// │  SESSIONS ON YOUR NETWORK (n)                             │ ┃
// │  | Someone's World                             [Join]     │ ┃
// │    hosted by someone · 2/16 · Social                      │ ┃
// │  SAVED WORLDS (n)                                         │ ┃
// └───────────────────────────────────────────────────────────┘ ┃
// Every line on a card is real state read off the backend at render time: user counts come from
// World.GetAllUsers / SessionListEntry.ActiveUsers, mode from World.Mode or the session's mode tag,
// host state from World.IsAuthority, access from WorldSettings.AccessLevel. There are no thumbnails
// because nothing produces one yet - the mode-tinted stripe down the left of each card is the only
// "image", and it is derived from real mode data, not decoration for its own sake.
// The chrome (header, tabs, filter, viewport, scrollbar, create page) is built ONCE; only the list
// content is torn down and rebuilt. Refreshes are event-driven (world added/removed, focus change,
// session found/lost/updated, join progress) and coalesced through a dirty flag that only rebuilds
// when the rendered content would actually differ - session discovery updates every listed session
// about once a second, and re-tessellating the whole list on each of those is exactly the kind of
// churn the canvas can't afford. -xlinka
public sealed class WorldsScreen : WidgetScreen, IDashboardKeyInput
{
    private const float HeaderHeight = 40f;
    private const float FilterHeight = 34f;
    private const float StatusHeight = 22f;
    private const float CardHeight = 62f;
    private const float SectionHeight = 28f;
    private const float InfoHeight = 34f;
    private const float RowSpacing = 6f;
    private const float ContentPad = 4f;
    private const float ScrollbarWidth = 16f;

    // A freshly built viewport sits at the RectTransform default (100x100) until the canvas runs a layout
    // pass; sizing the scrollbar off that computes a bogus scroll range. Same sentinel the other scrolling
    // screens use - the real viewport is several hundred px, so this never refuses a legitimate size.
    private const float LaidOutViewportFloor = 100f;

    private static readonly color CardFill = new color(0.17f, 0.16f, 0.26f, 0.92f);
    private static readonly color CardFillCurrent = new color(0.23f, 0.21f, 0.36f, 0.95f);
    private static readonly color ViewportFill = new color(0.13f, 0.12f, 0.20f, 0.55f);
    private static readonly color TabActiveFill = new color(0.45f, 0.38f, 0.80f, 0.92f);
    private static readonly color ChipFill = new color(0.26f, 0.24f, 0.38f, 0.90f);
    private static readonly color FocusFill = new color(0.42f, 0.36f, 0.76f, 0.95f);
    private static readonly color JoinFill = new color(0.24f, 0.56f, 0.38f, 0.95f);
    private static readonly color OpenFill = new color(0.30f, 0.42f, 0.72f, 0.95f);
    private static readonly color QuietFill = new color(0.25f, 0.23f, 0.33f, 0.85f);
    private static readonly color DangerFill = new color(0.70f, 0.24f, 0.28f, 0.95f);
    private static readonly color ScrollHandleColor = new color(0.55f, 0.50f, 0.85f, 0.90f);
    private static readonly color BuilderTint = new color(0.32f, 0.55f, 0.82f, 1f);
    private static readonly color SocialTint = new color(0.28f, 0.66f, 0.44f, 1f);
    private static readonly color EventTint = new color(0.58f, 0.42f, 0.78f, 1f);
    private static readonly color SavedTint = new color(0.48f, 0.48f, 0.56f, 1f);
    private static readonly color WarnText = new color(0.95f, 0.78f, 0.35f, 1f);

    private enum Tab { All, Open, Sessions, Saved, New }

    // Which inline text field owns the keyboard. The dash routes keystrokes to the current screen through
    // IDashboardKeyInput; with two fields on one screen the focus has to be explicit or typing would land
    // in whichever one was written last. None = we consume nothing and the keys fall through. -xlinka
    private enum FieldFocus { None, Filter, NewWorldName }

    // The create form is a dense stack of radio rows and has to fit the body without scrolling, so its rows
    // are shorter than the 34px dashboard default.
    protected override float RowHeight => 30f;

    private Tab _tab = Tab.All;
    private FieldFocus _focus = FieldFocus.None;
    private string _filter = string.Empty;
    private string _newWorldName = string.Empty;

    // Chrome, built once in BuildContent.
    private readonly List<(Tab tab, BorderedImage background, Text label)> _tabs = new();
    private Slot? _filterRow;
    private BorderedImage? _filterBackground;
    private Text? _filterLabel;
    private Slot? _listArea;
    private Slot? _createPage;
    private Slot? _statusRow;
    private Text? _statusLabel;
    private Text? _createStatus;
    private Text? _newWorldNameLabel;
    private BorderedImage? _newWorldNameBackground;

    // Scroll machinery.
    private ScrollRect? _scroll;
    private RectTransform? _viewportRect;
    private Slot? _contentSlot;
    private RectTransform? _contentRect;
    private Slot? _scrollTrack;
    private RectTransform? _scrollHandle;
    private float _handlePressY;
    private float _handlePressScroll;
    private int _scrollHandleRetries;

    // Row-height bookkeeping so the content rect can be pinned to an exact height (every row is a fixed
    // height, so this is exact - no ContentSizeFitter, which fights the ScrollRect over the same rect).
    private float _rowsHeight;
    private int _rowCount;

    // Live data for the current render.
    private readonly List<World> _openWorlds = new();
    private readonly List<SessionListEntry> _sessions = new();
    private readonly List<string> _savedFiles = new();
    private string _signature = string.Empty;
    private bool _dataDirty = true;

    // Close is destructive, so it arms first ("Close" -> "Confirm?") and disarms itself after a moment.
    private World? _closeArmed;

    // Create form state (all four are real HostNewWorld arguments).
    private string _template = "LocalHome";
    private WorldMode _mode = WorldMode.Builder;
    private SessionVisibility _visibility = SessionVisibility.Private;
    private int _maxUsers = 16;

    private Management.WorldManager? _hookedManager;
    private SessionBrowser? _hookedBrowser;
    private FocusManager? _hookedFocus;
    private Management.WorldLoadingService? _hookedLoader;

    private static Management.WorldManager? Manager => Lumora.Core.Engine.Current?.WorldManager;

    // BUILD

    protected override void BuildContent(UIBuilder builder)
    {
        ResolveDashboard();
        if (WorldTemplates.AvailableTemplates.Count > 0)
            _template = WorldTemplates.AvailableTemplates[0];

        var root = builder.Current;
        var col = root.AttachComponent<VerticalLayout>();
        col.Spacing.Value = 8f;
        col.PaddingLeft.Value = 16f;
        col.PaddingRight.Value = 16f;
        col.PaddingTop.Value = 14f;
        col.PaddingBottom.Value = 14f;
        col.ForceExpandWidth.Value = true;
        col.ForceExpandHeight.Value = false;

        BuildHeader(root);
        BuildFilterRow(root);

        // The list and the create form occupy the same body area; the tab decides which one is active.
        var body = root.AddSlot("Body");
        body.AttachComponent<RectTransform>();
        var bodyElement = body.AttachComponent<LayoutElement>();
        bodyElement.FlexibleWidth.Value = 1f;
        bodyElement.FlexibleHeight.Value = 1f;
        bodyElement.MinHeight.Value = 200f;

        BuildListArea(body);
        BuildCreatePage(body);
        BuildStatusRow(root);

        // Read the saved-world folder before the first render so the list doesn't come up empty and then
        // re-render a frame later when OnShow refreshes it.
        RefreshSavedFiles();
        SelectTab(_tab);
    }

    private void BuildHeader(Slot root)
    {
        var header = root.AddSlot("Header");
        header.AttachComponent<RectTransform>();
        SetFixedHeight(header, HeaderHeight);
        var layout = header.AttachComponent<HorizontalLayout>();
        layout.Spacing.Value = 6f;
        layout.ForceExpandWidth.Value = false;
        layout.ForceExpandHeight.Value = false;

        var title = header.AddSlot("Title");
        title.AttachComponent<RectTransform>();
        var titleElement = title.AttachComponent<LayoutElement>();
        titleElement.MinWidth.Value = 100f;
        titleElement.FlexibleWidth.Value = 1f;
        titleElement.MinHeight.Value = HeaderHeight;
        titleElement.PreferredHeight.Value = HeaderHeight;
        var titleLabel = AddFillLabel(title, "Worlds", 20f, SectionTitleColor);
        titleLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Left;

        AddTab(header, Tab.All, "All", 62f);
        AddTab(header, Tab.Open, "Open", 96f);
        AddTab(header, Tab.Sessions, "Sessions", 122f);
        AddTab(header, Tab.Saved, "Saved", 100f);
        AddTab(header, Tab.New, "+ New", 82f);

        AddPillButton(header, "Refresh", QuietFill, 84f, 32f, TextDim, RefreshRequested);
    }

    private void AddTab(Slot header, Tab tab, string label, float width)
    {
        var cell = header.AddSlot(label);
        cell.AttachComponent<RectTransform>();
        cell.AttachComponent<GraphicChunkRoot>();
        var element = cell.AttachComponent<LayoutElement>();
        element.MinWidth.Value = width;
        element.PreferredWidth.Value = width;
        element.FlexibleWidth.Value = 0f;
        element.MinHeight.Value = 32f;
        element.PreferredHeight.Value = 32f;
        element.FlexibleHeight.Value = 0f;
        var background = ApplyRoundedPanel(cell, TabFill, RowBorder);
        // No ColorDriver here: the active tab's tint is written directly on selection, and a driver would
        // fight that write for ownership of the same field.
        cell.AttachComponent<Button>().Clicked += (_, _) => SelectTab(tab);
        var text = AddFillLabel(cell, label, 14f, TextPrimary);
        _tabs.Add((tab, background, text));
    }

    private void BuildFilterRow(Slot root)
    {
        _filterRow = root.AddSlot("Filter");
        _filterRow.AttachComponent<RectTransform>();
        _filterRow.AttachComponent<GraphicChunkRoot>();
        SetFixedHeight(_filterRow, FilterHeight);
        var layout = _filterRow.AttachComponent<HorizontalLayout>();
        layout.Spacing.Value = 6f;
        layout.ForceExpandWidth.Value = false;
        layout.ForceExpandHeight.Value = false;

        var field = _filterRow.AddSlot("Field");
        field.AttachComponent<RectTransform>();
        var fieldElement = field.AttachComponent<LayoutElement>();
        fieldElement.MinWidth.Value = 200f;
        fieldElement.FlexibleWidth.Value = 1f;
        fieldElement.MinHeight.Value = FilterHeight;
        fieldElement.PreferredHeight.Value = FilterHeight;
        _filterBackground = ApplyRoundedPanel(field, ControlFill, RowBorder);
        field.AttachComponent<Button>().Clicked += (_, _) => SetFieldFocus(FieldFocus.Filter);
        _filterLabel = AddFillLabel(field, string.Empty, 14f, TextDim);
        _filterLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        InsetLabel(_filterLabel, 12f);

        AddPillButton(_filterRow, "Clear", QuietFill, 76f, FilterHeight, TextDim, () =>
        {
            _filter = string.Empty;
            SetFieldFocus(FieldFocus.None);
            RefreshList();
        });

        UpdateFilterLabel();
    }

    private void BuildListArea(Slot body)
    {
        _listArea = body.AddSlot("List");
        FillParent(_listArea.AttachComponent<RectTransform>());
        var layout = _listArea.AttachComponent<HorizontalLayout>();
        layout.Spacing.Value = 6f;
        layout.ForceExpandWidth.Value = false;
        layout.ForceExpandHeight.Value = true;

        var viewport = _listArea.AddSlot("Viewport");
        _viewportRect = viewport.AttachComponent<RectTransform>();
        var viewportElement = viewport.AttachComponent<LayoutElement>();
        viewportElement.FlexibleWidth.Value = 1f;
        viewportElement.FlexibleHeight.Value = 1f;
        ApplyRoundedPanel(viewport, ViewportFill, RowBorder);
        // ShowMaskGraphic: the canvas skips a mask slot's own graphics unless this is set, so without it the
        // viewport's backing panel would be invisible.
        viewport.AttachComponent<Mask>().ShowMaskGraphic.Value = true;
        _scroll = viewport.AttachComponent<ScrollRect>();
        _scroll.ScrollSensitivity.Value = new float2(1f, 1f);
        _scroll.ScrollChanged += (_, _) => UpdateScrollHandle();

        _contentSlot = viewport.AddSlot("Content");
        _contentRect = _contentSlot.AttachComponent<RectTransform>();
        // No GraphicChunkRoot here on purpose: ScrollRect.EnsureScrollSetup attaches the content's chunk and
        // flags it ScrollContent so render-offset scrolling can slide it as a unit. One owner, no fighting.
        // Top-pinned full-width strip whose HEIGHT is set explicitly by SetContentHeight from the row count.
        _contentRect.AnchorMin.Value = new float2(0f, 1f);
        _contentRect.AnchorMax.Value = new float2(1f, 1f);
        _contentRect.OffsetMin.Value = new float2(0f, -CardHeight);
        _contentRect.OffsetMax.Value = float2.Zero;
        var contentLayout = _contentSlot.AttachComponent<VerticalLayout>();
        contentLayout.Spacing.Value = RowSpacing;
        contentLayout.PaddingLeft.Value = ContentPad + 2f;
        contentLayout.PaddingRight.Value = ContentPad + 2f;
        contentLayout.PaddingTop.Value = ContentPad;
        contentLayout.PaddingBottom.Value = ContentPad;
        contentLayout.ForceExpandWidth.Value = true;
        contentLayout.ForceExpandHeight.Value = false;
        _scroll.Content.Target = _contentRect;

        BuildScrollbar(_listArea);
    }

    private void BuildScrollbar(Slot area)
    {
        var track = area.AddSlot("Scrollbar");
        _scrollTrack = track;
        track.AttachComponent<RectTransform>();
        // Own chunk: the handle's rect is rewritten on every scroll frame, and without a chunk boundary that
        // layout change escalates to a full canvas rebuild (re-tessellating the whole list mid-scroll).
        track.AttachComponent<GraphicChunkRoot>();
        var element = track.AttachComponent<LayoutElement>();
        element.MinWidth.Value = ScrollbarWidth;
        element.PreferredWidth.Value = ScrollbarWidth;
        element.FlexibleWidth.Value = 0f;
        element.FlexibleHeight.Value = 1f;
        ApplyRoundedPanel(track, TabFill, RowBorder);

        var handleSlot = track.AddSlot("Handle");
        _scrollHandle = handleSlot.AttachComponent<RectTransform>();
        _scrollHandle.AnchorMin.Value = new float2(0f, 1f);
        _scrollHandle.AnchorMax.Value = new float2(1f, 1f);
        _scrollHandle.OffsetMin.Value = new float2(2f, -60f);
        _scrollHandle.OffsetMax.Value = new float2(-2f, 0f);
        ApplyRoundedPanel(handleSlot, ScrollHandleColor, color.Transparent);

        var interaction = handleSlot.AttachComponent<InteractionElement>();
        interaction.Pressed += OnHandlePress;
        interaction.Dragged += OnHandleDrag;
    }

    private void BuildStatusRow(Slot root)
    {
        _statusRow = root.AddSlot("Status");
        _statusRow.AttachComponent<RectTransform>();
        _statusRow.AttachComponent<GraphicChunkRoot>();
        SetFixedHeight(_statusRow, StatusHeight);
        _statusLabel = AddFillLabel(_statusRow, string.Empty, 13f, TextDim);
        _statusLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        _statusRow.ActiveSelf.Value = false;
    }

    // TABS

    private void SelectTab(Tab tab)
    {
        _tab = tab;
        _closeArmed = null;
        if (_focus == FieldFocus.Filter && tab == Tab.New)
            SetFieldFocus(FieldFocus.None);
        else if (_focus == FieldFocus.NewWorldName && tab != Tab.New)
            SetFieldFocus(FieldFocus.None);

        for (int i = 0; i < _tabs.Count; i++)
        {
            var entry = _tabs[i];
            if (entry.background != null && !entry.background.IsDestroyed)
                entry.background.Tint.Value = entry.tab == tab ? TabActiveFill : TabFill;
        }

        bool creating = tab == Tab.New;
        if (_listArea != null && !_listArea.IsDestroyed)
            _listArea.ActiveSelf.Value = !creating;
        if (_createPage != null && !_createPage.IsDestroyed)
            _createPage.ActiveSelf.Value = creating;
        if (_filterRow != null && !_filterRow.IsDestroyed)
            _filterRow.ActiveSelf.Value = !creating;

        if (creating)
            MarkDirty();
        else
            RefreshList();
    }

    // A tab label carries its section's live count, so the counts are visible without switching tabs.
    private void UpdateTabLabels()
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            var entry = _tabs[i];
            if (entry.label == null || entry.label.IsDestroyed)
                continue;
            entry.label.Content.Value = entry.tab switch
            {
                Tab.Open => $"Open {_openWorlds.Count}",
                Tab.Sessions => $"Sessions {_sessions.Count}",
                Tab.Saved => $"Saved {_savedFiles.Count}",
                Tab.New => "+ New",
                _ => "All",
            };
        }
    }

    // SHOW / LIVE UPDATES

    protected override void OnShow()
    {
        base.OnShow();
        Subscribe();
        RefreshSavedFiles();
        if (_tab == Tab.New)
        {
            // The create page has no list to rebuild, but its tab strip still carries the live counts.
            CollectData();
            UpdateTabLabels();
            MarkDirty();
        }
        else
        {
            RefreshList();
        }
        World?.RunInUpdates(2, UpdateScrollHandle);
    }

    protected override void OnHide()
    {
        base.OnHide();
        _closeArmed = null;
        SetFieldFocus(FieldFocus.None);
    }

    public override void OnDestroy()
    {
        Unsubscribe();
        base.OnDestroy();
    }

    private void Subscribe()
    {
        var manager = Manager;
        if (manager != null && !ReferenceEquals(manager, _hookedManager))
        {
            if (_hookedManager != null)
            {
                _hookedManager.WorldAdded -= OnWorldsChanged;
                _hookedManager.WorldRemoved -= OnWorldsChanged;
            }
            manager.WorldAdded += OnWorldsChanged;
            manager.WorldRemoved += OnWorldsChanged;
            _hookedManager = manager;
        }

        var focus = Lumora.Core.Engine.Current?.FocusManager;
        if (focus != null && !ReferenceEquals(focus, _hookedFocus))
        {
            if (_hookedFocus != null)
                _hookedFocus.OnFocusedWorldChanged -= OnFocusedWorldChanged;
            focus.OnFocusedWorldChanged += OnFocusedWorldChanged;
            _hookedFocus = focus;
        }

        var loader = Lumora.Core.Engine.Current?.WorldLoadingService;
        if (loader != null && !ReferenceEquals(loader, _hookedLoader))
        {
            if (_hookedLoader != null)
            {
                _hookedLoader.OnLoadingProgress -= OnLoadingChanged;
                _hookedLoader.OnLoadingComplete -= OnLoadingChanged;
                _hookedLoader.OnLoadingFailed -= OnLoadingChanged;
            }
            loader.OnLoadingProgress += OnLoadingChanged;
            loader.OnLoadingComplete += OnLoadingChanged;
            loader.OnLoadingFailed += OnLoadingChanged;
            _hookedLoader = loader;
        }

        // Resolving the browser also (idempotently) starts LAN discovery, so sessions keep arriving for as
        // long as the browser screen has been opened at least once this run.
        var browser = GetBrowser();
        if (browser != null && !ReferenceEquals(browser, _hookedBrowser))
        {
            if (_hookedBrowser != null)
            {
                _hookedBrowser.OnSessionFound -= OnSessionChanged;
                _hookedBrowser.OnSessionUpdated -= OnSessionChanged;
                _hookedBrowser.OnSessionLost -= OnSessionLost;
            }
            browser.OnSessionFound += OnSessionChanged;
            browser.OnSessionUpdated += OnSessionChanged;
            browser.OnSessionLost += OnSessionLost;
            _hookedBrowser = browser;
        }
    }

    private void Unsubscribe()
    {
        if (_hookedManager != null)
        {
            _hookedManager.WorldAdded -= OnWorldsChanged;
            _hookedManager.WorldRemoved -= OnWorldsChanged;
            _hookedManager = null;
        }
        if (_hookedFocus != null)
        {
            _hookedFocus.OnFocusedWorldChanged -= OnFocusedWorldChanged;
            _hookedFocus = null;
        }
        if (_hookedLoader != null)
        {
            _hookedLoader.OnLoadingProgress -= OnLoadingChanged;
            _hookedLoader.OnLoadingComplete -= OnLoadingChanged;
            _hookedLoader.OnLoadingFailed -= OnLoadingChanged;
            _hookedLoader = null;
        }
        if (_hookedBrowser != null)
        {
            _hookedBrowser.OnSessionFound -= OnSessionChanged;
            _hookedBrowser.OnSessionUpdated -= OnSessionChanged;
            _hookedBrowser.OnSessionLost -= OnSessionLost;
            _hookedBrowser = null;
        }
    }

    // LAN discovery raises its events off the discovery thread, so the handlers only ever set a flag; the
    // rebuild itself happens in OnUpdate on the world thread.
    private void OnWorldsChanged(World world) => _dataDirty = true;
    private void OnFocusedWorldChanged(World oldWorld, World newWorld) => _dataDirty = true;
    private void OnLoadingChanged(Management.WorldLoadingOperation operation) => _dataDirty = true;
    private void OnSessionChanged(SessionListEntry entry) => _dataDirty = true;
    private void OnSessionLost(string sessionId) => _dataDirty = true;

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (!_dataDirty)
            return;
        _dataDirty = false;
        if (IsDestroyed || Slot == null || !Slot.ActiveSelf.Value)
            return;
        // Session discovery re-reports every listed session about once a second. Only rebuild when the
        // rendered content would actually change, so an idle browser costs nothing.
        var signature = ComputeSignature();
        if (signature == _signature)
            return;
        if (_tab == Tab.New)
        {
            // Nothing to re-render on the create page except the counts in the tab strip.
            _signature = signature;
            UpdateTabLabels();
            MarkDirty();
            return;
        }
        RefreshList();
    }

    private void RefreshRequested()
    {
        RefreshSavedFiles();
        // LAN discovery is continuous, so Refresh only has to knock the directory poll off its interval.
        GetBrowser()?.RequestRefresh();
        if (_tab == Tab.New)
        {
            CollectData();
            UpdateTabLabels();
            MarkDirty();
            return;
        }
        RefreshList();
    }

    // DATA

    private void CollectData()
    {
        _openWorlds.Clear();
        _sessions.Clear();

        var manager = Manager;
        if (manager == null)
            return;

        var userspace = manager.UserspaceWorld;
        foreach (var world in manager.Worlds)
        {
            if (world == null || world.IsDestroyed || ReferenceEquals(world, userspace))
                continue;
            // Overlay worlds are chrome (the dash itself lives in one); they are not places you go.
            if (world.Focus == World.WorldFocus.Overlay || world.Focus == World.WorldFocus.PrivateOverlay)
                continue;
            _openWorlds.Add(world);
        }

        var sessions = GetBrowser()?.GetSessions();
        if (sessions != null)
        {
            foreach (var entry in sessions)
            {
                if (entry == null || entry.JoinUrl == null)
                    continue;   // nothing to join means nothing to show
                if (IsAlreadyOpen(entry))
                    continue;   // it's in the Open Worlds section; listing it twice is the old browser's noise
                _sessions.Add(entry);
            }
        }
    }

    // A session we already have open. Hosted worlds carry the session id on the World itself; a world we
    // JOINED never receives the host's id (it stays "Unknown"), so fall back to matching the address we are
    // actually connected to against the announced join URL. -xlinka
    private bool IsAlreadyOpen(SessionListEntry entry)
    {
        for (int i = 0; i < _openWorlds.Count; i++)
        {
            var world = _openWorlds[i];
            var id = world.SessionID?.Value;
            if (!string.IsNullOrEmpty(entry.SessionId) && !string.IsNullOrEmpty(id)
                && string.Equals(id, entry.SessionId, StringComparison.OrdinalIgnoreCase))
                return true;

            var connected = world.Session?.Connections?.HostConnection?.Address;
            if (connected != null && entry.JoinUrl != null
                && connected.Port == entry.JoinUrl.Port
                && string.Equals(connected.Host, entry.JoinUrl.Host, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // Saved worlds are files on disk: they only change when we save/open one, or when something outside the
    // app writes the folder. Re-reading the directory every frame to keep a signature current would be disk
    // I/O on the update thread for nothing, so this list refreshes on show, on Refresh, and after we open one.
    private void RefreshSavedFiles()
    {
        _savedFiles.Clear();
        var directory = System.IO.Path.GetDirectoryName(Lumora.Core.Engine.LocalHomeSavePath);
        if (string.IsNullOrEmpty(directory))
            return;
        try
        {
            if (!System.IO.Directory.Exists(directory))
                return;
            var files = System.IO.Directory.GetFiles(directory, "*.lworld");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            _savedFiles.AddRange(files);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't read saved worlds: {ex.Message}", WarnText);
        }
    }

    // Everything the list actually renders, flattened. Cheap to build (these lists are small) and compared
    // against the last render so a no-op session update doesn't re-tessellate the screen.
    private string ComputeSignature()
    {
        CollectData();
        var sb = new System.Text.StringBuilder();
        var focused = Manager?.FocusedWorld;
        sb.Append((int)_tab).Append('|').Append(_filter).Append('|');
        for (int i = 0; i < _openWorlds.Count; i++)
        {
            var world = _openWorlds[i];
            sb.Append(WorldDisplayName(world)).Append(':')
              .Append(world.GetAllUsers().Count).Append(':')
              .Append((int)world.Mode).Append(':')
              .Append((int)world.State).Append(':')
              .Append(world.IsAuthority ? 'h' : 'g').Append(':')
              .Append(ReferenceEquals(world, focused) ? '*' : '-').Append(';');
        }
        sb.Append('|');
        for (int i = 0; i < _sessions.Count; i++)
        {
            var entry = _sessions[i];
            sb.Append(entry.SessionId).Append(':')
              .Append(entry.Name).Append(':')
              .Append(entry.ActiveUsers).Append('/').Append(entry.MaxUsers).Append(':')
              .Append((int)entry.Source).Append(';');
        }
        sb.Append('|').Append(_savedFiles.Count).Append('|');
        var operation = Lumora.Core.Engine.Current?.WorldLoadingService?.CurrentOperation;
        if (operation != null)
            sb.Append(operation.WorldName).Append(':').Append((int)operation.Phase).Append(':')
              .Append((int)(operation.Progress * 20f));
        return sb.ToString();
    }

    // RENDER

    private void RefreshList()
    {
        var content = _contentSlot;
        if (content == null || content.IsDestroyed)
            return;

        _signature = ComputeSignature();   // also (re)collects the open world / session lists
        UpdateTabLabels();

        content.DestroyChildren();
        _rowsHeight = 0f;
        _rowCount = 0;

        if (Manager == null)
        {
            InfoRow(content, "World manager isn't up yet.", WarnText);
        }
        else
        {
            var loader = Lumora.Core.Engine.Current?.WorldLoadingService;
            if (loader != null && loader.IsLoading)
            {
                var operation = loader.CurrentOperation;
                InfoRow(content, $"Joining '{operation?.WorldName}' - {operation?.StatusMessage} ({(int)((operation?.Progress ?? 0f) * 100f)}%)", AccentColor);
            }

            if (_tab is Tab.All or Tab.Open)
                BuildOpenSection(content);
            if (_tab is Tab.All or Tab.Sessions)
                BuildSessionSection(content);
            if (_tab is Tab.All or Tab.Saved)
                BuildSavedSection(content);
        }

        SetContentHeight();
        // The scroll position is NOT touched here. ScrollRect stores it normalized and re-applies it from the
        // layout pass, so a live refresh keeps your place; writing pixels back would convert them against the
        // stale pre-rebuild excess and shove the list under the user's cursor. -xlinka
        UpdateScrollHandle();
        World?.RunInUpdates(1, UpdateScrollHandle);
        MarkDirty();
    }

    private void BuildOpenSection(Slot content)
    {
        SectionRow(content, $"Open worlds ({_openWorlds.Count})");
        int shown = 0;
        for (int i = 0; i < _openWorlds.Count; i++)
        {
            var world = _openWorlds[i];
            if (BuildOpenWorldCard(content, world))
                shown++;
        }
        if (_openWorlds.Count == 0)
            InfoRow(content, "No worlds open. Host one from + New, or join a session below.", TextDim);
        else if (shown == 0)
            InfoRow(content, "No open world matches the filter.", TextDim);
    }

    private bool BuildOpenWorldCard(Slot content, World world)
    {
        var manager = Manager;
        if (manager == null)
            return false;

        bool focused = ReferenceEquals(world, manager.FocusedWorld);
        bool running = world.State == World.WorldState.Running;
        string name = WorldDisplayName(world);

        var meta = new List<string>(5);
        if (!running)
            meta.Add(world.State.ToString());
        meta.Add(UserCount(world.GetAllUsers().Count));
        meta.Add(ModeLabel(world.Mode));
        meta.Add(world.IsAuthority ? "Hosting" : "Guest");
        var access = world.Configuration?.AccessLevel.Value;
        if (access.HasValue)
            meta.Add(AccessLabel(access.Value));
        if (world.Name == "LocalHome")
            meta.Add("Home");
        string metaLine = string.Join(" · ", meta);

        if (!Matches(name, metaLine))
            return false;

        var captured = world;
        // The card itself is the primary click target (hit testing takes the deepest element, so the
        // buttons on top of it still win); the pill just makes the action visible.
        Action? primary = focused || !running ? null : () => FocusWorld(captured);
        var card = Card(content, name, metaLine, ModeColor(world.Mode), focused ? CardFillCurrent : CardFill, primary);

        if (focused)
            Chip(card, "Current", FocusFill, 92f);
        else if (!running)
            Chip(card, "Loading", ChipFill, 92f);
        else
            AddPillButton(card, "Focus", FocusFill, 92f, 32f, TextPrimary, () => FocusWorld(captured));

        // Closing the world you are standing in only works if there is somewhere to go, so the button only
        // appears when a fallback exists (see CloseWorld). No dead buttons.
        if (FindFallbackWorld(world) != null)
        {
            bool armed = ReferenceEquals(_closeArmed, world);
            AddPillButton(card, armed ? "Confirm?" : "Close", armed ? DangerFill : QuietFill, 88f, 32f,
                armed ? TextPrimary : TextDim, () => CloseWorld(captured));
        }
        return true;
    }

    // Two sources feed this one section: LAN discovery and the backend session directory. They are NOT
    // split into two lists - a session is a session, and the per-card Local/Internet chip says where it came
    // from. The browser already de-duplicates by session id, so a host that is both on your network and
    // listed on the directory appears once, as Local. -xlinka
    private void BuildSessionSection(Slot content)
    {
        var browser = GetBrowser();
        SectionRow(content, $"Sessions ({_sessions.Count})");
        int shown = 0;
        for (int i = 0; i < _sessions.Count; i++)
        {
            if (BuildSessionCard(content, _sessions[i]))
                shown++;
        }
        if (_sessions.Count == 0)
        {
            // Say which sources actually looked, so an empty list is never mistaken for "nobody is playing"
            // when the directory is simply unreachable.
            string reason;
            if (browser?.IsScanning.Value != true)
                reason = "No sessions found.";
            else if (browser.BackendUnreachable)
                reason = "Scanning the local network. Session directory unreachable.";
            else
                reason = "Scanning the local network and the session directory…";
            InfoRow(content, reason, browser?.BackendUnreachable == true ? WarnText : TextDim);
        }
        else if (shown == 0)
        {
            InfoRow(content, "No session matches the filter.", TextDim);
        }
    }

    private bool BuildSessionCard(Slot content, SessionListEntry entry)
    {
        string name = string.IsNullOrEmpty(entry.Name) ? "(unnamed world)" : entry.Name;
        var mode = WorldModePermissions.ParseMode(entry.Tags);

        var meta = new List<string>(5);
        if (!string.IsNullOrEmpty(entry.HostUsername))
            meta.Add($"hosted by {entry.HostUsername}");
        meta.Add($"{entry.ActiveUsers}/{entry.MaxUsers}");
        meta.Add(ModeLabel(mode));
        meta.Add(entry.Visibility.ToString());
        meta.Add(entry.Source == SessionSource.Internet ? "Internet" : "Local");
        string metaLine = string.Join(" · ", meta);

        if (!Matches(name, metaLine))
            return false;

        var captured = entry;
        bool joinable = entry.HasSpace;
        var card = Card(content, name, metaLine, ModeColor(mode), CardFill, joinable ? () => JoinSession(captured) : null);
        if (joinable)
            AddPillButton(card, "Join", JoinFill, 92f, 32f, TextPrimary, () => JoinSession(captured));
        else
            Chip(card, "Full", ChipFill, 92f);
        return true;
    }

    private void BuildSavedSection(Slot content)
    {
        SectionRow(content, $"Saved worlds ({_savedFiles.Count})");
        int shown = 0;
        for (int i = 0; i < _savedFiles.Count; i++)
        {
            if (BuildSavedCard(content, _savedFiles[i]))
                shown++;
        }
        if (_savedFiles.Count == 0)
            InfoRow(content, "No saved worlds yet. Save one from Session → World Save Options.", TextDim);
        else if (shown == 0)
            InfoRow(content, "No saved world matches the filter.", TextDim);
    }

    private bool BuildSavedCard(Slot content, string path)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        var meta = new List<string>(2);
        try
        {
            var info = new System.IO.FileInfo(path);
            meta.Add(info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"));
            meta.Add(FormatBytes(info.Length));
        }
        catch
        {
            meta.Add("saved world");
        }
        string metaLine = string.Join(" · ", meta);

        if (!Matches(name, metaLine))
            return false;

        var captured = path;
        // The mode of a saved world isn't known until it's loaded, so it gets the neutral stripe rather than
        // a guessed one.
        var card = Card(content, name, metaLine, SavedTint, CardFill, () => OpenSavedWorld(captured));
        AddPillButton(card, "Open", OpenFill, 92f, 32f, TextPrimary, () => OpenSavedWorld(captured));
        return true;
    }

    // ROW WIDGETS

    // A world/session card: mode-tinted stripe, name, one line of real metadata, then whatever action cells
    // the caller appends. Returns the card slot so the caller can append them.
    private Slot Card(Slot content, string title, string meta, color stripe, color fill, Action? activate)
    {
        var card = content.AddSlot("Card");
        card.AttachComponent<RectTransform>();
        card.AttachComponent<GraphicChunkRoot>();
        var element = card.AttachComponent<LayoutElement>();
        element.MinHeight.Value = CardHeight;
        element.PreferredHeight.Value = CardHeight;
        element.FlexibleHeight.Value = 0f;
        element.FlexibleWidth.Value = 1f;
        var background = ApplyRoundedPanel(card, fill, RowBorder);

        var layout = card.AttachComponent<HorizontalLayout>();
        layout.Spacing.Value = 10f;
        layout.PaddingLeft.Value = 10f;
        layout.PaddingRight.Value = 10f;
        layout.PaddingTop.Value = 9f;
        layout.PaddingBottom.Value = 9f;
        layout.ForceExpandWidth.Value = false;
        // Children size themselves on the cross axis and centre in the card, so a 32px button sits in a
        // 62px card instead of stretching to fill it.
        layout.ForceExpandHeight.Value = false;

        if (activate != null)
        {
            var button = card.AttachComponent<Button>();
            button.Clicked += (_, _) => activate();
            button.AddColorDriver(background.Tint, fill, InteractionColorMode.Direct);
        }

        var bar = card.AddSlot("Stripe");
        bar.AttachComponent<RectTransform>();
        var barElement = bar.AttachComponent<LayoutElement>();
        barElement.MinWidth.Value = 5f;
        barElement.PreferredWidth.Value = 5f;
        barElement.FlexibleWidth.Value = 0f;
        barElement.MinHeight.Value = 34f;
        barElement.PreferredHeight.Value = 34f;
        barElement.FlexibleHeight.Value = 0f;
        bar.AttachComponent<Image>().Tint.Value = stripe;

        var column = card.AddSlot("Text");
        column.AttachComponent<RectTransform>();
        var columnElement = column.AttachComponent<LayoutElement>();
        columnElement.MinWidth.Value = 180f;
        columnElement.FlexibleWidth.Value = 1f;
        columnElement.MinHeight.Value = 40f;
        columnElement.PreferredHeight.Value = 40f;
        columnElement.FlexibleHeight.Value = 0f;
        var columnLayout = column.AttachComponent<VerticalLayout>();
        columnLayout.Spacing.Value = 2f;
        columnLayout.ForceExpandWidth.Value = true;
        columnLayout.ForceExpandHeight.Value = false;

        TextLine(column, Truncate(title, 54), 17f, TextPrimary, 22f);
        TextLine(column, Truncate(meta, 110), 12f, TextDim, 16f);

        AddRow(CardHeight);
        return card;
    }

    private void TextLine(Slot column, string text, float size, color textColor, float height)
    {
        var line = column.AddSlot("Line");
        line.AttachComponent<RectTransform>();
        var element = line.AttachComponent<LayoutElement>();
        element.MinHeight.Value = height;
        element.PreferredHeight.Value = height;
        element.FlexibleHeight.Value = 0f;
        element.FlexibleWidth.Value = 1f;
        var label = AddFillLabel(line, text, size, textColor);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }

    private void SectionRow(Slot content, string title)
    {
        var row = content.AddSlot("Section");
        row.AttachComponent<RectTransform>();
        var element = row.AttachComponent<LayoutElement>();
        element.MinHeight.Value = SectionHeight;
        element.PreferredHeight.Value = SectionHeight;
        element.FlexibleHeight.Value = 0f;
        element.FlexibleWidth.Value = 1f;
        var label = AddFillLabel(row, title.ToUpperInvariant(), 13f, SectionTitleColor);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        InsetLabel(label, 6f);
        AddRow(SectionHeight);
    }

    private void InfoRow(Slot content, string text, color textColor)
    {
        var row = content.AddSlot("Info");
        row.AttachComponent<RectTransform>();
        var element = row.AttachComponent<LayoutElement>();
        element.MinHeight.Value = InfoHeight;
        element.PreferredHeight.Value = InfoHeight;
        element.FlexibleHeight.Value = 0f;
        element.FlexibleWidth.Value = 1f;
        var label = AddFillLabel(row, text, 14f, textColor);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        InsetLabel(label, 8f);
        AddRow(InfoHeight);
    }

    // A fixed-size, non-interactive state chip (Current / Loading / Full). It looks like a button on
    // purpose - it occupies the primary action's slot - but it never pretends to do anything.
    private Slot Chip(Slot row, string text, color fill, float width)
    {
        var cell = row.AddSlot("Chip");
        cell.AttachComponent<RectTransform>();
        var element = cell.AttachComponent<LayoutElement>();
        element.MinWidth.Value = width;
        element.PreferredWidth.Value = width;
        element.FlexibleWidth.Value = 0f;
        element.MinHeight.Value = 32f;
        element.PreferredHeight.Value = 32f;
        element.FlexibleHeight.Value = 0f;
        ApplyRoundedPanel(cell, fill, RowBorder);
        AddFillLabel(cell, text, 13f, TextPrimary);
        return cell;
    }

    // Fixed-size pill button with hover/press feedback. (The shared AddInlineButton stretches to the row
    // height, which is right for 34px rows and wrong inside a 62px card.)
    private Slot AddPillButton(Slot row, string label, color fill, float width, float height, color textColor, Action onClick)
    {
        var cell = row.AddSlot(label);
        cell.AttachComponent<RectTransform>();
        var element = cell.AttachComponent<LayoutElement>();
        element.MinWidth.Value = width;
        element.PreferredWidth.Value = width;
        element.FlexibleWidth.Value = 0f;
        element.MinHeight.Value = height;
        element.PreferredHeight.Value = height;
        element.FlexibleHeight.Value = 0f;
        var background = ApplyRoundedPanel(cell, fill, RowBorder);
        var button = cell.AttachComponent<Button>();
        button.Clicked += (_, _) => onClick();
        button.AddColorDriver(background.Tint, fill, InteractionColorMode.Direct);
        AddFillLabel(cell, label, 14f, textColor);
        return cell;
    }

    private void AddRow(float height)
    {
        _rowsHeight += height;
        _rowCount++;
    }

    private void SetContentHeight()
    {
        if (_contentRect == null)
            return;
        float total = _rowsHeight + ContentPad * 2f + (_rowCount > 1 ? RowSpacing * (_rowCount - 1) : 0f);
        if (total < CardHeight)
            total = CardHeight;
        _contentRect.OffsetMin.Value = new float2(0f, -total);
        _contentRect.OffsetMax.Value = float2.Zero;
    }

    // ACTIONS

    private void FocusWorld(World world)
    {
        var manager = Manager;
        if (manager == null || world == null || world.IsDestroyed)
            return;
        _closeArmed = null;
        manager.SwitchToWorld(world);
        SetStatus($"Switching to '{WorldDisplayName(world)}'…", TextDim);
        // The focus change is applied by the world manager's next update; the FocusManager event refreshes
        // the list once it has actually landed.
        _dataDirty = true;
    }

    // Two-step close: the first press arms this world, the second one actually closes it. Closing the world
    // you're standing in would leave focus pointing at nothing (the manager nulls it and does not re-home
    // you), so focus is handed to a fallback FIRST and the button is never offered without one. -xlinka
    private void CloseWorld(World world)
    {
        var manager = Manager;
        if (manager == null || world == null || world.IsDestroyed)
            return;

        if (!ReferenceEquals(_closeArmed, world))
        {
            _closeArmed = world;
            var armed = world;
            World?.RunInSeconds(4f, () =>
            {
                if (IsDestroyed || !ReferenceEquals(_closeArmed, armed))
                    return;
                _closeArmed = null;
                if (Slot != null && Slot.ActiveSelf.Value && _tab != Tab.New)
                    RefreshList();
            });
            RefreshList();
            return;
        }

        _closeArmed = null;
        var fallback = FindFallbackWorld(world);
        if (fallback == null)
        {
            SetStatus("That's your only open world - nowhere to go if it closes.", WarnText);
            RefreshList();
            return;
        }

        string name = WorldDisplayName(world);
        if (ReferenceEquals(world, manager.FocusedWorld))
            manager.SwitchToWorld(fallback);
        manager.DestroyWorld(world);
        SetStatus($"Closed '{name}'.", TextDim);
        _dataDirty = true;
        RefreshList();
    }

    // Somewhere to land when a world closes: prefer the local home, else any other running world.
    private World? FindFallbackWorld(World closing)
    {
        var manager = Manager;
        if (manager == null)
            return null;
        var userspace = manager.UserspaceWorld;
        World? any = null;
        foreach (var world in manager.Worlds)
        {
            if (world == null || world.IsDestroyed || ReferenceEquals(world, closing) || ReferenceEquals(world, userspace))
                continue;
            if (world.Focus == World.WorldFocus.Overlay || world.Focus == World.WorldFocus.PrivateOverlay)
                continue;
            if (world.State != World.WorldState.Running)
                continue;
            if (world.Name == "LocalHome")
                return world;
            any ??= world;
        }
        return any;
    }

    private void JoinSession(SessionListEntry entry)
    {
        var manager = Manager;
        var url = entry?.JoinUrl;
        if (manager == null || url == null)
            return;

        var loader = Lumora.Core.Engine.Current?.WorldLoadingService;
        if (loader != null && loader.IsLoading)
        {
            SetStatus($"Already joining '{loader.CurrentOperation?.WorldName}' - one at a time.", WarnText);
            return;
        }

        string name = string.IsNullOrEmpty(entry!.Name) ? url.Host : entry.Name;
        SetStatus($"Joining '{name}'…", TextDim);

        // Load + connect in the BACKGROUND and only focus once the world is actually Running. The sync
        // WorldManager.JoinSession would AddWorld + focus a half-initialized world instantly - that's what
        // dumped the user into a black loading world (mouse/dash dead), and it even reported "success" when
        // the connect failed. The loading service keeps the user in their current world until the join is
        // ready (3D indicator while it loads), and cleanly bails on failure. -xlinka
        if (loader != null)
        {
            loader.JoinSessionAsync(name, url, focusWhenReady: true);
            _dataDirty = true;
            return;
        }

        // Fallback if the loading service somehow isn't up yet.
        ushort port = url.Port > 0 ? (ushort)url.Port : (ushort)0;
        var world = manager.JoinSession(name, url.Host, port);
        if (world != null)
            manager.SwitchToWorld(world);
        _dataDirty = true;
    }

    private void OpenSavedWorld(string path)
    {
        var manager = Manager;
        if (manager == null)
            return;
        var world = manager.OpenSavedWorld(path);
        SetStatus(world != null
            ? $"Opened '{System.IO.Path.GetFileNameWithoutExtension(path)}'."
            : $"Couldn't open '{System.IO.Path.GetFileNameWithoutExtension(path)}'.",
            world != null ? TextDim : WarnText);
        RefreshSavedFiles();
        _dataDirty = true;
    }

    // CREATE PAGE

    private void BuildCreatePage(Slot body)
    {
        _createPage = body.AddSlot("Create");
        FillParent(_createPage.AttachComponent<RectTransform>());
        var col = _createPage.AttachComponent<VerticalLayout>();
        col.Spacing.Value = 8f;
        col.ForceExpandWidth.Value = true;
        col.ForceExpandHeight.Value = false;

        var nameRow = BeginRow(_createPage, "Name");
        var nameField = nameRow.AddSlot("Field");
        nameField.AttachComponent<RectTransform>();
        var nameElement = nameField.AttachComponent<LayoutElement>();
        nameElement.MinWidth.Value = 200f;
        nameElement.FlexibleWidth.Value = 1f;
        nameElement.FlexibleHeight.Value = 1f;
        _newWorldNameBackground = ApplyRoundedPanel(nameField, ControlFill, RowBorder);
        nameField.AttachComponent<Button>().Clicked += (_, _) => SetFieldFocus(FieldFocus.NewWorldName);
        _newWorldNameLabel = AddFillLabel(nameField, string.Empty, 14f, TextDim);
        _newWorldNameLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        InsetLabel(_newWorldNameLabel, 12f);
        UpdateNewWorldNameLabel();

        var columns = _createPage.AddSlot("Columns");
        columns.AttachComponent<RectTransform>();
        var columnsElement = columns.AttachComponent<LayoutElement>();
        columnsElement.FlexibleWidth.Value = 1f;
        columnsElement.FlexibleHeight.Value = 1f;
        var columnsLayout = columns.AttachComponent<HorizontalLayout>();
        columnsLayout.Spacing.Value = 16f;
        columnsLayout.ForceExpandWidth.Value = true;
        columnsLayout.ForceExpandHeight.Value = true;

        var left = AddFormColumn(columns);
        var right = AddFormColumn(columns);

        FormHeader(left, "Template");
        foreach (var template in WorldTemplates.AvailableTemplates)
        {
            var captured = template;
            RadioRow(left, "worlds-template", PrettyTemplate(template), template == _template, () =>
            {
                _template = captured;
                UpdateNewWorldNameLabel();
            });
        }

        FormHeader(left, "Mode");
        foreach (WorldMode mode in Enum.GetValues<WorldMode>())
        {
            var captured = mode;
            RadioRow(left, "worlds-mode", PrettyMode(mode), mode == _mode, () => _mode = captured);
        }

        FormHeader(right, "Who Can Join");
        foreach (SessionVisibility visibility in Enum.GetValues<SessionVisibility>())
        {
            var captured = visibility;
            RadioRow(right, "worlds-access", PrettyVisibility(visibility), visibility == _visibility,
                () => _visibility = captured);
        }

        FormHeader(right, "Session");
        SliderRow(right, "Max Users", 1f, 64f, _maxUsers,
            v => { _maxUsers = (int)MathF.Round(v); return _maxUsers.ToString(); });

        var create = _createPage.AddSlot("CreateButton");
        create.AttachComponent<RectTransform>();
        create.AttachComponent<GraphicChunkRoot>();
        SetFixedHeight(create, 40f);
        var createBackground = ApplyRoundedPanel(create, JoinFill, RowBorder);
        var createButton = create.AttachComponent<Button>();
        createButton.Clicked += (_, _) => HostWorld();
        createButton.AddColorDriver(createBackground.Tint, JoinFill, InteractionColorMode.Direct);
        AddFillLabel(create, "Create & Host", 16f, TextPrimary);

        var status = _createPage.AddSlot("CreateStatus");
        status.AttachComponent<RectTransform>();
        status.AttachComponent<GraphicChunkRoot>();
        SetFixedHeight(status, 24f);
        _createStatus = AddFillLabel(status, "Pick a template, name it, then create & host.", 13f, TextDim);
        _createStatus.HorizontalAlignment.Value = TextHorizontalAlignment.Left;

        _createPage.ActiveSelf.Value = false;
    }

    private static Slot AddFormColumn(Slot parent)
    {
        var column = parent.AddSlot("Column");
        column.AttachComponent<RectTransform>();
        var element = column.AttachComponent<LayoutElement>();
        element.FlexibleWidth.Value = 1f;
        element.FlexibleHeight.Value = 1f;
        var layout = column.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = 5f;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;
        return column;
    }

    private void FormHeader(Slot parent, string title)
    {
        var row = parent.AddSlot(title + "Header");
        row.AttachComponent<RectTransform>();
        SetFixedHeight(row, 26f);
        var label = AddFillLabel(row, title, 16f, SectionTitleColor);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }

    private void RadioRow(Slot parent, string group, string label, bool isChecked, Action onSelect)
    {
        var row = BeginRow(parent, label);
        var b = RowBuilder(row);
        b.MinWidth(180f).FlexibleWidth(1f);
        AddRowLabel(b, label, 15f, TextPrimary, TextHorizontalAlignment.Left);
        b.MinWidth(26f).PreferredWidth(26f).FlexibleWidth(0f);
        b.Radio(group, isChecked, (_, on) => { if (on) onSelect(); });
    }

    private void SliderRow(Slot parent, string label, float min, float max, float value, Func<float, string> applyAndFormat)
    {
        var row = BeginRow(parent, label);
        var b = RowBuilder(row);

        b.MinWidth(120f).PreferredWidth(120f).FlexibleWidth(0f);
        AddRowLabel(b, label, 15f, TextPrimary, TextHorizontalAlignment.Left);

        Text? valueText = null;
        b.MinWidth(120f).PreferredWidth(240f).FlexibleWidth(1f);
        b.Slider(value, min, max, (_, v) =>
        {
            var formatted = applyAndFormat(v);
            if (valueText != null && !valueText.IsDestroyed)
                valueText.Content.Value = formatted;
        });

        b.MinWidth(60f).PreferredWidth(60f).FlexibleWidth(0f);
        valueText = AddRowLabel(b, applyAndFormat(value), 15f, TextDim, TextHorizontalAlignment.Right);
    }

    private void HostWorld()
    {
        var manager = Manager;
        if (manager == null)
        {
            SetCreateStatus("No world manager available.", WarnText);
            return;
        }

        var name = string.IsNullOrWhiteSpace(_newWorldName) ? PrettyTemplate(_template) : _newWorldName.Trim();
        // Clamp to the modes this template allows (a template may be social-only).
        var mode = WorldTemplates.DefaultMode(_template);
        foreach (var allowed in WorldTemplates.AllowedModes(_template))
        {
            if (allowed == _mode) { mode = _mode; break; }
        }

        SetCreateStatus($"Hosting '{name}'…", TextDim);
        var world = manager.HostNewWorld(_template, name, _visibility, _maxUsers, mode);
        if (world == null)
        {
            SetCreateStatus("Failed to host world.", WarnText);
            return;
        }

        SetCreateStatus($"Now hosting '{name}' ({ModeLabel(mode)}).", TextDim);
        // Hosting focuses the new world and we leave the create page, so repeat the result where the user
        // is about to be looking.
        SetStatus($"Now hosting '{name}' ({ModeLabel(mode)}).", TextDim);
        _newWorldName = string.Empty;
        SetFieldFocus(FieldFocus.None);
        UpdateNewWorldNameLabel();
        _dataDirty = true;
        // Hosting drops you into the new world, so show it in the open list.
        SelectTab(Tab.Open);
    }

    // FILTER FIELD / KEY INPUT

    private void SetFieldFocus(FieldFocus focus)
    {
        _focus = focus;
        if (_filterBackground != null && !_filterBackground.IsDestroyed)
            _filterBackground.BorderTint.Value = focus == FieldFocus.Filter ? AccentColor : RowBorder;
        if (_newWorldNameBackground != null && !_newWorldNameBackground.IsDestroyed)
            _newWorldNameBackground.BorderTint.Value = focus == FieldFocus.NewWorldName ? AccentColor : RowBorder;
        UpdateFilterLabel();
        UpdateNewWorldNameLabel();
        MarkDirty();
    }

    private void UpdateFilterLabel()
    {
        if (_filterLabel == null || _filterLabel.IsDestroyed)
            return;
        bool focused = _focus == FieldFocus.Filter;
        if (_filter.Length == 0)
        {
            _filterLabel.Content.Value = focused ? "|" : "Click here, then type to filter by name, host or mode";
            _filterLabel.Color.Value = TextDim;
        }
        else
        {
            _filterLabel.Content.Value = focused ? _filter + "|" : _filter;
            _filterLabel.Color.Value = TextPrimary;
        }
    }

    private void UpdateNewWorldNameLabel()
    {
        if (_newWorldNameLabel == null || _newWorldNameLabel.IsDestroyed)
            return;
        bool focused = _focus == FieldFocus.NewWorldName;
        if (_newWorldName.Length == 0)
        {
            _newWorldNameLabel.Content.Value = focused ? "|" : $"World name (defaults to \"{PrettyTemplate(_template)}\")";
            _newWorldNameLabel.Color.Value = TextDim;
        }
        else
        {
            _newWorldNameLabel.Content.Value = focused ? _newWorldName + "|" : _newWorldName;
            _newWorldNameLabel.Color.Value = TextPrimary;
        }
    }

    public bool ConsumeChar(char c)
    {
        if (_focus == FieldFocus.None)
            return false;
        if (char.IsControl(c))
            return true;
        if (_focus == FieldFocus.Filter)
        {
            if (_filter.Length >= 48)
                return true;
            _filter += c;
            UpdateFilterLabel();
            RefreshList();
            return true;
        }
        if (_newWorldName.Length >= 48)
            return true;
        _newWorldName += c;
        UpdateNewWorldNameLabel();
        MarkDirty();
        return true;
    }

    public bool ConsumeBackspace()
    {
        if (_focus == FieldFocus.None)
            return false;
        if (_focus == FieldFocus.Filter)
        {
            if (_filter.Length > 0)
                _filter = _filter.Substring(0, _filter.Length - 1);
            UpdateFilterLabel();
            RefreshList();
            return true;
        }
        if (_newWorldName.Length > 0)
            _newWorldName = _newWorldName.Substring(0, _newWorldName.Length - 1);
        UpdateNewWorldNameLabel();
        MarkDirty();
        return true;
    }

    public bool ConsumeEnter()
    {
        if (_focus == FieldFocus.None)
            return false;
        if (_focus == FieldFocus.NewWorldName)
        {
            SetFieldFocus(FieldFocus.None);
            HostWorld();
            return true;
        }
        SetFieldFocus(FieldFocus.None);
        return true;
    }

    public bool ConsumeEscape()
    {
        if (_focus == FieldFocus.None)
            return false;
        if (_focus == FieldFocus.Filter && _filter.Length > 0)
        {
            _filter = string.Empty;
            SetFieldFocus(FieldFocus.None);
            RefreshList();
            return true;
        }
        SetFieldFocus(FieldFocus.None);
        return true;
    }

    private bool Matches(string title, string meta)
    {
        if (_filter.Length == 0)
            return true;
        return title.Contains(_filter, StringComparison.OrdinalIgnoreCase)
            || meta.Contains(_filter, StringComparison.OrdinalIgnoreCase);
    }

    // SCROLLBAR

    private void OnHandlePress(UIInteractionContext context)
    {
        _handlePressY = context.LocalPoint.y;
        _handlePressScroll = _scroll?.AbsolutePosition.y ?? 0f;
    }

    private void OnHandleDrag(UIInteractionContext context)
    {
        if (_scroll == null || _viewportRect == null || _contentRect == null)
            return;
        float viewportHeight = _viewportRect.LocalComputeRect.height;
        float contentHeight = _contentRect.LocalComputeRect.height;
        if (viewportHeight <= LaidOutViewportFloor || contentHeight <= 0f)
            return;
        float maxScroll = MathF.Max(0f, contentHeight - viewportHeight);
        if (maxScroll <= 0f)
            return;
        float handleHeight = MathF.Max(30f, viewportHeight * (viewportHeight / contentHeight));
        float travel = viewportHeight - handleHeight;
        if (travel <= 0f)
            return;
        // Dragging the handle down (local Y decreases) scrolls the content down. Setting AbsolutePosition
        // routes through ScrollRect and fires ScrollChanged -> UpdateScrollHandle; no explicit canvas dirty,
        // which would re-tessellate the whole list on every drag frame.
        float deltaY = context.LocalPoint.y - _handlePressY;
        float scrolled = _handlePressScroll - deltaY * (maxScroll / travel);
        _scroll.AbsolutePosition = new float2(0f, System.Math.Clamp(scrolled, 0f, maxScroll));
    }

    private void UpdateScrollHandle()
    {
        if (_scroll == null || _scrollTrack == null || _scrollHandle == null
            || _viewportRect == null || _contentRect == null)
            return;
        float viewportHeight = _viewportRect.LocalComputeRect.height;
        if (viewportHeight <= LaidOutViewportFloor)
        {
            // Layout hasn't produced the real viewport rect yet. Retry next frame, bounded.
            if (_scrollHandleRetries++ < 30)
                World?.RunInUpdates(1, UpdateScrollHandle);
            return;
        }
        _scrollHandleRetries = 0;

        float contentHeight = _contentRect.LocalComputeRect.height;
        if (contentHeight <= 0f)
            return;

        float maxScroll = MathF.Max(0f, contentHeight - viewportHeight);
        // Gate the ActiveSelf writes: Sync.Value isn't change-gated, so re-writing the same value still
        // flags a layout change - and this runs on every scroll frame.
        if (maxScroll <= 0.5f)
        {
            if (_scrollTrack.ActiveSelf.Value)
                _scrollTrack.ActiveSelf.Value = false;   // everything fits, no bar
            if (_scroll.NormalizedPosition.y != 0f)
                _scroll.NormalizedPosition = float2.Zero;
            return;
        }
        if (!_scrollTrack.ActiveSelf.Value)
            _scrollTrack.ActiveSelf.Value = true;
        if (_scroll.AbsolutePosition.y > maxScroll)
            _scroll.AbsolutePosition = new float2(0f, maxScroll);
        float handleHeight = MathF.Max(30f, viewportHeight * (viewportHeight / contentHeight));
        float fraction = System.Math.Clamp(_scroll.AbsolutePosition.y / maxScroll, 0f, 1f);
        float offset = fraction * (viewportHeight - handleHeight);
        _scrollHandle.OffsetMax.Value = new float2(-2f, -offset);
        _scrollHandle.OffsetMin.Value = new float2(2f, -(offset + handleHeight));
    }

    // HELPERS

    // One-line result of the last action ("Closed 'X'", "Already joining…"). It clears itself so a stale
    // message can't sit there reading like current state. -xlinka
    private void SetStatus(string text, color textColor)
    {
        if (_statusRow == null || _statusRow.IsDestroyed || _statusLabel == null || _statusLabel.IsDestroyed)
            return;
        _statusLabel.Content.Value = text;
        _statusLabel.Color.Value = textColor;
        _statusRow.ActiveSelf.Value = !string.IsNullOrEmpty(text);
        MarkDirty();

        int token = ++_statusToken;
        World?.RunInSeconds(6f, () =>
        {
            if (IsDestroyed || token != _statusToken || _statusRow == null || _statusRow.IsDestroyed)
                return;
            _statusRow.ActiveSelf.Value = false;
            MarkDirty();
        });
    }

    private int _statusToken;

    private void SetCreateStatus(string text, color textColor)
    {
        if (_createStatus == null || _createStatus.IsDestroyed)
            return;
        _createStatus.Content.Value = text;
        _createStatus.Color.Value = textColor;
        MarkDirty();
    }

    private static SessionBrowser? GetBrowser()
    {
        var root = Lumora.Core.Engine.Current?.WorldManager?.UserspaceWorld?.RootSlot;
        if (root == null)
            return null;
        var browser = root.GetComponent<SessionBrowser>() ?? root.AttachComponent<SessionBrowser>();
        browser.StartScanning();   // idempotent
        return browser;
    }

    private static void FillParent(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }

    private static void InsetLabel(Text label, float left)
    {
        var rect = label.RectTransform;
        if (rect != null)
            rect.OffsetMin.Value = new float2(left, 0f);
    }

    private static string WorldDisplayName(World world)
        => string.IsNullOrEmpty(world.WorldName?.Value) ? world.Name : world.WorldName!.Value;

    private static string UserCount(int n) => n == 1 ? "1 user" : $"{n} users";

    private static string Truncate(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max - 1) + "…";

    private static string FormatBytes(long bytes)
    {
        const long KB = 1024;
        const long MB = KB * 1024;
        if (bytes >= MB) return $"{bytes / (double)MB:0.0} MB";
        if (bytes >= KB) return $"{bytes / (double)KB:0.0} KB";
        return $"{bytes} B";
    }

    private static color ModeColor(WorldMode mode) => mode switch
    {
        WorldMode.Social => SocialTint,
        WorldMode.Event => EventTint,
        _ => BuilderTint,
    };

    private static string ModeLabel(WorldMode mode) => mode switch
    {
        WorldMode.Social => "Social",
        WorldMode.Event => "Event",
        _ => "Builder",
    };

    private static string AccessLabel(World.WorldAccessLevel level) => level switch
    {
        World.WorldAccessLevel.Private => "Private",
        World.WorldAccessLevel.LAN => "LAN",
        World.WorldAccessLevel.Contacts => "Contacts",
        World.WorldAccessLevel.ContactsPlus => "Contacts+",
        World.WorldAccessLevel.Anyone => "Public",
        _ => level.ToString(),
    };

    private static string PrettyTemplate(string template) => template switch
    {
        "LocalHome" => "Home Space",
        "Grid" => "Grid Space",
        "Scratch" => "Scratch Space",
        _ => template,
    };

    private static string PrettyMode(WorldMode mode) => mode switch
    {
        WorldMode.Builder => "Builder (full editing)",
        WorldMode.Social => "Social (no editing)",
        WorldMode.Event => "Event (view only)",
        _ => mode.ToString(),
    };

    private static string PrettyVisibility(SessionVisibility visibility) => visibility switch
    {
        SessionVisibility.Private => "Private (invite only)",
        SessionVisibility.LAN => "LAN (local network)",
        SessionVisibility.Contacts => "Contacts",
        SessionVisibility.Public => "Anyone",
        _ => visibility.ToString(),
    };
}
