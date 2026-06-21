// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Components.Network;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Dashboard "Worlds" screen: a world browser laid out as a grid of thumbnail cards with a category
/// sidebar, instead of a flat list. Lists the worlds you have open and the
/// sessions discovered on the local network; each card is mode-tinted and tagged Build / Social / Event,
/// and clicking a card enters/joins it.
/// </summary>
public sealed class WorldsScreen : WidgetScreen
{
    private static readonly color CardFill = new color(0.17f, 0.16f, 0.26f, 0.95f);
    private static readonly color ThumbFill = new color(0.10f, 0.10f, 0.16f, 1f);
    private static readonly color SidebarFill = new color(0.22f, 0.20f, 0.34f, 0.6f);
    private static readonly color BuilderTag = new color(0.30f, 0.52f, 0.78f, 0.95f);
    private static readonly color SocialTag = new color(0.28f, 0.62f, 0.42f, 0.95f);
    private static readonly color EventTag = new color(0.55f, 0.40f, 0.75f, 0.95f);
    private static readonly color SavedTag = new color(0.45f, 0.45f, 0.52f, 0.95f);

    private const float CardSpacing = 8f;
    private static readonly float2 CardCell = new float2(168f, 186f);

    private static readonly string[] Categories = { "All", "Open", "LAN", "My Worlds", "Build", "Social", "Event" };

    private string _filter = "All";
    private Slot? _root;

    protected override void OnShow()
    {
        base.OnShow();
        GetBrowser();   // keep LAN discovery running while the browser is up
        Rebuild();
    }

    protected override void BuildContent(UIBuilder builder)
    {
        ResolveDashboard();
        _root = builder.Current;
        Rebuild();
    }

    private void Rebuild()
    {
        var root = _root;
        if (root == null)
            return;
        root.DestroyChildren();

        // The outer layout lives on _root (which survives DestroyChildren), so reuse it across rebuilds.
        var outer = root.GetComponent<VerticalLayout>() ?? root.AttachComponent<VerticalLayout>();
        outer.Spacing.Value = 8f;
        outer.PaddingLeft.Value = 14f;
        outer.PaddingRight.Value = 14f;
        outer.PaddingTop.Value = 14f;
        outer.PaddingBottom.Value = 14f;
        outer.ForceExpandWidth.Value = true;
        outer.ForceExpandHeight.Value = false;

        // Header.
        var header = BeginRow(root, "Header");
        var hb = RowBuilder(header);
        hb.MinWidth(160f).FlexibleWidth(1f);
        AddRowLabel(hb, "Worlds", 18f, SectionTitleColor, TextHorizontalAlignment.Left);
        AddInlineButton(header, "Refresh", TabFill, 84f, Rebuild);

        // Body: category sidebar + card grid.
        var body = root.AddSlot("Body");
        body.AttachComponent<RectTransform>();
        var bodyElement = body.AttachComponent<LayoutElement>();
        bodyElement.FlexibleWidth.Value = 1f;
        bodyElement.FlexibleHeight.Value = 1f;
        var bodyLayout = body.AttachComponent<HorizontalLayout>();
        bodyLayout.Spacing.Value = 10f;
        bodyLayout.ForceExpandWidth.Value = false;
        bodyLayout.ForceExpandHeight.Value = true;

        BuildSidebar(body);

        var cards = CollectCards();
        if (cards.Count == 0)
            BuildEmpty(body);
        else
            BuildGrid(body, cards);

        MarkDirty();
    }

    // SIDEBAR

    private void BuildSidebar(Slot body)
    {
        var sidebar = body.AddSlot("Sidebar");
        sidebar.AttachComponent<RectTransform>();
        var element = sidebar.AttachComponent<LayoutElement>();
        element.MinWidth.Value = 116f;
        element.PreferredWidth.Value = 116f;
        element.FlexibleWidth.Value = 0f;
        element.FlexibleHeight.Value = 1f;
        var layout = sidebar.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = 5f;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;

        foreach (var category in Categories)
        {
            var captured = category;
            var cell = sidebar.AddSlot(category);
            cell.AttachComponent<RectTransform>();
            var cellElement = cell.AttachComponent<LayoutElement>();
            cellElement.MinHeight.Value = 32f;
            cellElement.PreferredHeight.Value = 32f;
            cellElement.FlexibleHeight.Value = 0f;
            cellElement.FlexibleWidth.Value = 1f;
            ApplyRoundedPanel(cell, category == _filter ? AccentColor : SidebarFill, RowBorder);
            cell.AttachComponent<Button>().Clicked += (_, _) =>
            {
                _filter = captured;
                Rebuild();
            };
            AddFillLabel(cell, category, 14f, TextPrimary);
        }
    }

    // GRID

