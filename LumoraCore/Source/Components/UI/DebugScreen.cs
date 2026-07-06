// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Dashboard Debug screen: a live read-out of the active session's networking and in-flight asset
/// transfers (with per-asset progress). Built so a user can SEE whether a spawned mesh/texture is
/// actually transferring and how far along it is, instead of guessing. Two sub-tabs - Network and
/// Assets - refreshed a few times a second while the screen is visible.
/// </summary>
public sealed class DebugScreen : WidgetScreen
{
    private const float TabBarHeight = 44f;
    private const float RefreshInterval = 0.25f;

    private static readonly color TabActiveFill = new color(0.45f, 0.38f, 0.80f, 0.90f);

    private readonly List<(Slot page, BorderedImage tab)> _tabs = new();
    private int _activeTab;
    private float _refreshAccum;

    private Text? _networkText;
    private Text? _assetsText;

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

        _networkText = AddTab(tabBar, contentHost, "Network");
        _assetsText = AddTab(tabBar, contentHost, "Assets");

        SelectTab(0);
    }

    private Text AddTab(Slot tabBar, Slot contentHost, string name)
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

        var info = page.AddSlot("Info");
        var infoRect = info.AttachComponent<RectTransform>();
        infoRect.AnchorMin.Value = float2.Zero;
        infoRect.AnchorMax.Value = float2.One;
        infoRect.OffsetMin.Value = float2.Zero;
        infoRect.OffsetMax.Value = float2.Zero;
        var text = info.AttachComponent<Text>();
        text.Font.Target = _dashboard?.Font.Target!;
        text.Size.Value = 14f;
        text.Color.Value = TextPrimary;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = TextVerticalAlignment.Top;
        text.WordWrap.Value = true;
        text.Content.Value = "…";

        _tabs.Add((page, background));
        return text;
    }

    private void SelectTab(int index)
    {
        _activeTab = index;
        for (int i = 0; i < _tabs.Count; i++)
        {
            var (page, tab) = _tabs[i];
            if (page != null && !page.IsDestroyed)
                page.ActiveSelf.Value = i == index;
            if (tab != null && !tab.IsDestroyed)
                tab.Tint.Value = i == index ? TabActiveFill : TabFill;
        }
        _refreshAccum = RefreshInterval; // force an immediate refresh on next tick
        RefreshActive();
        _dashboard?.Slot.GetComponent<Canvas>()?.MarkDirty();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (!Slot.ActiveSelf.Value)
            return;

        _refreshAccum += delta;
        if (_refreshAccum < RefreshInterval)
            return;
        _refreshAccum = 0f;
        RefreshActive();
    }

    private void RefreshActive()
    {
        if (_activeTab == 0 && _networkText != null)
            _networkText.Content.Value = BuildNetworkInfo();
        else if (_activeTab == 1 && _assetsText != null)
            _assetsText.Content.Value = BuildAssetsInfo();

        Slot.GetComponentInParents<Canvas>()?.MarkDirty();
    }

    private static string BuildNetworkInfo()
    {
        var transferer = Engine.Current?.ActiveSessionTransferer;
        var session = transferer?.Session;
        if (session == null)
            return "Not connected to a session.";

        var world = session.World;
        var sync = session.Sync;
        var meta = session.Metadata;
        int users = world?.GetAllUsers()?.Count ?? 0;

        var sb = new StringBuilder();
        sb.AppendLine($"Role: {(world?.IsAuthority == true ? "Host" : "Client")}");
        sb.AppendLine($"World: {world?.WorldName?.Value ?? "-"}");
        sb.AppendLine($"Session: {meta?.SessionId ?? "-"}");
        sb.AppendLine($"Visibility: {meta?.Visibility.ToString() ?? "-"}");
        sb.AppendLine($"Users: {users}");
        if (sync != null)
        {
            sb.AppendLine($"Tick rate: {sync.SyncRate} Hz");
            sb.AppendLine($"Deltas  sent/recv: {sync.TotalSentDeltas}/{sync.TotalReceivedDeltas}");
            sb.AppendLine($"Fulls   sent/recv: {sync.TotalSentFulls}/{sync.TotalReceivedFulls}");
            sb.AppendLine($"Streams sent/recv: {sync.TotalSentStreams}/{sync.TotalReceivedStreams}");
            sb.AppendLine($"Corrections: {sync.TotalCorrections}");
            sb.AppendLine($"Queues  proc/tx: {sync.MessagesToProcessCount}/{sync.MessagesToTransmitCount}");
        }
        sb.AppendLine($"Asset jobs  up/down: {transferer!.UploadJobCount}/{transferer.DownloadJobCount}");
        sb.Append($"Asset pending/relay: {transferer.PendingAssetRequestCount}/{transferer.PendingRelayCount}");
        return sb.ToString();
    }

    private static string BuildAssetsInfo()
    {
        var transferer = Engine.Current?.ActiveSessionTransferer;
        if (transferer == null)
            return "Not connected to a session.";

        var transfers = transferer.GetActiveTransfers();
        if (transfers.Count == 0)
            return "No active asset transfers.";

        var sb = new StringBuilder();
        sb.AppendLine($"Active transfers: {transfers.Count}");
        foreach (var t in transfers)
        {
            string arrow = t.IsUpload ? "up" : "down";
            int pct = (int)(System.Math.Clamp(t.Fraction, 0f, 1f) * 100f);
            sb.AppendLine($"{arrow} {ShortName(t.Uri)}  [{Bar(t.Fraction)}] {pct}%  ({FormatBytes(t.Transferred)}/{FormatBytes(t.Total)})");
        }
        return sb.ToString();
    }

    private static string ShortName(Uri uri)
    {
        var s = uri.OriginalString;
        int slash = s.LastIndexOf('/');
        var name = slash >= 0 && slash + 1 < s.Length ? s.Substring(slash + 1) : s;
        return name.Length > 14 ? name.Substring(0, 14) : name;
    }

    private static string Bar(float fraction)
    {
        const int cells = 12;
        int filled = (int)(System.Math.Clamp(fraction, 0f, 1f) * cells + 0.5f);
        return new string('#', filled) + new string('-', cells - filled);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L)
            return $"{bytes / (1024f * 1024f):0.0}MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024f:0.0}KB";
        return $"{bytes}B";
    }
}
