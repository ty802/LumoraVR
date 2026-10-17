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

// Who is online, at a glance: how many clients are running on the services this client talks to, on the title
// row, and under it one icon per kind of machine with how many are on it. The numbers are the services' own,
// brought back by this client's presence report (OnlinePresence). Services that keep no count say so, and
// unreachable ones read Offline, and either way the card is one quiet line.
//
// The card owns its own footprint: it writes GridY/GridHeight on its Widget and keeps its BOTTOM edge where it
// is, so going quiet takes rows off the top rather than leaving a hole above it. Polled once a second, same
// as before: the counts behind it move on a backend poll, not per frame. -xlinka
[ComponentCategory("Hidden")]
public sealed class DirectoryWidgetPreset : HomeWidgetPreset
{
    // The two heights the card wants, in canvas units; the rows they take come from the grid it is on.
    private const float CollapsedHeight = 36f;
    private const float ExpandedHeight = 62f;

    private const float TitleTop = 6f;
    private const float RowHeight = 20f;
    private const float PlatformTop = TitleTop + RowHeight + 4f;
    private const float TotalWidth = 64f;
    private const float Glyph = 16f;
    private const float ChipWidth = 56f;

    // Counts are indexed by the Platform enum, so the icons come out in its order without a lookup table.
    private const int PlatformCount = 4;
    private static readonly string[] Icons = { "panes", "prompt", "phone", "more" };

    private sealed class PlatformChip
    {
        public Slot Slot = null!;
        public RectTransform Rect = null!;
        public Text Count = null!;
    }

    private readonly int[] _counts = new int[PlatformCount];
    private readonly PlatformChip?[] _chips = new PlatformChip?[PlatformCount];

    private RectTransform? _titleRect;
    private Text? _title;
    private Text? _total;
    private Slot? _platforms;

    // Null until the first poll applies one, so the card always writes its own footprint once instead of
    // trusting whatever the grid placed it at.
    private bool? _expanded;

    // What the card shows, for checks: the count on the title row, or -1 while it says Offline.
    public int Shown { get; private set; } = -1;
    public int ShownOn(Platform platform)
    {
        int index = (int)platform;
        return index >= 0 && index < PlatformCount && _chips[index] is { } chip && chip.Slot.ActiveSelf.Value ? _counts[index] : 0;
    }

