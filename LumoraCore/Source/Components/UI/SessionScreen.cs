// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Math;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Dashboard Session screen with top sub-tabs (Settings / Users / Permissions) for the focused
/// world. Settings binds the world configuration; Users lists the world's users; Permissions
/// (built out later) edits roles/capabilities backed by the hard host-authoritative datamodel gate.
/// </summary>
public sealed class SessionScreen : WidgetScreen
{
    private const float TabBarHeight = 44f;

    private static readonly color TabActiveFill = new color(0.45f, 0.38f, 0.80f, 0.90f);
    private static readonly color SaveFill = new color(0.28f, 0.60f, 0.40f, 0.95f);
    private static readonly color KickFill = new color(0.70f, 0.24f, 0.28f, 0.95f);

    private readonly List<(Slot page, BorderedImage tab)> _tabs = new();

    // Settings scroll machinery (the form is taller than the panel).
    private ScrollRect? _scroll;
    private RectTransform? _viewportRect;
    private RectTransform? _contentRect;
    private Slot? _scrollTrack;
    private RectTransform? _scrollHandle;
    private Slot? _leftColumn;
    private Slot? _rightColumn;
    private float _handlePressY;
    private float _handlePressScroll;
    private int _scrollHandleRetries;

    // A freshly-built viewport sits at the RectTransform default (100x100) until the canvas runs a
    // layout pass. Sizing the scrollbar off that 100px makes maxScroll = content - 100 (huge) with a
    // tiny thumb, so the short form scrolls out of view into empty space. This sentinel rejects the
    // un-laid-out rect; the real viewport is ~424, so legitimate sizes are never refused. -xlinka
    private const float LaidOutViewportFloor = 100f;
    private readonly List<SettingsSection> _sections = new();
    private Slot? _settingsPage;
    private Slot? _usersPage;
    private Slot? _permissionsPage;
    private FocusManager? _focusManager;

    private World? FocusedWorld => Lumora.Core.Engine.Current?.WorldManager?.FocusedWorld;

    // A collapsible section: a clickable header that shows/hides its body of rows.
    private sealed class SettingsSection
    {
        public required Slot Body;
        public required Text Chevron;
        public bool Expanded = true;
    }

    protected override void OnShow()
    {
        base.OnShow();
        SubscribeFocus();
        // Settings / Users / Permissions are PER-WORLD. Re-point every tab at whatever
        // world is focused right now (hosting or switching worlds changes the focus), so
        // the screen never stays stuck on the world it was first built for.
        RebuildForFocusedWorld();
        // Layout computes the rects after the screen is shown; size the scrollbar then.
        World.RunInUpdates(2, UpdateScrollHandle);
    }

    public override void OnDestroy()
    {
        if (_focusManager != null)
        {
            _focusManager.OnFocusedWorldChanged -= HandleFocusedWorldChanged;
            _focusManager = null;
        }
        base.OnDestroy();
    }

