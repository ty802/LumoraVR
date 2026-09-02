// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core;
using Lumora.Core.Components.Network;
using Lumora.Core.Math;
using Lumora.Nexus.Cloud.Cdn;

namespace Lumora.Core.Components.UI;

// Two shapes, one card. With the services unreachable there is nothing to report, so the card takes
// itself down to its title row instead of stacking three lines of zeroes; reachable, it opens up to a
// donut of who is actually in the worlds this client has open, split by the machine they are on, with
// what the directory can see and the round trip to the services underneath.
//
// The card owns its own footprint: it writes GridY/GridHeight on its Widget and keeps its BOTTOM edge
// where it is, so collapsing takes rows off the top rather than leaving a hole above the nav bar. Polled
// once a second, same as before: the counts behind it move on a backend poll, not per frame. -xlinka
[ComponentCategory("Hidden")]
public sealed class DirectoryWidgetPreset : HomeWidgetPreset
{
    private const int CollapsedRows = 3;
    private const int ExpandedRows = 7;

    // Canvas units. The donut square is the outer diameter, so the ring sits exactly inside its band and
    // the sessions line under it clears the bottom of the ring by construction.
    private const float OuterRadius = 40f;
    private const float InnerRadius = 27f;
    private const float DonutSize = OuterRadius * 2f;
    private const float SliceGap = 2f;
    private const float TopAngle = 270f;

    private const float TitleTop = 4f;
    private const float TitleHeight = 18f;
    private const float LineHeight = 13f;
    private const float BodyTop = TitleTop + TitleHeight;
    private const float BodyHeight = DonutSize + LineHeight * 2f;
    private const float LegendLeft = Pad + DonutSize + 10f;
    private const float LegendRowHeight = 16f;
    private const float DotSize = 8f;
    // Right side of the title row: the status word right-aligned in a fixed column, its dot just left of
    // it, and the title given everything up to that.
    private const float StatusWidth = 58f;
    private const float StatusGap = 6f;
    private const float StatusDotLeft = -(StatusWidth + StatusGap + DotSize);
    private const float TitleTextRight = StatusDotLeft - 8f;

    private const string Separator = " · ";

    // Counts are indexed by the Platform enum, which is why the slices and the legend come out Windows,
    // Linux, Android, Other without a lookup table.
    private const int PlatformCount = 4;

    private sealed class LegendRow
    {
        public Slot Slot = null!;
        public RectTransform Rect = null!;
        public RoundedPanel Dot = null!;
        public Text Name = null!;
        public Text Count = null!;
    }

    private readonly int[] _counts = new int[PlatformCount];
    private readonly HashSet<string> _seenUsers = new();
    private readonly ArcSegment?[] _slices = new ArcSegment?[PlatformCount];
    private readonly LegendRow?[] _legend = new LegendRow?[PlatformCount];

    private Widget? _widget;
    private RectTransform? _titleRect;
    private RoundedPanel? _dot;
    private Text? _status;
    private Slot? _body;
    private Text? _total;
    private Text? _sessions;
    private Text? _ping;

    // Null until the first poll applies one, so the card always writes its own footprint once instead of
    // trusting whatever the grid placed it at.
    private bool? _expanded;