    private void BuildGrid(Slot body, List<CardData> cards)
    {
        var grid = body.AddSlot("Grid");
        grid.AttachComponent<RectTransform>();
        var gridElement = grid.AttachComponent<LayoutElement>();
        gridElement.FlexibleWidth.Value = 1f;
        gridElement.FlexibleHeight.Value = 1f;
        // Fixed-size cells that wrap to fill the width (don't stretch with the panel).
        var gl = grid.AttachComponent<GridLayout>();
        gl.CellSize.Value = CardCell;
        gl.Spacing.Value = CardSpacing;

        foreach (var card in cards)
            BuildCard(grid, card);
    }

    private void BuildCard(Slot grid, CardData card)
    {
        var cell = grid.AddSlot("Card");
        cell.AttachComponent<RectTransform>();
        ApplyRoundedPanel(cell, CardFill, RowBorder);
        cell.AttachComponent<Button>().Clicked += (_, _) => card.Activate();

        var layout = cell.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = 4f;
        layout.PaddingLeft.Value = 6f;
        layout.PaddingRight.Value = 6f;
        layout.PaddingTop.Value = 6f;
        layout.PaddingBottom.Value = 6f;
        layout.ForceExpandWidth.Value = true;
        layout.ForceExpandHeight.Value = false;

        // Image placeholder (neutral) filling the top - real world thumbnails go here later.
        var thumb = cell.AddSlot("Thumb");
        thumb.AttachComponent<RectTransform>();
        var thumbElement = thumb.AttachComponent<LayoutElement>();
        thumbElement.FlexibleWidth.Value = 1f;
        thumbElement.FlexibleHeight.Value = 1f;
        thumbElement.MinHeight.Value = 96f;
        ApplyRoundedPanel(thumb, ThumbFill, RowBorder);

        // Name.
        AddCardLine(cell, card.Title, 14f, TextPrimary, 20f);

        // Bottom: type pill + subtitle (host / users).
        var bottom = cell.AddSlot("Bottom");
        bottom.AttachComponent<RectTransform>();
        var bottomElement = bottom.AttachComponent<LayoutElement>();
        bottomElement.MinHeight.Value = 22f;
        bottomElement.PreferredHeight.Value = 22f;
        bottomElement.FlexibleHeight.Value = 0f;
        bottomElement.FlexibleWidth.Value = 1f;
        var bottomLayout = bottom.AttachComponent<HorizontalLayout>();
        bottomLayout.Spacing.Value = 5f;
        bottomLayout.ForceExpandWidth.Value = false;
        bottomLayout.ForceExpandHeight.Value = true;

        var pill = bottom.AddSlot("Pill");
        pill.AttachComponent<RectTransform>();
        var pillElement = pill.AttachComponent<LayoutElement>();
        pillElement.MinWidth.Value = 56f;
        pillElement.PreferredWidth.Value = 56f;
        pillElement.FlexibleWidth.Value = 0f;
        pillElement.FlexibleHeight.Value = 1f;
        ApplyRoundedPanel(pill, card.PillText != null ? card.PillColor : TagColor(card.Mode), RowBorder);
        AddFillLabel(pill, card.PillText ?? ModeLabel(card.Mode), 11f, TextPrimary);

        var sub = bottom.AddSlot("Sub");
        sub.AttachComponent<RectTransform>();
        var subElement = sub.AttachComponent<LayoutElement>();
        subElement.FlexibleWidth.Value = 1f;
        subElement.FlexibleHeight.Value = 1f;
        subElement.MinWidth.Value = 40f;
        var subLabel = AddFillLabel(sub, card.Subtitle, 11f, TextDim);
        subLabel.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }

    private void AddCardLine(Slot card, string text, float size, color textColor, float height)
    {
        var line = card.AddSlot("Line");
        line.AttachComponent<RectTransform>();
        var element = line.AttachComponent<LayoutElement>();
        element.MinHeight.Value = height;
        element.PreferredHeight.Value = height;
        element.FlexibleHeight.Value = 0f;
        element.FlexibleWidth.Value = 1f;
        var label = AddFillLabel(line, text, size, textColor);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }

    private void BuildEmpty(Slot body)
    {
        var wrap = body.AddSlot("Empty");
        wrap.AttachComponent<RectTransform>();
        var element = wrap.AttachComponent<LayoutElement>();
        element.FlexibleWidth.Value = 1f;
        element.MinHeight.Value = 60f;
        element.PreferredHeight.Value = 60f;
        var browser = GetBrowser();
        var text = _filter switch
        {
            "Open" => "No open worlds. Create one from Home → Create New World.",
            "LAN" => browser?.IsScanning.Value == true ? "Scanning for sessions…" : "No sessions found.",
            "My Worlds" => "No saved worlds yet. Save one from Session → World Save Options.",
            _ => "No worlds here yet.",
        };
        var label = AddFillLabel(wrap, text, 15f, TextDim);
        label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
    }

    // DATA

    private readonly struct CardData
    {
        public readonly string Title;
        public readonly string Subtitle;
        public readonly WorldMode Mode;
        public readonly Action Activate;
        public readonly string? PillText;   // null = use the mode label/color
        public readonly color PillColor;

        public CardData(string title, string subtitle, WorldMode mode, Action activate, string? pillText = null, color pillColor = default)
        {
            Title = title;
            Subtitle = subtitle;
            Mode = mode;
            Activate = activate;
            PillText = pillText;
            PillColor = pillColor;
        }
    }