    // Re-point all three tabs at the currently focused world.
    private void RebuildForFocusedWorld()
    {
        RebuildSettings();
        RebuildUsersList();
        RebuildPermissions();
        _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    private void SubscribeFocus()
    {
        if (_focusManager != null)
            return;
        _focusManager = Lumora.Core.Engine.Current?.FocusManager;
        if (_focusManager != null)
            _focusManager.OnFocusedWorldChanged += HandleFocusedWorldChanged;
    }

    private void HandleFocusedWorldChanged(World oldWorld, World newWorld)
    {
        // Only a visible Session screen needs to re-point live; a hidden one rebuilds the
        // next time it is shown (OnShow). Defer a frame so the focus switch fully settles.
        if (IsDestroyed || !Slot.ActiveSelf.Value)
            return;
        World?.RunInUpdates(1, () =>
        {
            if (IsDestroyed || !Slot.ActiveSelf.Value)
                return;
            RebuildForFocusedWorld();
        });
    }

    private static string WorldDisplayName(World world)
        => string.IsNullOrEmpty(world.WorldName?.Value) ? world.Name : world.WorldName!.Value;

    protected override void BuildContent(UIBuilder builder)
    {
        _dashboard = Slot.GetComponentInParents<Dashboard>();
        _tabs.Clear();

        var root = builder.Current;
        var col = root.AttachComponent<VerticalLayout>();
        col.Spacing.Value = 10f;
        col.PaddingLeft.Value = 16f;
        col.PaddingRight.Value = 16f;
        col.PaddingTop.Value = 16f;
        col.PaddingBottom.Value = 16f;
        col.ForceExpandWidth.Value = true;
        col.ForceExpandHeight.Value = false;

        var tabBar = root.AddSlot("Tabs");
        tabBar.AttachComponent<RectTransform>();
        SetFixedHeight(tabBar, TabBarHeight);
        var tabRow = tabBar.AttachComponent<HorizontalLayout>();
        tabRow.Spacing.Value = 8f;
        tabRow.ForceExpandWidth.Value = true;
        tabRow.ForceExpandHeight.Value = true;

        var contentHost = root.AddSlot("Content");
        contentHost.AttachComponent<RectTransform>();
        var hostElement = contentHost.AttachComponent<LayoutElement>();
        hostElement.FlexibleWidth.Value = 1f;
        hostElement.FlexibleHeight.Value = 1f;

        AddTab(tabBar, contentHost, "Settings", BuildSettingsPage);
        AddTab(tabBar, contentHost, "Users", BuildUsersPage);
        AddTab(tabBar, contentHost, "Permissions", BuildPermissionsPage);

        SelectTab(0);
    }

    // TABS

    private void AddTab(Slot tabBar, Slot contentHost, string name, Action<Slot> buildPage)
    {
        int index = _tabs.Count;

        var tabSlot = tabBar.AddSlot(name);
        tabSlot.AttachComponent<RectTransform>();
        var element = tabSlot.AttachComponent<LayoutElement>();
        element.FlexibleWidth.Value = 1f;
        element.FlexibleHeight.Value = 1f;
        var background = ApplyRoundedPanel(tabSlot, TabFill, RowBorder);

        var button = tabSlot.AttachComponent<Button>();
        button.Clicked += (_, _) => SelectTab(index);

        AddFillLabel(tabSlot, name, 18f, TextPrimary);

        var page = contentHost.AddSlot(name);
        var pageRect = page.AttachComponent<RectTransform>();
        pageRect.AnchorMin.Value = float2.Zero;
        pageRect.AnchorMax.Value = float2.One;
        pageRect.OffsetMin.Value = float2.Zero;
        pageRect.OffsetMax.Value = float2.Zero;
        page.ActiveSelf.Value = false;

        var pageLayout = page.AttachComponent<VerticalLayout>();
        pageLayout.Spacing.Value = 8f;
        pageLayout.ForceExpandWidth.Value = true;
        pageLayout.ForceExpandHeight.Value = false;

        _tabs.Add((page, background));
        buildPage(page);
    }

    private void SelectTab(int index)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            var (page, tab) = _tabs[i];
            if (page != null && !page.IsDestroyed)
                page.ActiveSelf.Value = i == index;
            if (tab != null && !tab.IsDestroyed)
                tab.Tint.Value = i == index ? TabActiveFill : TabFill;
        }
        _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    // SETTINGS PAGE

    private void BuildSettingsPage(Slot page)
    {
        _settingsPage = page;
        RebuildSettings();
    }