    public DirectoryWidgetPreset()
    {
        MinSize.Value = new float2(200f, 44f);
        PreferredSize.Value = new float2(244f, 130f);
        MaxSize.Value = new float2(520f, 260f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        _widget = widget;

        // The title row is the only thing both shapes share, so it moves rather than being built twice:
        // pinned under the top edge while the card is open, centred in the card once it collapses.
        var title = SettingsUI.Child(root, "Title", new float2(0f, 1f), new float2(1f, 1f),
            new float2(Pad, -(TitleTop + TitleHeight)), new float2(-Pad, -TitleTop));
        _titleRect = SettingsUI.Rect(title);

        Label(title, "Text", "Directory", DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, float2.Zero, new float2(1f, 1f),
            float2.Zero, new float2(TitleTextRight, 0f), SemiboldFont);

        var dotSlot = SettingsUI.Child(title, "Dot", new float2(1f, 0.5f), new float2(1f, 0.5f),
            new float2(StatusDotLeft, -DotSize * 0.5f), new float2(StatusDotLeft + DotSize, DotSize * 0.5f));
        _dot = SettingsUI.Panel(dotSlot, DashTheme.TextMuted, color.Transparent, DotSize * 0.5f);

        _status = Label(title, "Status", "Offline", DashTheme.FontSmall, DashTheme.TextDim,
            TextHorizontalAlignment.Right, new float2(1f, 0f), new float2(1f, 1f),
            new float2(-StatusWidth, 0f), float2.Zero);

        // Everything under the title hangs off one slot, so collapsing the card is a single SetActive.
        var body = Row(root, "Body", BodyTop, BodyHeight, 0f, 0f);
        _body = body;

        var donut = SettingsUI.Child(body, "Donut", new float2(0f, 1f), new float2(0f, 1f),
            new float2(Pad, -DonutSize), new float2(Pad + DonutSize, 0f));
        for (int i = 0; i < PlatformCount; i++)
        {
            var sliceSlot = SettingsUI.Child(donut, "Slice" + i, float2.Zero, new float2(1f, 1f),
                float2.Zero, float2.Zero);
            var arc = sliceSlot.AttachComponent<ArcSegment>();
            arc.InnerRadius.Value = InnerRadius;
            arc.OuterRadius.Value = OuterRadius;
            arc.Tint.Value = PlatformColor(i);
            // Flat colour: an outline on a 13 unit band would be most of the band.
            arc.OutlineColor.Value = color.Transparent;
            arc.OutlineThickness.Value = 0f;
            arc.ArcLength.Value = 0f;
            _slices[i] = arc;
            SetActive(sliceSlot, false);
        }

        _total = Label(donut, "Total", "0", DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Center, float2.Zero, new float2(1f, 1f),
            float2.Zero, float2.Zero, BoldFont);

        var legend = SettingsUI.Child(body, "Legend", new float2(0f, 1f), new float2(1f, 1f),
            new float2(LegendLeft, -DonutSize), new float2(-Pad, 0f));
        for (int i = 0; i < PlatformCount; i++)
        {
            var rowSlot = Row(legend, "Row" + i, i * LegendRowHeight, LegendRowHeight, 0f, 0f);
            var entryDot = SettingsUI.Child(rowSlot, "Dot", new float2(0f, 0.5f), new float2(0f, 0.5f),
                new float2(0f, -DotSize * 0.5f), new float2(DotSize, DotSize * 0.5f));
            _legend[i] = new LegendRow
            {
                Slot = rowSlot,
                Rect = SettingsUI.Rect(rowSlot),
                Dot = SettingsUI.Panel(entryDot, DashTheme.TextMuted, color.Transparent, DotSize * 0.5f),
                Name = Label(rowSlot, "Name", string.Empty, DashTheme.FontSmall, DashTheme.TextDim,
                    TextHorizontalAlignment.Left, float2.Zero, new float2(1f, 1f),
                    new float2(DotSize + 6f, 0f), new float2(-26f, 0f)),
                Count = Label(rowSlot, "Count", string.Empty, DashTheme.FontSmall, DashTheme.Text,
                    TextHorizontalAlignment.Right, new float2(1f, 0f), new float2(1f, 1f),
                    new float2(-26f, 0f), float2.Zero),
            };
            SetActive(rowSlot, false);
        }

        _sessions = Label(Row(body, "Sessions", DonutSize, LineHeight, Pad, Pad), "Text", string.Empty,
            DashTheme.FontSmall, DashTheme.TextDim, TextHorizontalAlignment.Left,
            float2.Zero, new float2(1f, 1f), float2.Zero, float2.Zero);

        _ping = Label(Row(body, "Ping", DonutSize + LineHeight, LineHeight, Pad, Pad), "Text", string.Empty,
            DashTheme.FontSmall, DashTheme.TextDim, TextHorizontalAlignment.Left,
            float2.Zero, new float2(1f, 1f), float2.Zero, float2.Zero);

        Poll();
    }

    protected override float PollInterval => 1f;

    protected override void Poll()
    {
        if (_status == null || _status.IsDestroyed)
            return;

        var browser = Browser();
        int internet = 0, users = 0, lan = 0;
        if (browser != null)
        {
            foreach (var entry in browser.GetSessions())
            {
                if (entry == null)
                    continue;
                if (entry.Source == SessionSource.Internet)
                {
                    internet++;
                    users += entry.ActiveUsers;
                }
                else
                {
                    lan++;
                }
            }
        }

        // Unreachable is the health check saying no, or the directory saying no with nothing on the local
        // network to fall back on. No browser at all is the same answer: there is nothing to report.
        bool online = Connectivity.IsOnline && browser != null && (!browser.BackendUnreachable || lan > 0);
        ApplyFootprint(online);

        bool changed = false;
        if (_dot != null && !_dot.IsDestroyed)
            SettingsUI.SetFill(_dot, online ? DashTheme.Positive : DashTheme.Negative);

        string status = online ? "Online" : "Offline";
        if (!string.Equals(_status.Content.Value, status, StringComparison.Ordinal))
        {
            ListingStyle.SetText(_status, status);
            changed = true;
        }

        if (online)
            changed |= WriteBody(internet, users, lan);

        if (changed)
            Repaint();
    }

    private bool WriteBody(int internet, int users, int lan)
    {
        int total = CountUsers();
        bool changed = false;

        float cursor = TopAngle;
        for (int i = 0; i < PlatformCount; i++)
        {
            var arc = _slices[i];
            if (arc == null || arc.IsDestroyed)
                continue;

            int count = _counts[i];
            if (count <= 0)
            {
                SetActive(arc.Slot, false);
                continue;
            }

            float sweep = 360f * count / total;
            // The only platform present is a full ring minus the gap, not a hairline; a share too small
            // to survive the gap still keeps a degree so it cannot vanish outright.
            float start = cursor + SliceGap * 0.5f;
            float length = MathF.Max(sweep - SliceGap, 1f);
            cursor += sweep;

            SetActive(arc.Slot, true);
            if (arc.AngleStart.Value != start)
            {
                arc.AngleStart.Value = start;
                changed = true;
            }
            if (arc.ArcLength.Value != length)
            {
                arc.ArcLength.Value = length;
                changed = true;
            }
        }

        if (_total != null && !_total.IsDestroyed)
        {
            string label = total.ToString();
            if (!string.Equals(_total.Content.Value, label, StringComparison.Ordinal))
            {
                ListingStyle.SetText(_total, label);
                changed = true;
            }
        }

        int shown = 0;
        for (int i = 0; i < PlatformCount; i++)
        {
            if (_counts[i] > 0)
                shown++;
        }

        // The legend block is centred against the donut, so a single platform lines up with the middle of
        // the ring instead of hanging off its top edge.
        float top = (DonutSize - shown * LegendRowHeight) * 0.5f;
        int row = 0;
        for (int i = 0; i < PlatformCount; i++)
        {
            if (_counts[i] <= 0)
                continue;

            var entry = _legend[row];
            row++;
            if (entry == null || entry.Slot.IsDestroyed)
                continue;

            SetActive(entry.Slot, true);
            SettingsUI.SetOffsets(entry.Rect,
                new float2(0f, -(top + row * LegendRowHeight)),
                new float2(0f, -(top + (row - 1) * LegendRowHeight)));
            SettingsUI.SetFill(entry.Dot, PlatformColor(i));

            string name = PlatformName(i);
            string count = _counts[i].ToString();
            if (!string.Equals(entry.Name.Content.Value, name, StringComparison.Ordinal)
                || !string.Equals(entry.Count.Content.Value, count, StringComparison.Ordinal))
            {
                ListingStyle.SetText(entry.Name, name);
                ListingStyle.SetText(entry.Count, count);
                changed = true;
            }
        }
        for (; row < PlatformCount; row++)
        {
            var entry = _legend[row];
            if (entry != null && !entry.Slot.IsDestroyed)
                SetActive(entry.Slot, false);
        }

        changed |= SetLine(_sessions, SessionsLine(internet, users, lan));
        changed |= SetLine(_ping, PingLine());
        return changed;
    }

    private static bool SetLine(Text? text, string value)
    {
        if (text == null || text.IsDestroyed || string.Equals(text.Content.Value, value, StringComparison.Ordinal))
            return false;
        ListingStyle.SetText(text, value);
        return true;
    }

    // Collapsed is 13 x 3, expanded 13 x 7, and the bottom edge does not move: the card takes its rows off
    // the top, so the row it was anchored to stays the row it is anchored to. A grid too short to hold the
    // open card (the header strip) keeps the footprint it placed us at and only the content switches.
    // -xlinka
    private void ApplyFootprint(bool expanded)
    {
        // The layout switch is latched, the footprint write is not: the first poll can run before the
        // Widget exists, and a latch taken then would skip the write for good. The grid writes below
        // are equality-gated, so asking every poll costs nothing. -xlinka
        bool switched = _expanded != expanded;
        _expanded = expanded;

        if (switched)
            SetActive(_body, expanded);

        var rect = _titleRect;
        if (switched && rect != null && !rect.IsDestroyed)
        {
            if (expanded)
            {
                SettingsUI.SetAnchors(rect, new float2(0f, 1f), new float2(1f, 1f));
                SettingsUI.SetOffsets(rect, new float2(Pad, -(TitleTop + TitleHeight)), new float2(-Pad, -TitleTop));
            }
            else
            {
                SettingsUI.SetAnchors(rect, new float2(0f, 0.5f), new float2(1f, 0.5f));
                SettingsUI.SetOffsets(rect, new float2(Pad, -TitleHeight * 0.5f), new float2(-Pad, TitleHeight * 0.5f));
            }
        }

        var widget = _widget != null && !_widget.IsDestroyed ? _widget : Slot.GetComponent<Widget>();
        if (widget != null)
        {
            var grid = Slot.GetComponentInParents<WidgetGrid>();
            int gridRows = grid != null ? grid.GridSize.rows : 0;
            if (grid == null || gridRows <= 0 || gridRows >= ExpandedRows)
            {
                int rows = expanded ? ExpandedRows : CollapsedRows;
                int bottom = widget.GridY.Value + widget.GridHeight.Value;
                int y = bottom - rows;
                if (y < 0)
                    y = 0;
                if (widget.GridHeight.Value != rows)
                    widget.GridHeight.Value = rows;
                if (widget.GridY.Value != y)
                    widget.GridY.Value = y;
            }
        }

        if (switched)
            Repaint();
    }

    // Who is in the worlds this client has open, one entry per person: the directory's remote sessions
    // carry no per-user platform, so the donut can only speak for the worlds we are actually in. The same
    // person standing in two of them is one dot, not two. -xlinka
    private int CountUsers()
    {
        Array.Clear(_counts, 0, _counts.Length);
        _seenUsers.Clear();

        var manager = Engine.Current?.WorldManager;
        if (manager == null)
            return 0;

        var userspace = manager.UserspaceWorld;
        int total = 0;
        foreach (var world in manager.Worlds)
        {
            if (world == null || world.IsDestroyed || ReferenceEquals(world, userspace))
                continue;
            foreach (var user in world.GetAllUsers())
            {
                if (user == null)
                    continue;
                string id = user.UserID?.Value ?? string.Empty;
                if (id.Length > 0 && !_seenUsers.Add(id))
                    continue;
                int index = (int)user.UserPlatform.Value;
                if (index < 0 || index >= PlatformCount)
                    index = (int)Platform.Other;
                _counts[index]++;
                total++;
            }
        }
        return total;
    }

    private static string SessionsLine(int internet, int users, int lan)
    {
        string line = string.Empty;
        if (internet > 0)
            line = internet + " internet " + Plural(internet, "session");
        if (users > 0)
            line = Append(line, users + " " + Plural(users, "user"));
        if (lan > 0)
            line = Append(line, lan + " on LAN");
        return line.Length == 0 ? "no sessions found" : line;
    }

    private static string PingLine()
    {
        int ms = Connectivity.LastLatencyMs;
        return ms < 0 ? "ping to services: waiting" : "ping to services: " + ms + " ms";
    }

    private static string Append(string line, string part)
        => line.Length == 0 ? part : line + Separator + part;

    private static string Plural(int count, string word) => count == 1 ? word : word + "s";

    private static string PlatformName(int index) => ((Platform)index) switch
    {
        Platform.Windows => "Windows",
        Platform.Linux => "Linux",
        Platform.Android => "Android",
        _ => "Other",
    };

    private static color PlatformColor(int index) => ((Platform)index) switch
    {
        Platform.Windows => DashTheme.PlatformWindows,
        Platform.Linux => DashTheme.PlatformLinux,
        Platform.Android => DashTheme.PlatformAndroid,
        _ => DashTheme.TextMuted,
    };

    // Same browser the world browser lists from: on the userspace root, attached on first use, and
    // StartScanning is idempotent.
    private static SessionBrowser? Browser()
    {
        var root = Engine.Current?.WorldManager?.UserspaceWorld?.RootSlot;
        if (root == null)
            return null;
        var browser = root.GetComponent<SessionBrowser>() ?? root.AttachComponent<SessionBrowser>();
        browser.StartScanning();
        return browser;
    }
}
