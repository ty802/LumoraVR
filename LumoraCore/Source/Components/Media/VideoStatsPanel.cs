// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Text;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Media;

// The playback readout beside a video screen: what the decoder, the clock, the audio path, the
// buffer and the mixer are doing, one row each. The slot it sits on is synced and placed by the
// player; everything under it is LOCAL, because every peer reads its own decoder and mixer. -xlinka
[ComponentCategory("Media")]
public sealed class VideoStatsPanel : Component
{
    public readonly SyncRef<VideoTextureProvider> Video;

    // Authored in the player's controls pixel space and scaled by whoever places the panel.
    public const float PanelWidth = 660f;
    public const float PanelHeight = 372f;
    private const float RowHeight = 50f;
    private const float PollSeconds = 0.5f;
    private const float FrameMargin = 26f;

    private Slot? _canvasSlot;
    private FontProvider? _font;
    private FontProvider? _titleFont;
    private readonly Text?[] _values = new Text?[5];
    private readonly StringBuilder _builder = new StringBuilder(256);
    private float _poll;
    private long _lastFrames = -1;
    private bool _built;

    public VideoStatsPanel()
    {
        Video = new SyncRef<VideoTextureProvider>(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        Build();
    }

    private void Build()
    {
        if (_built || Slot == null || Slot.IsDestroyed)
            return;
        _built = true;

        var frameSlot = Slot.AddLocalSlot("Frame");
        frameSlot.LocalPosition.Value = new float3(0f, 0f, -FrameMargin * 0.25f);
        var frameMesh = frameSlot.AttachComponent<RoundedQuadMesh>();
        frameMesh.Size.Value = new float2(PanelWidth + FrameMargin * 2f, PanelHeight + FrameMargin * 2f);
        frameMesh.CornerRadius.Value = FrameMargin;
        frameMesh.CornerSegments.Value = 4;
        frameMesh.DualSided.Value = false;
        var frameMaterial = DashGlass.Attach(frameSlot);
        var frameRenderer = frameSlot.AttachComponent<MeshRenderer>();
        frameRenderer.Mesh.Target = frameMesh;
        frameRenderer.Material.Target = frameMaterial;
        frameRenderer.ShadowCastMode.Value = ShadowCastMode.Off;

        var fontSlot = Slot.AddLocalSlot("Font");
        _font = fontSlot.AttachComponent<FontProvider>();
        _font.URL.Value = ImportDialog.ResolveFontUrl(World) ?? DashTheme.FontRegular;
        if (_font.FallbackURLs.Count == 0)
            _font.FallbackURLs.Add(DashTheme.FontRegular);
        _titleFont = fontSlot.AttachComponent<FontProvider>();
        _titleFont.URL.Value = DashTheme.FontSemibold;
        if (_titleFont.FallbackURLs.Count == 0)
            _titleFont.FallbackURLs.Add(DashTheme.FontRegular);

        _canvasSlot = Slot.AddLocalSlot("Panel");
        var rect = _canvasSlot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(PanelWidth * -0.5f, PanelHeight * -0.5f);
        rect.OffsetMax.Value = new float2(PanelWidth * 0.5f, PanelHeight * 0.5f);
        _canvasSlot.AttachComponent<Canvas>();
        SettingsUI.Panel(_canvasSlot, DashTheme.GlassBackdrop, DashTheme.OutlineStrong, DashTheme.RadiusPanel);

        var content = _canvasSlot.AddSlot("Content");
        Fill(content.AttachComponent<RectTransform>());
        content.AttachComponent<GraphicChunkRoot>();
        var column = content.AttachComponent<VerticalLayout>();
        column.Spacing.Value = 6f;
        column.PaddingLeft.Value = 12f;
        column.PaddingRight.Value = 12f;
        column.PaddingTop.Value = 12f;
        column.PaddingBottom.Value = 12f;
        column.ForceExpandWidth.Value = true;
        column.ForceExpandHeight.Value = false;

        var header = content.AddSlot("Header");
        header.AttachComponent<RectTransform>();
        Metrics(header, 44f, 0f);
        SettingsUI.Panel(header, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);
        var headerLayout = header.AttachComponent<HorizontalLayout>();
        headerLayout.PaddingLeft.Value = 14f;
        headerLayout.PaddingRight.Value = 14f;
        headerLayout.ForceExpandWidth.Value = true;
        headerLayout.ForceExpandHeight.Value = true;
        var headerUi = new UIBuilder(header);
        headerUi.Font(_titleFont);
        var title = headerUi.Text("VideoStats.Title".AsLocale("Playback stats"), 20f, DashTheme.Text);
        title.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        title.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        title.WordWrap.Value = false;

        var labels = new LocaleText[]
        {
            "VideoStats.Video".AsLocale("Video"),
            "VideoStats.Clock".AsLocale("Clock"),
            "VideoStats.Audio".AsLocale("Audio"),
            "VideoStats.Buffer".AsLocale("Buffer"),
            "VideoStats.Mixer".AsLocale("Mixer"),
        };
        for (int i = 0; i < labels.Length; i++)
        {
            var row = content.AddSlot("Row" + i);
            row.AttachComponent<RectTransform>();
            Metrics(row, RowHeight, 0f);
            SettingsUI.Panel(row, DashTheme.Surface, DashTheme.Outline, DashTheme.RadiusControl);
            var rowLayout = row.AttachComponent<HorizontalLayout>();
            rowLayout.Spacing.Value = 10f;
            rowLayout.PaddingLeft.Value = 14f;
            rowLayout.PaddingRight.Value = 12f;
            rowLayout.ForceExpandWidth.Value = false;
            rowLayout.ForceExpandHeight.Value = true;

            var ui = new UIBuilder(row);
            ui.Font(_titleFont);
            ui.PushStyle().MinWidth(96f).PreferredWidth(96f).FlexibleWidth(0f);
            var label = ui.Text(labels[i], 15f, DashTheme.TextMuted);
            label.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
            label.VerticalAlignment.Value = TextVerticalAlignment.Middle;
            label.WordWrap.Value = false;
            ui.PopStyle();
            ui.Font(_font);
            ui.PushStyle().MinWidth(0f).PreferredWidth(0f).FlexibleWidth(1f);
            var value = ui.Text("-", 15f, DashTheme.Text);
            value.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
            value.VerticalAlignment.Value = TextVerticalAlignment.Middle;
            value.WordWrap.Value = false;
            ui.PopStyle();
            _values[i] = value;
        }
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (Slot == null || !Slot.ActiveSelf.Value)
        {
            _poll = 0f;
            _lastFrames = -1;
            return;
        }
        _poll -= World?.Time?.Delta ?? 0f;
        if (_poll > 0f)
            return;
        float elapsed = PollSeconds - _poll;
        _poll = PollSeconds;
        var video = Video.Target;
        if (video == null || video.IsDestroyed)
            return;
        Refresh(video, elapsed);
    }

    private void Refresh(VideoTextureProvider video, float elapsed)
    {
        var asset = video.Asset;
        var decoder = video.DecoderStats;
        bool ready = asset != null && asset.IsReady;

        float fps = 0f;
        if (_lastFrames >= 0 && decoder.VideoFrames >= _lastFrames && elapsed > 0.05f)
            fps = (float)((decoder.VideoFrames - _lastFrames) / elapsed);
        _lastFrames = decoder.VideoFrames;

        string engine = video.CurrentPlaybackEngine.Value;
        double length = video.ClipLength;
        var mixerStats = Engine.Current?.AudioSystem?.Simulator.Stats;
        var sb = _builder;

        sb.Clear();
        if (ready && asset!.VideoWidth > 0)
            sb.Append(asset.VideoWidth).Append('x').Append(asset.VideoHeight);
        else
            sb.Append("no picture");
        sb.Append("   ").Append(fps.ToString("0.0")).Append(" fps   ").Append(string.IsNullOrEmpty(engine) ? "-" : engine);
        if (video.IsStreaming) sb.Append("   live");
        SetText(_values[0], sb.ToString());

        sb.Clear();
        sb.Append(decoder.DecoderSeconds >= 0.0 ? FormatTime(decoder.DecoderSeconds) : "-");
        sb.Append(" decoded   ").Append(FormatTime(video.IsStreaming ? 0.0 : video.Position));
        if (length > 0.0) sb.Append(" / ").Append(FormatTime(length));
        sb.Append(" wanted   ").Append(video.CurrentClockError.Value.ToString("+0.00;-0.00")).Append(" s   ").Append(decoder.State ?? "-");
        SetText(_values[1], sb.ToString());

        sb.Clear();
        sb.Append("lead ").Append((decoder.AudioLeadSeconds * 1000.0).ToString("0")).Append(" ms   queued ").Append(decoder.AudioQueued);
        sb.Append("   ").Append(decoder.AudioBlocks).Append(" in / ").Append(decoder.AudioBlocksPushed).Append(" out / ").Append(decoder.AudioBlocksLate).Append(" late");
        SetText(_values[2], sb.ToString());

        sb.Clear();
        sb.Append("underruns ").Append(video.AudioUnderruns).Append("   drops ").Append(video.AudioDrops);
        sb.Append("   flushes ").Append(video.AudioFlushes).Append("   waits ").Append(video.AudioWaits);
        sb.Append("   latency ").Append((video.LatencySeconds * 1000.0).ToString("0")).Append(" ms");
        SetText(_values[3], sb.ToString());

        sb.Clear();
        if (mixerStats.HasValue)
        {
            var m = mixerStats.Value;
            sb.Append("underruns ").Append(m.DeviceUnderruns).Append("   dropped ").Append(m.DroppedFrames);
            sb.Append("   outputs ").Append(m.ActiveOutputs).Append("   peak ").Append(m.LastDevicePeak.ToString("0.00"));
            if (m.NonFiniteSamples > 0) sb.Append("   non-finite ").Append(m.NonFiniteSamples);
        }
        else
        {
            sb.Append("off");
        }
        SetText(_values[4], sb.ToString());
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0.0)
            return "0:00";
        int total = (int)seconds;
        int minutes = total / 60;
        int secs = total % 60;
        if (minutes < 60)
            return $"{minutes}:{secs:00}";
        int hours = minutes / 60;
        return $"{hours}:{minutes % 60:00}:{secs:00}";
    }

    private static void Metrics(Slot slot, float height, float flexibleHeight)
    {
        var element = slot.GetComponent<LayoutElement>() ?? slot.AttachComponent<LayoutElement>();
        element.MinWidth.Value = 0f;
        element.PreferredWidth.Value = 0f;
        element.FlexibleWidth.Value = 1f;
        element.MinHeight.Value = height;
        element.PreferredHeight.Value = height;
        element.FlexibleHeight.Value = flexibleHeight;
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }

    private static void SetText(Text? text, string content)
    {
        if (text == null || text.IsDestroyed)
            return;
        if (!string.Equals(text.Content.Value, content, StringComparison.Ordinal))
            text.Content.Value = content;
    }
}