    public DirectoryWidgetPreset()
    {
        MinSize.Value = new float2(180f, 34f);
        PreferredSize.Value = new float2(300f, 62f);
        MaxSize.Value = new float2(520f, 120f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        // The title row is the only thing both shapes share, so it moves rather than being built twice:
        // pinned under the top edge while the card is open, centred in the card once it goes quiet.
        var title = SettingsUI.Child(root, "Title", new float2(0f, 1f), new float2(1f, 1f),
            new float2(Pad, -(TitleTop + RowHeight)), new float2(-Pad, -TitleTop));
        _titleRect = SettingsUI.Rect(title);
        _title = Label(title, "Text", "Users online", DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, float2.Zero, new float2(1f, 1f),
            float2.Zero, new float2(-TotalWidth, 0f), SemiboldFont);
        _total = Label(title, "Total", string.Empty, DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Right, new float2(1f, 0f), new float2(1f, 1f),
            new float2(-TotalWidth, 0f), float2.Zero, BoldFont);

        _platforms = SettingsUI.Child(root, "Platforms", new float2(0f, 1f), new float2(1f, 1f),
            new float2(Pad, -(PlatformTop + RowHeight)), new float2(-Pad, -PlatformTop));
        for (int i = 0; i < PlatformCount; i++)
        {
            var slot = SettingsUI.Child(_platforms, "Platform" + i, new float2(0f, 0.5f), new float2(0f, 0.5f),
                new float2(0f, -RowHeight * 0.5f), new float2(ChipWidth, RowHeight * 0.5f));
            var glyphSlot = SettingsUI.Child(slot, "Glyph", new float2(0f, 0.5f), new float2(0f, 0.5f),
                new float2(0f, -Glyph * 0.5f), new float2(Glyph, Glyph * 0.5f));
            var picture = glyphSlot.AttachComponent<Lumora.Core.Assets.ImageProvider>();
            picture.GenerateMipmaps.Value = false;
            picture.URL.Value = new Uri("res://Assets/Textures/Icons/" + Icons[i] + ".svg");
            var glyph = glyphSlot.AttachComponent<Image>();
            glyph.Texture.Target = picture;
            glyph.PreserveAspect.Value = true;
            glyph.InteractionTarget.Value = false;
            glyph.Tint.Value = PlatformColor(i);
            var count = Label(slot, "Count", "0", DashTheme.FontSmall, DashTheme.Text, TextHorizontalAlignment.Left,
                float2.Zero, new float2(1f, 1f), new float2(Glyph + 5f, 0f), float2.Zero, SemiboldFont);
            _chips[i] = new PlatformChip { Slot = slot, Rect = SettingsUI.Rect(slot), Count = count };
            SetActive(slot, false);
        }

        Poll();
    }

    protected override float PollInterval => 1f;

    protected override void Poll()
    {
        if (_total == null || _total.IsDestroyed)
            return;

        // Still wakes the session browser: the dash's other cards and the world links read its listing.
        Browser();

        var counts = OnlinePresence.Counts;
        bool online = counts != null;
        ApplyFootprint(online);

        bool changed;
        if (counts != null)
        {
            _counts[(int)Platform.Windows] = counts.Windows;
            _counts[(int)Platform.Linux] = counts.Linux;
            _counts[(int)Platform.Android] = counts.Android;
            _counts[(int)Platform.Other] = counts.Other;
            Shown = counts.Total;
            changed = SetLine(_total, counts.Total.ToString(), DashTheme.Text);
            changed |= LayChips();
        }
        else
        {
            Shown = -1;
            string quiet = OnlinePresence.State switch
            {
                OnlinePresenceState.Waiting => string.Empty,
                OnlinePresenceState.NoCount => "No count",
                _ => "Offline",
            };
            changed = SetLine(_total, quiet, DashTheme.TextMuted);
        }
        if (_title != null && !_title.IsDestroyed)
        {
            var ink = online ? DashTheme.Text : DashTheme.TextDim;
            if (!_title.Color.Value.Equals(ink))
            {
                ListingStyle.SetTextColor(_title, ink);
                changed = true;
            }
        }

        if (changed)
            Repaint();
    }

    // One icon per platform somebody is on, left to right in enum order, none for a platform nobody is on.
    private bool LayChips()
    {
        bool changed = false;
        float x = 0f;
        for (int i = 0; i < PlatformCount; i++)
        {
            var chip = _chips[i];
            if (chip == null || chip.Slot.IsDestroyed)
                continue;
            bool show = _counts[i] > 0;
            if (chip.Slot.ActiveSelf.Value != show)
            {
                SetActive(chip.Slot, show);
                changed = true;
            }
            if (!show)
                continue;
            if (MathF.Abs(chip.Rect.OffsetMin.Value.x - x) > 0.01f)
            {
                SettingsUI.SetOffsets(chip.Rect, new float2(x, -RowHeight * 0.5f), new float2(x + ChipWidth, RowHeight * 0.5f));
                changed = true;
            }
            changed |= SetLine(chip.Count, _counts[i].ToString(), DashTheme.Text);
            x += ChipWidth;
        }
        return changed;
    }

    private static bool SetLine(Text? text, string value, in color ink)
    {
        if (text == null || text.IsDestroyed)
            return false;
        bool changed = false;
        if (!string.Equals(text.Content.Value, value, StringComparison.Ordinal))
        {
            ListingStyle.SetText(text, value);
            changed = true;
        }
        if (!text.Color.Value.Equals(ink))
        {
            ListingStyle.SetTextColor(text, ink);
            changed = true;
        }
        return changed;
    }

    // Collapsed is one line, expanded two, and the bottom edge does not move: the card takes its rows off the
    // top, so the row it was anchored to stays the row it is anchored to. A grid too short to hold the open
    // card keeps the footprint it placed us at and only the content switches. -xlinka
    private void ApplyFootprint(bool expanded)
    {
        // The layout switch is latched, the footprint write is not: the first poll can run before the
        // Widget exists, and a latch taken then would skip the write for good. The grid writes below
        // are equality-gated, so asking every poll costs nothing. -xlinka
        bool switched = _expanded != expanded;
        _expanded = expanded;

        if (switched)
            SetActive(_platforms, expanded);

        var rect = _titleRect;
        if (switched && rect != null && !rect.IsDestroyed)
        {
            if (expanded)
            {
                SettingsUI.SetAnchors(rect, new float2(0f, 1f), new float2(1f, 1f));
                SettingsUI.SetOffsets(rect, new float2(Pad, -(TitleTop + RowHeight)), new float2(-Pad, -TitleTop));
            }
            else
            {
                SettingsUI.SetAnchors(rect, new float2(0f, 0.5f), new float2(1f, 0.5f));
                SettingsUI.SetOffsets(rect, new float2(Pad, -RowHeight * 0.5f), new float2(-Pad, RowHeight * 0.5f));
            }
        }

        FitRows(expanded ? ExpandedHeight : CollapsedHeight);

        if (switched)
            Repaint();
    }

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