    private void RebuildSettings()
    {
        var page = _settingsPage;
        if (page == null)
            return;
        page.DestroyChildren();
        _sections.Clear();
        _scrollHandleRetries = 0; // fresh build: restart the wait-for-layout retry budget

        var world = FocusedWorld;
        if (world == null)
        {
            AddRow(page, "No focused world.");
            return;
        }

        var config = world.Configuration;
        if (config == null)
        {
            AddRow(page, "World settings are still syncing…");
            return;
        }

        // Make it unmistakable WHICH world these settings belong to (they are per-world).
        var titleRow = page.AddSlot("WorldTitle");
        titleRow.AttachComponent<RectTransform>();
        SetFixedHeight(titleRow, 26f);
        var titleLabel = AddFillLabel(titleRow, $"Configuring: {WorldDisplayName(world)}", 16f, SectionTitleColor);
        titleLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Left;

        // Masked, scrollable two-column form (it's taller than the panel) with a
        // draggable scrollbar on the right. Every toggle/slider applies live in its
        // callback - there is no "save settings" step. Save Changes under World Save
        // Options persists the world's scene to disk, separate from these settings.
        var area = page.AddSlot("ScrollArea");
        area.AttachComponent<RectTransform>();
        var areaElement = area.AttachComponent<LayoutElement>();
        areaElement.FlexibleWidth.Value = 1f;
        areaElement.FlexibleHeight.Value = 1f;
        var areaLayout = area.AttachComponent<HorizontalLayout>();
        areaLayout.Spacing.Value = 6f;
        areaLayout.ForceExpandWidth.Value = false;
        areaLayout.ForceExpandHeight.Value = true;

        var viewport = area.AddSlot("Viewport");
        _viewportRect = viewport.AttachComponent<RectTransform>();
        var viewportElement = viewport.AttachComponent<LayoutElement>();
        viewportElement.FlexibleWidth.Value = 1f;
        viewportElement.FlexibleHeight.Value = 1f;
        viewport.AttachComponent<Mask>();
        _scroll = viewport.AttachComponent<ScrollRect>();
        _scroll.ScrollSensitivity.Value = new float2(1f, 1f);
        _scroll.ScrollChanged += (_, _) => UpdateScrollHandle();

        var content = viewport.AddSlot("Content");
        _contentRect = content.AttachComponent<RectTransform>();
        // Render-offset scrolling (off by default): give the content its own chunk so it slides as a unit
        // while the viewport mask/background stay fixed. -xlinka
        if (Canvas.ScrollRenderOffset)
            content.AttachComponent<GraphicChunkRoot>();
        // Top-pinned, full-width content strip; its HEIGHT is set explicitly by RecomputeContentHeight.
        // (The ContentSizeFitter approach was reverted: it resized this rect mid-layout-pass and, combined
        // with ScrollRect mutating the same rect, never let the canvas settle - it re-dirtied every frame and
        // froze the deep Session form. The proper fix resolves self-size inside the single compute pass + scrolls
        // by a render offset; until we do that migration, pin the height explicitly like before.) -xlinka
        _contentRect.AnchorMin.Value = new float2(0f, 1f);
        _contentRect.AnchorMax.Value = new float2(1f, 1f);
        _contentRect.OffsetMin.Value = new float2(0f, -100f);
        _contentRect.OffsetMax.Value = float2.Zero;
        var columns = content.AttachComponent<HorizontalLayout>();
        columns.Spacing.Value = 16f;
        columns.ForceExpandWidth.Value = true;
        columns.ForceExpandHeight.Value = true;
        _scroll.Content.Target = _contentRect;

        _leftColumn = AddColumn(content);
        _rightColumn = AddColumn(content);

        BuildScrollbar(area);

        // LEFT: identity, basics, world save options.
        var worldBody = AddSection(_leftColumn, "World");
        AddRow(worldBody, $"World Name: {world.WorldName?.Value ?? world.Name}");
        SliderRow(worldBody, "Max Users", 1f, 64f, config.MaxUsers.Value,
            v => { config.MaxUsers.Value = (int)MathF.Round(v); return config.MaxUsers.Value.ToString(); });
        ToggleRow(worldBody, "Mobile Friendly", config.MobileFriendly.Value, v => config.MobileFriendly.Value = v);
        ToggleRow(worldBody, "Allow Joining", config.AllowJoin.Value, v => config.AllowJoin.Value = v);
        // No "Public" toggle - public-ness is just the "Anyone" access level below, not a separate flag. -xlinka
        AddRow(worldBody, $"Description: {(string.IsNullOrEmpty(config.Description.Value) ? "(none)" : config.Description.Value)}");

        var saveBody = AddSection(_leftColumn, "World Save Options");
        SaveButtonRow(saveBody, "Save Changes", SaveFill, () => SaveFocusedWorld(world));
        SaveButtonRow(saveBody, "Save As…", TabFill, () => SaveWorldCopy(world));
        SaveButtonRow(saveBody, "Save Copy…", TabFill, () => SaveWorldCopy(world));

        // RIGHT: who-can-join, session policy.
        var accessBody = AddSection(_rightColumn, "Who Can Join This World?");
        foreach (var level in Enum.GetValues<World.WorldAccessLevel>())
        {
            var captured = level;
            RadioRow(accessBody, "join-access", PrettyAccess(level), config.AccessLevel.Value == level,
                () =>
                {
                    // Host-only: changing access re-advertises the session + starts/stops the LAN beacon live
                    // (WorldSettings.AccessLevel handler). Re-render so the new selection actually shows. -xlinka
                    if (!world.IsAuthority) return;
                    config.AccessLevel.Value = captured;
                    RebuildSettings();
                });
        }

        var sessionBody = AddSection(_rightColumn, "Session");
        ToggleRow(sessionBody, "Edit Mode", config.EditMode.Value, v => config.EditMode.Value = v);
        ToggleRow(sessionBody, "Auto-Kick AFK", config.AutoKickAFK.Value, v => config.AutoKickAFK.Value = v);
        SliderRow(sessionBody, "Max AFK (min)", 1f, 120f, config.MaxAFKMinutes.Value,
            v => { config.MaxAFKMinutes.Value = (int)MathF.Round(v); return $"{config.MaxAFKMinutes.Value} min"; });
        ToggleRow(sessionBody, "Hide From Lists", config.HideFromSessionLists.Value, v => config.HideFromSessionLists.Value = v);
        SliderRow(sessionBody, "Autosave (sec)", 0f, 600f, config.AutoSaveInterval.Value,
            v =>
            {
                config.AutoSaveInterval.Value = MathF.Round(v / 30f) * 30f;
                return config.AutoSaveInterval.Value <= 0f ? "Off" : $"{config.AutoSaveInterval.Value:0}s";
            });
        ToggleRow(sessionBody, "Cleanup Assets", config.CleanupUnusedAssets.Value, v => config.CleanupUnusedAssets.Value = v);
        SliderRow(sessionBody, "Cleanup Every (s)", 30f, 1800f, config.AssetCleanupInterval.Value,
            v => { config.AssetCleanupInterval.Value = MathF.Round(v / 30f) * 30f; return $"{config.AssetCleanupInterval.Value:0}s"; });

        RecomputeContentHeight();
        // Size/position the scrollbar once layout has computed the rects.
        World.RunInUpdates(2, UpdateScrollHandle);
    }