    private List<CardData> CollectCards()
    {
        var cards = new List<CardData>();
        var manager = Lumora.Core.Engine.Current?.WorldManager;
        if (manager == null)
            return cards;

        bool wantOpen = _filter == "All" || _filter == "Open" || IsModeFilter(_filter);
        bool wantLan = _filter == "All" || _filter == "LAN" || IsModeFilter(_filter);

        if (wantOpen)
        {
            var userspace = manager.UserspaceWorld;
            var focused = manager.FocusedWorld;
            foreach (var world in manager.Worlds)
            {
                if (world == null || ReferenceEquals(world, userspace))
                    continue;
                if (!MatchesModeFilter(world.Mode))
                    continue;

                var name = world.WorldName?.Value;
                if (string.IsNullOrEmpty(name))
                    name = world.Name;
                int users = world.GetAllUsers().Count;
                bool isFocused = ReferenceEquals(world, focused);
                var captured = world;
                cards.Add(new CardData(
                    name,
                    isFocused ? $"{Users(users)} · here" : Users(users),
                    world.Mode,
                    () => { manager.SwitchToWorld(captured); Rebuild(); }));
            }
        }

        if (wantLan)
        {
            var sessions = GetBrowser()?.GetSessions();
            if (sessions != null)
            {
                foreach (var entry in sessions)
                {
                    if (entry == null)
                        continue;
                    var mode = WorldModePermissions.ParseMode(entry.Tags);
                    if (!MatchesModeFilter(mode))
                        continue;
                    var captured = entry;
                    cards.Add(new CardData(
                        string.IsNullOrEmpty(entry.Name) ? "(unnamed)" : entry.Name,
                        $"{entry.HostUsername ?? "?"} · {entry.ActiveUsers}/{entry.MaxUsers}",
                        mode,
                        () => JoinSession(manager, captured)));
                }
            }
        }

        // My Worlds: saved world files on disk. Mode isn't known until loaded, so they carry a neutral
        // "Saved" pill and are excluded from the mode filters.
        if (_filter is "All" or "My Worlds")
        {
            foreach (var path in SavedWorldFiles())
            {
                var captured = path;
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                string when;
                try { when = System.IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"); }
                catch { when = ""; }
                cards.Add(new CardData(
                    name,
                    string.IsNullOrEmpty(when) ? "Saved world" : when,
                    WorldMode.Builder,
                    () => { manager.OpenSavedWorld(captured); Rebuild(); },
                    pillText: "Saved",
                    pillColor: SavedTag));
            }
        }

        return cards;
    }

    private static IEnumerable<string> SavedWorldFiles()
    {
        var dir = System.IO.Path.GetDirectoryName(Lumora.Core.Engine.LocalHomeSavePath);
        if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir))
            yield break;
        foreach (var file in System.IO.Directory.GetFiles(dir, "*.lworld"))
            yield return file;
    }

    private bool IsModeFilter(string filter) => filter is "Build" or "Social" or "Event";

    private bool MatchesModeFilter(WorldMode mode)
    {
        return _filter switch
        {
            "Build" => mode == WorldMode.Builder,
            "Social" => mode == WorldMode.Social,
            "Event" => mode == WorldMode.Event,
            _ => true,
        };
    }

    private static string Users(int n) => n == 1 ? "1 user" : $"{n} users";

    private static void JoinSession(Management.WorldManager manager, Network.SessionListEntry entry)
    {
        var url = entry.JoinUrl;
        if (url == null)
            return;

        // Load + connect in the BACKGROUND and only focus once the world is actually Running. The sync
        // WorldManager.JoinSession would AddWorld + focus a half-initialized world instantly - that's what
        // dumped the user into a black loading world (mouse/dash dead), and it even reported "success" when
        // the connect failed. The loading service keeps the user in their current world until the join is
        // ready (3D indicator while it loads), and cleanly bails on failure. -xlinka
        var loader = Lumora.Core.Engine.Current?.WorldLoadingService;
        if (loader != null)
        {
            loader.JoinSessionAsync(entry.Name ?? url.Host, url, focusWhenReady: true);
            return;
        }

        // Fallback if the loading service somehow isn't up yet.
        ushort port = url.Port > 0 ? (ushort)url.Port : (ushort)0;
        var world = manager.JoinSession(entry.Name ?? url.Host, url.Host, port);
        if (world != null)
            manager.SwitchToWorld(world);
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

    private static color TagColor(WorldMode mode) => mode switch
    {
        WorldMode.Social => SocialTag,
        WorldMode.Event => EventTag,
        _ => BuilderTag,
    };

    private static string ModeLabel(WorldMode mode) => mode switch
    {
        WorldMode.Social => "Social",
        WorldMode.Event => "Event",
        _ => "Build",
    };
}
