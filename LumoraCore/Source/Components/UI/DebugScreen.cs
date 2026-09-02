// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// live read-out of the active session's networking and in-flight asset transfers, with per-asset
// progress. Two sub-tabs - Network and Assets - refreshed a few times a second while visible.
[ComponentCategory("Hidden")]
public sealed class DebugScreen : WidgetScreen, ITabbedScreen
{
    // The capture harness raises a named tab; nothing in the dash calls this.
    public bool ShowTab(string name)
    {
        for (int i = 0; i < _tabs.Count; i++)
        {
            if (!string.Equals(_tabs[i].name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            SelectTab(i);
            return true;
        }
        return false;
    }

    private const float TabBarHeight = 44f;
    private const float RefreshInterval = 0.25f;

    // Opaque blend, not the translucent AccentSoft: over the panel that wash lands DARKER than a
    // plain Surface tab, so the selected tab would read as the recessed one. -xlinka
    private static readonly color TabActiveFill = color.Lerp(DashTheme.Surface, DashTheme.Accent, 0.30f);

    private readonly List<(Slot page, BorderedImage tab, string name)> _tabs = new();
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
        AddFillLabel(tabSlot, name, DashTheme.FontBody, TextPrimary, SemiboldFont);

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
        // Fixed width here on purpose: these are columns of numbers that have to line up. -xlinka
        text.Font.Target = _dashboard?.FontMono.Target ?? _dashboard?.Font.Target!;
        text.Size.Value = DashTheme.FontSmall;
        text.Color.Value = DashTheme.TextDim;
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = TextVerticalAlignment.Top;
        text.WordWrap.Value = true;
        text.Content.Value = "…";

        _tabs.Add((page, background, name));
        return text;
    }

    private void SelectTab(int index)
    {
        _activeTab = index;
        for (int i = 0; i < _tabs.Count; i++)
        {
            var (page, tab, _) = _tabs[i];
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
            sb.AppendLine($"Desyncs: {sync.TotalDesyncs}  dup deltas: {sync.TotalDuplicateDeltas}");
            sb.AppendLine($"List mismatches: {sync.TotalListMismatches}  targeted resyncs: {sync.TotalTargetedResyncs}");
            sb.AppendLine($"Delta backlog: {sync.PendingDeltaCount} batches / {sync.PendingDeltaBytes / 1024} KB{(sync.IsDesynced ? " (DESYNCED)" : "")}");
            sb.AppendLine($"Queues  proc/tx: {sync.MessagesToProcessCount}/{sync.MessagesToTransmitCount}");
        }
        sb.AppendLine($"Asset jobs  up/down: {transferer!.UploadJobCount}/{transferer.DownloadJobCount}");
        sb.Append($"Asset pending/relay: {transferer.PendingAssetRequestCount}/{transferer.PendingRelayCount}");
        return sb.ToString();
    }

    // Two sources, one page: what is moving between this client and the content service, and what is
    // moving between this client and its peers in a session. Assets in flight covers both, plus the
    // decode and upload that follows a transfer. -xlinka
    private static string BuildAssetsInfo()
    {
        var sb = new StringBuilder();
        int loading = Lumora.Core.Assets.Asset.LoadingCount;
        sb.AppendLine($"Assets loading: {loading}");

        var cloud = Lumora.Nexus.Cloud.Cdn.TransferRegistry.Snapshot();
        var totals = Lumora.Nexus.Cloud.Cdn.TransferRegistry.Totals();
        sb.AppendLine($"Cloud: {totals.downloads} down / {totals.uploads} up"
            + (totals.remainingBytes > 0 ? $"  {FormatBytes(totals.remainingBytes)} left" : string.Empty)
            + $"  (done {Lumora.Nexus.Cloud.Cdn.TransferRegistry.CompletedDownloads} down / {Lumora.Nexus.Cloud.Cdn.TransferRegistry.CompletedUploads} up)");
        foreach (var entry in cloud)
        {
            string arrow = entry.IsUpload ? "up  " : "down";
            float fraction = (float)entry.Fraction;
            int pct = (int)(System.Math.Clamp(fraction, 0f, 1f) * 100f);
            string size = entry.TotalBytes > 0
                ? $"({FormatBytes(entry.TransferredBytes)}/{FormatBytes(entry.TotalBytes)})"
                : "(size unknown)";
            sb.AppendLine($"{arrow} {ShortHash(entry.Hash)}  [{Bar(fraction)}] {pct}%  {size}");
        }

        var transferer = Engine.Current?.ActiveSessionTransferer;
        if (transferer == null)
        {
            sb.AppendLine("Peers: not in a session.");
            return sb.ToString();
        }

        var transfers = transferer.GetActiveTransfers();
        sb.AppendLine($"Peers: {transfers.Count} active"
            + $"  jobs {transferer.UploadJobCount} up / {transferer.DownloadJobCount} down"
            + $"  pending {transferer.PendingAssetRequestCount}");
        foreach (var t in transfers)
        {
            string arrow = t.IsUpload ? "up  " : "down";
            int pct = (int)(System.Math.Clamp(t.Fraction, 0f, 1f) * 100f);
            sb.AppendLine($"{arrow} {ShortName(t.Uri)}  [{Bar(t.Fraction)}] {pct}%  ({FormatBytes(t.Transferred)}/{FormatBytes(t.Total)})");
        }
        return sb.ToString();
    }

    private static string ShortHash(string hash)
        => string.IsNullOrEmpty(hash) ? "?" : hash.Length > 14 ? hash.Substring(0, 14) : hash;

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