    private static Slot AddColumn(Slot parent)
    {
        var column = parent.AddSlot("Column");
        column.AttachComponent<RectTransform>();
        var element = column.AttachComponent<LayoutElement>();
        element.FlexibleWidth.Value = 1f;
        element.FlexibleHeight.Value = 1f;
        var layout = column.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = 4f;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;
        return column;
    }

    // Adds a collapsible section: a clickable header (chevron + title) and a body of
    // rows. Returns the body to fill; the caller adds rows to it.
    private Slot AddSection(Slot column, string title)
    {
        var header = column.AddSlot(title + "Header");
        header.AttachComponent<RectTransform>();
        header.AttachComponent<GraphicChunkRoot>();
        SetFixedHeight(header, 28f);
        ApplyRoundedPanel(header, TabFill, RowBorder);
        var hl = header.AttachComponent<HorizontalLayout>();
        hl.Spacing.Value = 8f;
        hl.PaddingLeft.Value = 6f;
        hl.PaddingRight.Value = 6f;
        hl.ForceExpandWidth.Value = false;
        hl.ForceExpandHeight.Value = true;

        var hb = RowBuilder(header);
        hb.MinWidth(18f).PreferredWidth(18f).FlexibleWidth(0f);
        var chevron = AddRowLabel(hb, "▼", 15f, SectionTitleColor, TextHorizontalAlignment.Center);
        hb.MinWidth(100f).FlexibleWidth(1f);
        AddRowLabel(hb, title, 17f, SectionTitleColor, TextHorizontalAlignment.Left);
        var button = header.AttachComponent<Button>();

        var body = column.AddSlot(title + "Body");
        body.AttachComponent<RectTransform>();
        body.AttachComponent<LayoutElement>();
        var bodyLayout = body.AttachComponent<VerticalLayout>();
        bodyLayout.Spacing.Value = 3f;
        bodyLayout.PaddingTop.Value = 2f;
        bodyLayout.PaddingBottom.Value = 4f;
        bodyLayout.ForceExpandWidth.Value = true;
        bodyLayout.ForceExpandHeight.Value = false;

        var section = new SettingsSection { Body = body, Chevron = chevron };
        button.Clicked += (_, _) => ToggleSection(section);
        _sections.Add(section);
        return body;
    }

    private void ToggleSection(SettingsSection section)
    {
        section.Expanded = !section.Expanded;
        section.Body.ActiveSelf.Value = section.Expanded;
        section.Chevron.Content.Value = section.Expanded ? "▼" : "▶";
        RecomputeContentHeight();
        if (_scroll != null)
            _scroll.NormalizedPosition = float2.Zero;
        // The rects only update on the next rebuild, so size the handle a frame later.
        World.RunInUpdates(1, UpdateScrollHandle);
        _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    // SCROLLBAR

    private void BuildScrollbar(Slot area)
    {
        var track = area.AddSlot("Scrollbar");
        _scrollTrack = track;
        track.AttachComponent<RectTransform>();
        var trackElement = track.AttachComponent<LayoutElement>();
        trackElement.MinWidth.Value = 18f;
        trackElement.PreferredWidth.Value = 18f;
        trackElement.FlexibleWidth.Value = 0f;
        trackElement.FlexibleHeight.Value = 1f;
        ApplyRoundedPanel(track, TabFill, RowBorder);

        var handleSlot = track.AddSlot("Handle");
        _scrollHandle = handleSlot.AttachComponent<RectTransform>();
        _scrollHandle.AnchorMin.Value = new float2(0f, 1f);
        _scrollHandle.AnchorMax.Value = new float2(1f, 1f);
        _scrollHandle.OffsetMin.Value = new float2(2f, -60f);
        _scrollHandle.OffsetMax.Value = new float2(-2f, 0f);
        ApplyRoundedPanel(handleSlot, AccentColor, color.Transparent);

        // The handle itself is the draggable element (classic scrollbar feel); the
        // viewport's ScrollRect already handles drag-on-content and the mouse wheel.
        var interaction = handleSlot.AttachComponent<InteractionElement>();
        interaction.Pressed += OnHandlePress;
        interaction.Dragged += OnHandleDrag;
    }

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
        // Ignore drags before the viewport is laid out - the 100px default would compute a bogus,
        // huge scroll range and fling the form off-screen. -xlinka
        if (viewportHeight <= LaidOutViewportFloor || contentHeight <= 0f)
            return;
        float maxScroll = MathF.Max(0f, contentHeight - viewportHeight);
        if (maxScroll <= 0f)
            return;
        float handleHeight = MathF.Max(30f, viewportHeight * (viewportHeight / contentHeight));
        float travel = viewportHeight - handleHeight;
        if (travel <= 0f)
            return;
        // Dragging the handle down (local Y decreases) scrolls the content down.
        float deltaY = context.LocalPoint.y - _handlePressY;
        float scrolled = _handlePressScroll - deltaY * (maxScroll / travel);
        _scroll.AbsolutePosition = new float2(0f, Clamp(scrolled, 0f, maxScroll));
        UpdateScrollHandle();
        _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    private void UpdateScrollHandle()
    {
        if (_scroll == null || _scrollTrack == null || _scrollHandle == null
            || _viewportRect == null || _contentRect == null)
            return;
        float viewportHeight = _viewportRect.LocalComputeRect.height;
        if (viewportHeight <= LaidOutViewportFloor)
        {
            // Layout hasn't produced the real viewport rect yet (still the 100px default). Retrying
            // next frame, bounded so a genuinely tiny/broken panel can't churn forever.
            if (_scrollHandleRetries++ < 30)
                World?.RunInUpdates(1, UpdateScrollHandle);
            return;
        }
        _scrollHandleRetries = 0;

        // The ContentSizeFitter owns the content height, and ScrollRect.ApplyScroll clamps the scroll to
        // that same rect - so reading it here keeps the handle, the scroll clamp, and the real content in
        // perfect agreement (one source of truth, no racing pin). -xlinka
        float contentHeight = _contentRect.LocalComputeRect.height;
        if (contentHeight <= 0f)
        {
            if (_scrollHandleRetries++ < 30)
                World?.RunInUpdates(1, UpdateScrollHandle);
            return;
        }

        float maxScroll = MathF.Max(0f, contentHeight - viewportHeight);
        if (maxScroll <= 0.5f)
        {
            _scrollTrack.ActiveSelf.Value = false; // everything fits; no scrollbar
            if (_scroll.NormalizedPosition.y != 0f)
                _scroll.NormalizedPosition = float2.Zero; // drop any stale scroll so the form sits at the top
            return;
        }
        _scrollTrack.ActiveSelf.Value = true;
        // Clamp any existing scroll to the (possibly reduced) real range so it can't rest past the bottom.
        if (_scroll.AbsolutePosition.y > maxScroll)
            _scroll.AbsolutePosition = new float2(0f, maxScroll);
        float handleHeight = MathF.Max(30f, viewportHeight * (viewportHeight / contentHeight));
        float fraction = Clamp(_scroll.AbsolutePosition.y / maxScroll, 0f, 1f);
        float offset = fraction * (viewportHeight - handleHeight);
        _scrollHandle.OffsetMax.Value = new float2(-2f, -offset);
        _scrollHandle.OffsetMin.Value = new float2(2f, -(offset + handleHeight));
    }

    // Size each expanded section body to its rows (collapsed bodies are hidden), then
    // size the scroll content to the taller column so ScrollRect knows the range.
    private void RecomputeContentHeight()
    {
        foreach (var section in _sections)
        {
            var bodyElement = section.Body.GetComponent<LayoutElement>();
            if (bodyElement == null)
                continue;
            float height = section.Expanded ? MeasureStack(section.Body) : 0f;
            bodyElement.MinHeight.Value = height;
            bodyElement.PreferredHeight.Value = height;
        }

        // Pin the content height to the taller column's measured stack so ScrollRect knows the range.
        // (Explicit pin instead of a ContentSizeFitter - the fitter froze the canvas; see RebuildSettings.)
        float total = MathF.Max(MeasureStack(_leftColumn), MeasureStack(_rightColumn)) + 6f;
        if (_contentRect != null)
        {
            _contentRect.OffsetMin.Value = new float2(0f, -total);
            _contentRect.OffsetMax.Value = float2.Zero;
        }
    }

    // Sum of the active children's fixed heights plus the layout's spacing/padding.
    private static float MeasureStack(Slot? container)
    {
        if (container == null)
            return 0f;
        var layout = container.GetComponent<VerticalLayout>();
        float spacing = layout?.Spacing.Value ?? 0f;
        float total = (layout?.PaddingTop.Value ?? 0f) + (layout?.PaddingBottom.Value ?? 0f);
        int count = 0;
        foreach (var child in container.Children)
        {
            if (!child.ActiveSelf.Value)
                continue;
            var element = child.GetComponent<LayoutElement>();
            if (element == null)
                continue;
            total += element.PreferredHeight.Value;
            count++;
        }
        if (count > 1)
            total += spacing * (count - 1);
        return total;
    }

    private static float Clamp(float value, float min, float max)
        => value < min ? min : (value > max ? max : value);

    // A row (with the same pill as the others, for consistency) with the label on the left
    // and a radio on the right; radios sharing a group are mutually exclusive.
    private void RadioRow(Slot parent, string group, string label, bool isChecked, Action onSelect)
    {
        var row = parent.AddSlot(label);
        row.AttachComponent<RectTransform>();
        row.AttachComponent<GraphicChunkRoot>();
        SetFixedHeight(row, 28f);
        ApplyRoundedPanel(row, RowFill, RowBorder);
        var h = row.AttachComponent<HorizontalLayout>();
        h.Spacing.Value = 10f;
        h.PaddingLeft.Value = 12f;
        h.PaddingRight.Value = 12f;
        h.ForceExpandWidth.Value = false;
        h.ForceExpandHeight.Value = true;

        var b = RowBuilder(row);
        b.MinWidth(160f).FlexibleWidth(1f);
        AddRowLabel(b, label, 15f, TextPrimary, TextHorizontalAlignment.Left);
        b.MinWidth(26f).PreferredWidth(26f).FlexibleWidth(0f);
        b.Radio(group, isChecked, (_, on) => { if (on) onSelect(); });
    }

    private void SaveButtonRow(Slot parent, string label, color fill, Action onClick)
    {
        var row = parent.AddSlot(label);
        row.AttachComponent<RectTransform>();
        row.AttachComponent<GraphicChunkRoot>();
        SetFixedHeight(row, 34f);
        ApplyRoundedPanel(row, fill, RowBorder);
        var button = row.AttachComponent<Button>();
        button.Clicked += (_, _) => onClick();
        AddFillLabel(row, label, 16f, TextPrimary);
    }

    private static string PrettyAccess(World.WorldAccessLevel level) => level switch
    {
        World.WorldAccessLevel.Private => "Private (invite only)",
        World.WorldAccessLevel.LAN => "LAN (local network)",
        World.WorldAccessLevel.Contacts => "Contacts",
        World.WorldAccessLevel.ContactsPlus => "Contacts+",
        World.WorldAccessLevel.Anyone => "Anyone (public)",
        World.WorldAccessLevel.GroupMembers => "Group Members",
        World.WorldAccessLevel.GroupPlus => "Group+",
        World.WorldAccessLevel.GroupPublic => "Group Public",
        World.WorldAccessLevel.RegisteredUsers => "Registered Users",
        _ => level.ToString(),
    };

    private void SaveFocusedWorld(World world)
    {
        // Only the local home has a known save location for now.
        if (world.Name == "LocalHome")
            WorldStorage.SaveToFile(world, Lumora.Core.Engine.LocalHomeSavePath);
    }

    // "Save As" / "Save Copy" write an independent timestamped file. With no record
    // system yet, both behave the same; only the local home has a save location.
    private void SaveWorldCopy(World world)
    {
        if (world.Name != "LocalHome")
            return;
        var directory = System.IO.Path.GetDirectoryName(Lumora.Core.Engine.LocalHomeSavePath) ?? ".";
        var name = $"home_{DateTime.Now:yyyyMMdd_HHmmss}.lworld";
        // Saved worlds (unlike the local home) are encrypted at rest.
        WorldStorage.SaveToFile(world, System.IO.Path.Combine(directory, name), encrypt: true);
    }

    // USERS PAGE - live list of session users; the host (authority) can change roles and kick.

    private void BuildUsersPage(Slot page)
    {
        _usersPage = page;
        RebuildUsersList();
    }

    private void RebuildUsersList()
    {
        var page = _usersPage;
        if (page == null)
            return;
        page.DestroyChildren();

        var world = FocusedWorld;
        if (world == null)
        {
            AddRow(page, "No focused world.");
            return;
        }

        var users = world.GetAllUsers();

        var header = BeginRow(page, "UsersHeader");
        var hb = RowBuilder(header);
        hb.MinWidth(200f).FlexibleWidth(1f);
        AddRowLabel(hb, $"{WorldDisplayName(world)} — users ({users.Count})", 16f, SectionTitleColor, TextHorizontalAlignment.Left);
        AddInlineButton(header, "Refresh", TabFill, RebuildUsersList);

        foreach (var user in users)
            UserRow(page, world, user);

        // Make the empty single-user case self-explanatory: there's nobody to moderate.
        int moderatable = 0;
        foreach (var user in users)
            if (!user.IsLocal && world.DataModelPermissions.GetRole(user) != world.DataModelPermissions.HostRole)
                moderatable++;
        if (world.IsAuthority && moderatable == 0)
            AddRow(page, "You're the host. Mute / Silence / Kick / Ban appear next to other users when they join.");

        _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    private void UserRow(Slot page, World world, User user)
    {
        var permissions = world.DataModelPermissions;
        var role = permissions.GetRole(user);
        bool isHostUser = role == permissions.HostRole;

        var row = BeginRow(page, "User");
        var b = RowBuilder(row);

        var name = user.UserName?.Value;
        if (string.IsNullOrEmpty(name))
            name = $"User {user.ReferenceID}";
        if (user.IsLocal)
            name += "  (You)";

        b.MinWidth(150f).FlexibleWidth(1f);
        AddRowLabel(b, name, 16f, TextPrimary, TextHorizontalAlignment.Left);

        // Host moderation tools for other (non-host) users; everyone else sees the role read-only.
        if (world.IsAuthority && !user.IsLocal && !isHostUser)
        {
            AddInlineButton(row, role.Name, TabFill, () =>
            {
                permissions.SetUserRole(user, NextAssignableRole(WorldModePermissions.AssignableRoles(permissions, world.Mode), role));
                RebuildUsersList();
            });
            AddInlineButton(row, user.IsMuted.Value ? "Muted" : "Mute", user.IsMuted.Value ? AccentColor : TabFill, () =>
            {
                user.IsMuted.Value = !user.IsMuted.Value;
                RebuildUsersList();
            });
            AddInlineButton(row, user.IsSilenced.Value ? "Silenced" : "Silence", user.IsSilenced.Value ? AccentColor : TabFill, () =>
            {
                user.IsSilenced.Value = !user.IsSilenced.Value;
                RebuildUsersList();
            });
            AddInlineButton(row, "Respawn", TabFill, () => RequestUserRespawn(world, user));
            AddInlineButton(row, "Kick", KickFill, () =>
            {
                user.Kick();
                RebuildUsersList();
            });
            AddInlineButton(row, "Ban", KickFill, () =>
            {
                user.Ban();
                RebuildUsersList();
            });
        }
        else
        {
            b.MinWidth(70f).PreferredWidth(70f).FlexibleWidth(0f);
            AddRowLabel(b, role.Name, 15f, isHostUser ? AccentColor : TextDim, TextHorizontalAlignment.Right);
            // You can always respawn yourself (e.g. if stuck in geometry) - works solo too.
            if (user.IsLocal)
                AddInlineButton(row, "Respawn", TabFill, () => RequestUserRespawn(world, user));
        }
    }

    private static DataModelPermissionRole NextAssignableRole(System.Collections.Generic.IReadOnlyList<DataModelPermissionRole> roles, DataModelPermissionRole current)
    {
        int index = -1;
        for (int i = 0; i < roles.Count; i++)
        {
            if (roles[i] == current)
            {
                index = i;
                break;
            }
        }
        return roles[(index + 1) % roles.Count];
    }

    private static void RequestUserRespawn(World world, User user)
    {
        // Reach the target's character via their UserRoot and teleport it back to spawn. Works where
        // the host has authority over the character transform; logs if the character isn't reachable.
        var character = user.Root?.GetRegisteredComponent<Lumora.Core.Components.CharacterController>();
        if (character == null)
        {
            Lumora.Core.Logging.Logger.Log($"[Session] Respawn: no reachable character for '{user.UserName?.Value}'.");
            return;
        }
        character.Teleport(new float3(0f, 1f, 0f));
        _ = world;
    }

    // 70px-wide inline button; delegates to the shared width-parameterized builder.
    private void AddInlineButton(Slot row, string label, color fill, Action onClick)
        => AddInlineButton(row, label, fill, 70f, onClick);

    // PERMISSIONS PAGE - default role per access class (a defaults grid) plus
    // per-user overrides. Roles are presets over the HARD gate; host-authoritative, deny-at-source.

    private void BuildPermissionsPage(Slot page)
    {
        _permissionsPage = page;
        RebuildPermissions();
    }

    private void RebuildPermissions()
    {
        var page = _permissionsPage;
        if (page == null)
            return;
        page.DestroyChildren();

        var world = FocusedWorld;
        if (world == null)
        {
            AddRow(page, "No focused world.");
            return;
        }
        var permissions = world.DataModelPermissions;

        AddRow(page, world.IsAuthority
            ? $"{WorldDisplayName(world)} — default role per user class (host-authoritative, denied at source)."
            : $"{WorldDisplayName(world)} — permissions are controlled by the host.");

        // In Social/Event worlds the authored world is frozen for everyone (incl. host); only the
        // Moderator / User / Spectator roles apply, and editing the world is locked regardless of role.
        if (world.Mode != WorldMode.Builder)
            AddRow(page, $"This is a {world.Mode} world — world editing is locked; roles cover moderation + your own items only.");

        foreach (DataModelAccessClass accessClass in Enum.GetValues<DataModelAccessClass>())
            DefaultRoleRow(page, world, permissions, accessClass);

        var overrides = BeginRow(page, "Overrides");
        var b = RowBuilder(overrides);
        b.MinWidth(200f).FlexibleWidth(1f);
        AddRowLabel(b, $"Per-User Overrides: {permissions.UserOverrideCount}", 15f, TextDim, TextHorizontalAlignment.Left);
        if (world.IsAuthority && permissions.UserOverrideCount > 0)
        {
            AddInlineButton(overrides, "Clear", KickFill, () =>
            {
                permissions.ClearUserOverrides();
                RebuildPermissions();
                RebuildUsersList();
            });
        }

        _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    private void DefaultRoleRow(Slot page, World world, DataModelPermissionController permissions, DataModelAccessClass accessClass)
    {
        var row = BeginRow(page, "Default" + accessClass);
        var b = RowBuilder(row);
        b.MinWidth(150f).PreferredWidth(150f).FlexibleWidth(0f);
        AddRowLabel(b, $"Default {accessClass}", 15f, TextPrimary, TextHorizontalAlignment.Left);

        var current = permissions.GetDefaultRole(accessClass);
        foreach (var role in WorldModePermissions.AssignableRoles(permissions, world.Mode))
        {
            var captured = role;
            AddRoleButton(row, role.Name, role == current, world.IsAuthority, () =>
            {
                permissions.SetDefaultRole(accessClass, captured);
                RebuildPermissions();
            });
        }
    }

    // A role cell in the defaults grid; highlighted when it's the selected role for that row.
    private void AddRoleButton(Slot row, string label, bool selected, bool interactive, Action onClick)
    {
        var cell = row.AddSlot(label);
        cell.AttachComponent<RectTransform>();
        var element = cell.AttachComponent<LayoutElement>();
        element.MinWidth.Value = 92f;
        element.PreferredWidth.Value = 92f;
        element.FlexibleWidth.Value = 0f;
        element.FlexibleHeight.Value = 1f;
        ApplyRoundedPanel(cell, selected ? AccentColor : TabFill, RowBorder);
        if (interactive)
        {
            var button = cell.AttachComponent<Button>();
            button.Clicked += (_, _) => onClick();
        }
        AddFillLabel(cell, label, 13f, selected ? TextPrimary : TextDim);
    }

    // ROW / WIDGET HELPERS

    private void AddRow(Slot page, string text)
    {
        var row = BeginRow(page, "Info");
        var label = AddFillLabel(row, text, 16f, TextDim);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }

    private void SliderRow(Slot page, string label, float min, float max, float value, Func<float, string> applyAndFormat)
    {
        var row = BeginRow(page, label);
        var b = RowBuilder(row);

        b.MinWidth(178f).PreferredWidth(178f).FlexibleWidth(0f);
        AddRowLabel(b, label, 15f, TextPrimary, TextHorizontalAlignment.Left);

        Text? valueText = null;
        var slider = b.Slider(value, min, max, (_, v) =>
        {
            var formatted = applyAndFormat(v);
            if (valueText != null && !valueText.IsDestroyed)
                valueText.Content.Value = formatted;
        });
        var sliderLayout = slider.Slot.GetComponent<LayoutElement>() ?? slider.Slot.AttachComponent<LayoutElement>();
        sliderLayout.MinWidth.Value = 120f;
        sliderLayout.PreferredWidth.Value = 240f;
        sliderLayout.FlexibleWidth.Value = 1f;

        b.MinWidth(80f).PreferredWidth(80f).FlexibleWidth(0f);
        valueText = AddRowLabel(b, applyAndFormat(value), 15f, TextDim, TextHorizontalAlignment.Right);
    }

    private void ToggleRow(Slot page, string label, bool value, Action<bool> apply)
    {
        var row = BeginRow(page, label);
        var b = RowBuilder(row);

        b.MinWidth(178f).PreferredWidth(178f).FlexibleWidth(0f);
        AddRowLabel(b, label, 15f, TextPrimary, TextHorizontalAlignment.Left);

        Text? stateText = null;
        b.MinWidth(28f).PreferredWidth(28f).FlexibleWidth(0f);
        b.Checkbox(value, (_, isChecked) =>
        {
            apply(isChecked);
            if (stateText != null && !stateText.IsDestroyed)
                stateText.Content.Value = isChecked ? "On" : "Off";
        });

        b.MinWidth(80f).FlexibleWidth(1f);
        stateText = AddRowLabel(b, value ? "On" : "Off", 15f, TextDim, TextHorizontalAlignment.Left);
    }

}
