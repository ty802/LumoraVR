// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.Twitch;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Localization;
using Lumora.Core.Media;
using Lumora.Core.Persistence;
using LumoraLogger = Lumora.Core.Logging.Logger;
using StereoLayoutMode = Lumora.Core.Components.Import.StereoLayout;
using WorldAudioOutput = Lumora.Core.Components.Audio.AudioOutput;
using VideoProjectionMode = Lumora.Core.Components.Import.VideoProjection;

namespace Lumora.Core.Components.Media;

// The whole in-world video item: a screen, a frame, a sphere for the two projections, the sound, and
// the controls under it. Attaching this to a bare slot builds all of it, so spawning a player is one
// AttachComponent and nothing has to know the part list.
//
// It owns no playback state of its own. The provider's SyncPlayback is the clock every peer already
// agrees on, so this is a face on top of it: everything the panel shows is read back out of replicated
// state or out of the LOCAL decoder's own outputs, and every label write is silent. That is the point -
// a panel that broadcast its own text would have four peers fighting over one string for a value all
// four of them can compute, and the per-peer half of it (which engine picked, what failed here) is
// honestly different on each machine anyway. -xlinka
[ComponentCategory("Media")]
public sealed class VideoPlayer : Component, IPlayable
{
    public readonly SyncRef<VideoTextureProvider> Video;

    public readonly Sync<StereoLayoutMode> StereoLayout;

    public readonly Sync<VideoProjectionMode> Projection;

    // Squash or stretch the picture after the aspect is worked out. Some 360 footage is packed at an
    // aspect its own header lies about, and this is the knob for it.
    public readonly Sync<float2> SizeCompensation;

    // Metres across. The height follows the video's aspect, so this is the only size control.
    public readonly Sync<float> DisplayWidth;

    // The emitter, aliased because the field carries the same name as its own type.
    public readonly SyncRef<WorldAudioOutput> AudioOutput;

    // The readout in the corner of the picture: decoder, clock, audio path and mixer numbers.
    public readonly Sync<bool> ShowStats;

    // A twitch.tv channel brings its chat along, parked off the left edge of the screen.
    public readonly Sync<bool> ShowChat;

    // The controls canvas is authored in pixels and then scaled so its width matches the screen's, the
    // way the import dialogs do it: layout numbers stay readable and the panel tracks the item's size.
    private const float CanvasWidth = 1280f;
    private const float CanvasHeight = 220f;

    // Metres of air between the bottom of the screen and the top of the controls, and how far the
    // bar pitches out of the screen's plane about that top edge: bottom forward, face turned up
    // toward whoever is standing in front, the way a desk console sits under a monitor.
    private const float ControlsGap = 0.04f;
    private const float ControlsTiltDegrees = 14f;

    // The bar's rounded backing, in its pixel space (the bar is scaled to the screen width).
    private const float ControlsFrameMargin = 26f;

    // How far the frame sticks out past the screen, and how far behind it sits.
    private const float FrameMargin = 0.02f;
    private const float FrameDepth = 0.004f;

    // The 360 and 180 views are an orb parked beside the screen, not a dome around the room: its
    // diameter follows the screen height and it sits one gap past the frame's right edge.
    private const float OrbHeightRatio = 0.9f;
    private const float OrbGap = 0.06f;

    // The chat column stands as tall as the screen, one gap off its left edge, turned a little toward
    // whoever is watching so the two read as one curved surface.
    private const float ChatGap = 0.05f;
    private const float ChatYawDegrees = 22f;


    // The live badge over the top corner of the picture, in the controls' pixel space.
    private const float LiveBadgeWidth = 132f;
    private const float LiveBadgeHeight = 46f;
    private const float LiveBadgeMargin = 18f;

    private const float DefaultAspect = 9f / 16f;

    private const float SeekStep = 10f;

    private Slot? _displaySlot;
    private Slot? _frameSlot;
    private Slot? _sphereSlot;
    private Slot? _controlsSlot;
    private Slot? _controlsFrameSlot;
    private Slot? _audioOnlySlot;

    private QuadMesh? _displayMesh;
    private SphereMesh? _sphereMesh;
    private RoundedQuadMesh? _frameMesh;
    private BoxCollider? _displayCollider;
    private UnlitMaterial? _displayMaterial;
    private UnlitMaterial? _sphereMaterial;
    private FontProvider? _font;

    private Text? _titleLabel;
    private Text? _engineLabel;
    private Text? _liveBadge;
    private Text? _statusLabel;
    private Text? _timeLabel;
    private Text? _playLabel;
    private Button? _playButton;
    private Button? _loopButton;
    private Button? _spatialButton;
    private Button? _statsButton;
    private Slot? _chatSlot;
    private TwitchChat? _chat;
    private TwitchChatPanel? _chatPanel;
    private Slot? _statsSlot;
    private Slot? _liveBadgeSlot;
    private FontProvider? _titleFont;
    private Slider? _timeline;
    private Slider? _volumeSlider;
    private TextInput? _sourceInput;

    private bool _built;
    private bool _wired;
    private bool _subscribed;
    private float _labelPoll;
    private float2 _lastDisplaySize = new float2(-1f, -1f);
    private bool _toggleStateKnown;
    private bool _lastLoop;
    private bool _lastSpatialize;
    private bool _lastStats;

    public VideoPlayer()
    {
        Video = new SyncRef<VideoTextureProvider>(this);
        StereoLayout = new Sync<StereoLayoutMode>(this, StereoLayoutMode.None);
        Projection = new Sync<VideoProjectionMode>(this, VideoProjectionMode.Flat);
        SizeCompensation = new Sync<float2>(this, float2.One);
        DisplayWidth = new Sync<float>(this, 1f);
        AudioOutput = new SyncRef<WorldAudioOutput>(this);
        ShowStats = new Sync<bool>(this, false);
        ShowChat = new Sync<bool>(this, true);
    }

    public VideoTextureProvider? Provider => Video.Target;

    // SOURCE

    public void SetSource(Uri url, bool stream)
    {
        var video = EnsureProvider();
        if (video == null)
            return;
        video.Stream.Value = stream;
        video.URL.Value = url;
        SyncChat(url);
    }

    // CHAT

    // Only the peer that set the source builds or removes the chat; the slot and its components
    // replicate from there, and every peer then reads the channel for itself.
    private void SyncChat(Uri? url)
    {
        if (Slot == null || Slot.IsDestroyed)
            return;

        string channel = TwitchChat.ChannelFromUrl(url);
        if (channel.Length == 0)
        {
            var stale = Slot.FindChild("Chat", recursive: false);
            if (stale != null && !stale.IsDestroyed)
                stale.Destroy();
            _chatSlot = null;
            _chat = null;
            _chatPanel = null;
            return;
        }

        _chatSlot = Slot.FindChildOrAdd("Chat");
        _chat = _chatSlot.GetOrAttachComponent<TwitchChat>();
        if (!string.Equals(_chat.Channel.Value, channel, StringComparison.Ordinal))
        {
            LumoraLogger.Debug($"VideoPlayer: twitch channel '{channel}' detected, chat panel goes beside the screen");
            _chat.Channel.Value = channel;
        }
        _chatPanel = _chatSlot.GetOrAttachComponent<TwitchChatPanel>();
        if (!ReferenceEquals(_chatPanel.Chat.Target, _chat))
            _chatPanel.Chat.Target = _chat;
        _lastDisplaySize = new float2(-1f, -1f);
    }

    public void SetSource(string source)
    {
        var url = ParseSource(source);
        if (url == null)
            return;
        // A file on this machine is seekable; anything reached over the network is pulled as it plays.
        bool stream = url.IsAbsoluteUri
            && !url.IsFile
            && !string.Equals(url.Scheme, "local", StringComparison.OrdinalIgnoreCase);
        SetSource(url, stream);
    }

    public static Uri? ParseSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;
        source = source.Trim().Trim('"');
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return uri;
        try
        {
            return new Uri(source);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    // IPlayable
    // Forwarded rather than mirrored: two copies of a playhead is two playheads, and the provider's is
    // the one the decoder and the other peers are reading.

    public float Position
    {
        get => Provider?.Position ?? 0f;
        set { var v = Provider; if (v != null) v.Position = value; }
    }

    public float Speed
    {
        get => Provider?.Speed ?? 1f;
        set { var v = Provider; if (v != null) v.Speed = value; }
    }

    public double ClipLength => Provider?.ClipLength ?? -1.0;

    public bool IsStreaming => Provider?.IsStreaming ?? false;

    public bool IsPlaying => Provider?.IsPlaying ?? false;

    public bool IsFinished => Provider?.IsFinished ?? false;

    public float NormalizedPosition
    {
        get => Provider?.NormalizedPosition ?? -1f;
        set { var v = Provider; if (v != null) v.NormalizedPosition = value; }
    }

    public bool Loop
    {
        get => Provider?.Loop ?? false;
        set { var v = Provider; if (v != null) v.Loop = value; }
    }

    [SyncMethod]
    public void Play() => Provider?.Play();

    [SyncMethod]
    public void Stop() => Provider?.Stop();

    [SyncMethod]
    public void Pause() => Provider?.Pause();

    [SyncMethod]
    public void Resume() => Provider?.Resume();

    [SyncMethod]
    public void TogglePlayback() => Provider?.TogglePlayback();

    // LIFECYCLE

    public override void OnAttach()
    {
        base.OnAttach();
        Build();
    }

    public override void OnStart()
    {
        base.OnStart();
        // A peer that received this item, or loaded it out of a save, already has every part: find them
        // rather than building a second set on top.
        Wire();
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        base.Load(node, control);
        control.OnLoaded(Wire);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        ApplyProjection();
        ApplyStereo();
    }

    public override void OnDestroy()
    {
        if (_subscribed && _sourceInput != null && !_sourceInput.IsDestroyed)
            _sourceInput.EditingFinished -= OnSourceEditingFinished;
        _subscribed = false;
        base.OnDestroy();
    }

    // BUILD

    private VideoTextureProvider? EnsureProvider()
    {
        var video = Video.Target;
        if (video != null && !video.IsDestroyed)
            return video;
        if (Slot == null || Slot.IsDestroyed)
            return null;
        video = Slot.GetComponent<VideoTextureProvider>() ?? Slot.AttachComponent<VideoTextureProvider>();
        Video.Target = video;
        return video;
    }

    private void Build()
    {
        if (_built || Slot == null || Slot.IsDestroyed)
            return;
        _built = true;

        var root = Slot.GetOrAttachComponent<ObjectRoot>();
        if (string.IsNullOrEmpty(root.ObjectName.Value))
            root.ObjectName.Value = "Video Player";

        var grab = Slot.GetOrAttachComponent<Grabbable>();
        grab.AllowGrab.Value = true;
        grab.Scalable.Value = true;

        var video = EnsureProvider();
        if (video == null)
            return;

        var audio = AudioOutput.Target;
        if (audio == null || audio.IsDestroyed)
        {
            audio = Slot.GetComponent<WorldAudioOutput>() ?? Slot.AttachComponent<WorldAudioOutput>();
            AudioOutput.Target = audio;
        }
        audio.Source.Target = video;
        audio.AudioTypeGroup.Value = Lumora.Core.Audio.AudioTypeGroup.Multimedia;
        audio.Spatialize.Value = true;
        audio.SpatialBlend.Value = 1f;
        audio.MinDistance.Value = 1f;
        audio.MaxDistance.Value = 25f;
        audio.RolloffMode.Value = Lumora.Audio.AudioRolloffCurve.LogarithmicFadeOff;
        // A video panel that somebody carries across the room must not pitch-shift while they walk.
        audio.DopplerLevel.Value = 0f;

        BuildFont();
        BuildDisplay(video);
        BuildLiveBadge();
        BuildStats(video);
        BuildFrame();
        BuildSphere(video);
        BuildControls(video, audio);
        SyncChat(video.URL.Value);

        _wired = true;
        ApplyProjection();
        ApplyStereo();
        UpdateSurfaces(video);
    }

    private void BuildFont()
    {
        var fontSlot = Slot.FindChildOrAdd("Font");
        _font = fontSlot.GetOrAttachComponent<FontProvider>();
        // Text with no font draws nothing at all, so a face is picked here rather than left to whatever
        // the world happens to have registered.
        var url = ImportDialog.ResolveFontUrl(World) ?? DashTheme.FontRegular;
        _font.URL.Value = url;
        if (_font.FallbackURLs.Count == 0)
            _font.FallbackURLs.Add(DashTheme.FontRegular);

        var boldSlot = fontSlot.FindChildOrAdd("FontBold");
        _titleFont = boldSlot.GetOrAttachComponent<FontProvider>();
        _titleFont.URL.Value = DashTheme.FontSemibold;
        if (_titleFont.FallbackURLs.Count == 0)
            _titleFont.FallbackURLs.Add(DashTheme.FontRegular);
    }

    private void BuildDisplay(VideoTextureProvider video)
    {
        _displaySlot = Slot.FindChildOrAdd("Display");
        _displayMesh = _displaySlot.GetOrAttachComponent<QuadMesh>();
        _displayMesh.DualSided.Value = false;

        _displayMaterial = _displaySlot.GetOrAttachComponent<UnlitMaterial>();
        _displayMaterial.Texture.Target = video;
        _displayMaterial.BlendMode.Value = BlendMode.Opaque;
        _displayMaterial.Culling.Value = Culling.None;
        _displayMaterial.TintColor.Value = colorHDR.White;

        var renderer = _displaySlot.GetOrAttachComponent<MeshRenderer>();
        renderer.Mesh.Target = _displayMesh;
        renderer.Material.Target = _displayMaterial;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;

        _displayCollider = _displaySlot.GetOrAttachComponent<BoxCollider>();

        // Said on the screen itself, because a radio stream with no picture is otherwise a black slab
        // that looks broken. The readable side of a quad is its +Z, so this sits just in front of it.
        _audioOnlySlot = _displaySlot.FindChildOrAdd("AudioOnly");
        _audioOnlySlot.LocalPosition.Value = new float3(0f, 0f, 0.002f);
        var audioOnlyText = _audioOnlySlot.GetOrAttachComponent<TextRenderer>();
        audioOnlyText.Text.Value = "Audio only";
        audioOnlyText.Size.Value = 0.08f;
        audioOnlyText.Color.Value = DashTheme.TextMuted;
        if (_font != null)
            audioOnlyText.Font.Target = _font;
        audioOnlyText.HorizontalAlign.Value = TextHorizontalAlignment.Center;
        audioOnlyText.VerticalAlign.Value = TextVerticalAlignment.Middle;
        _audioOnlySlot.ActiveSelf.Value = false;
    }

    // A red chip over the top corner of the picture while the source is live.
    private void BuildLiveBadge()
    {
        if (_displaySlot == null)
            return;

        // Beside the screen slot, not under it: a canvas parented to the picture's own slot never drew.
        _liveBadgeSlot = Slot.FindChildOrAdd("LiveBadge");
        var rect = _liveBadgeSlot.GetOrAttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(LiveBadgeWidth * -0.5f, LiveBadgeHeight * -0.5f);
        rect.OffsetMax.Value = new float2(LiveBadgeWidth * 0.5f, LiveBadgeHeight * 0.5f);
        _liveBadgeSlot.GetOrAttachComponent<Canvas>();
        var panel = _liveBadgeSlot.GetComponent<RoundedPanel>() ?? _liveBadgeSlot.AttachComponent<RoundedPanel>();
        panel.Color.Value = DashTheme.Negative;
        panel.CornerRadius.Value = DashTheme.RadiusControl;
        panel.OutlineThickness.Value = 0f;

        var label = _liveBadgeSlot.FindChildOrAdd("Label");
        FillRect(label);
        if (label.GetComponent<Text>() == null)
        {
            var ui = new UIBuilder(label);
            ui.Font(_titleFont);
            var text = ui.Text("VideoPlayer.Live".AsLocale("LIVE"), 24f, DashTheme.Text);
            FillRect(text.Slot);
            text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
            text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
            text.WordWrap.Value = false;
        }
        _liveBadgeSlot.ActiveSelf.Value = false;
    }

    // The readout stands beside the screen on a synced anchor the player places; the panel component
    // under it builds its own local canvas on every peer.
    private void BuildStats(VideoTextureProvider video)
    {
        _statsSlot = Slot.FindChildOrAdd("Stats");
        _statsSlot.ActiveSelf.Value = ShowStats.Value;
        var panel = _statsSlot.GetOrAttachComponent<VideoStatsPanel>();
        if (!ReferenceEquals(panel.Video.Target, video))
            panel.Video.Target = video;
    }

    private void BuildFrame()
    {
        _frameSlot = Slot.FindChildOrAdd("Frame");
        _frameSlot.LocalPosition.Value = new float3(0f, 0f, -FrameDepth);
        _frameMesh = _frameSlot.GetOrAttachComponent<RoundedQuadMesh>();
        _frameMesh.CornerRadius.Value = 0.02f;
        _frameMesh.CornerSegments.Value = 4;
        _frameMesh.DualSided.Value = false;

        var frameMaterial = DashGlass.Attach(_frameSlot);

        var renderer = _frameSlot.GetOrAttachComponent<MeshRenderer>();
        renderer.Mesh.Target = _frameMesh;
        renderer.Material.Target = frameMaterial;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
    }

    private void BuildSphere(VideoTextureProvider video)
    {
        _sphereSlot = Slot.FindChildOrAdd("Sphere");
        _sphereMesh = _sphereSlot.GetOrAttachComponent<SphereMesh>();
        _sphereMesh.Radius.Value = 0.25f;
        _sphereMesh.Segments.Value = 64;
        _sphereMesh.Rings.Value = 32;

        _sphereMaterial = _sphereSlot.GetOrAttachComponent<UnlitMaterial>();
        _sphereMaterial.Texture.Target = video;
        _sphereMaterial.BlendMode.Value = BlendMode.Opaque;
        // The unlit shader compiles with culling off, so this is a statement of intent rather than a
        // switch: nothing is culled either way. It costs nothing, because a ray from the middle of a
        // sphere leaves through exactly one surface, so a viewer inside sees one face and not two.
        _sphereMaterial.Culling.Value = Culling.None;
        _sphereMaterial.TintColor.Value = colorHDR.White;

        var renderer = _sphereSlot.GetOrAttachComponent<MeshRenderer>();
        renderer.Mesh.Target = _sphereMesh;
        renderer.Material.Target = _sphereMaterial;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
        _sphereSlot.ActiveSelf.Value = false;
    }

    // CONTROLS

    private void BuildControls(VideoTextureProvider video, WorldAudioOutput audio)
    {
        _controlsSlot = Slot.FindChildOrAdd("Controls");
        var rect = _controlsSlot.GetOrAttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(CanvasWidth * -0.5f, CanvasHeight * -0.5f);
        rect.OffsetMax.Value = new float2(CanvasWidth * 0.5f, CanvasHeight * 0.5f);
        _controlsSlot.GetOrAttachComponent<Canvas>();

        var background = _controlsSlot.GetComponent<RoundedPanel>() ?? _controlsSlot.AttachComponent<RoundedPanel>();
        background.Color.Value = DashTheme.GlassBackdrop;
        background.CornerRadius.Value = DashTheme.RadiusPanel;
        background.OutlineColor.Value = DashTheme.OutlineStrong;
        background.OutlineThickness.Value = DashTheme.OutlineWidth;

        // The same rounded backing the screen has. It lives beside the canvas, not under it: a mesh
        // under a canvas slot is treated as part of the canvas and hides what it was meant to back.
        _controlsFrameSlot = Slot.FindChildOrAdd("ControlsFrame");
        var frameMesh = _controlsFrameSlot.GetOrAttachComponent<RoundedQuadMesh>();
        frameMesh.Size.Value = new float2(CanvasWidth + ControlsFrameMargin * 2f, CanvasHeight + ControlsFrameMargin * 2f);
        frameMesh.CornerRadius.Value = ControlsFrameMargin;
        frameMesh.CornerSegments.Value = 4;
        frameMesh.DualSided.Value = false;
        var frameMaterial = DashGlass.Attach(_controlsFrameSlot);
        var frameRenderer = _controlsFrameSlot.GetOrAttachComponent<MeshRenderer>();
        frameRenderer.Mesh.Target = frameMesh;
        frameRenderer.Material.Target = frameMaterial;
        frameRenderer.ShadowCastMode.Value = ShadowCastMode.Off;

        var content = _controlsSlot.FindChildOrAdd("Content");
        FillRect(content);
        // Its own chunk so the buttons draw above the rounded backdrop on the canvas root.
        content.GetOrAttachComponent<GraphicChunkRoot>();
        if (content.ChildCount > 0)
            return;

        var ui = new UIBuilder(content);
        ui.Font(_font);
        ui.FontSize(16f);
        ui.TextColor(DashTheme.Text);
        ui.ForegroundColor(DashTheme.Accent);
        ui.BackgroundColor(DashTheme.Surface);

        var column = ui.VerticalLayout(6f, 10f);
        column.ForceExpandWidth.Value = true;
        column.ForceExpandHeight.Value = false;
        column.MainAlignment.Value = MainAxisAlignment.Center;
        FillRect(ui.Current);

        BuildTitleRow(ui);
        BuildTimelineRow(ui, video);
        BuildTransportRow(ui, audio);
        BuildSourceRow(ui);

        ui.NestOut();
    }

    private void BuildTitleRow(UIBuilder ui)
    {
        ui.PushStyle();
        RowHeight(ui, 32f);
        ui.HorizontalLayout(8f);

        ui.PushStyle().MinWidth(0f).PreferredWidth(0f).FlexibleWidth(1f);
        ui.Font(_titleFont);
        _titleLabel = Label(ui, "TitleLabel", "No video", 22f, DashTheme.Text, TextHorizontalAlignment.Left);
        ui.Font(_font);
        ui.PopStyle();

        ui.PushStyle().MinWidth(150f).PreferredWidth(150f).FlexibleWidth(0f);
        _engineLabel = Label(ui, "EngineLabel", string.Empty, 14f, DashTheme.TextMuted, TextHorizontalAlignment.Right);
        ui.PopStyle();

        ui.PushStyle().MinWidth(60f).PreferredWidth(60f).FlexibleWidth(0f);
        _liveBadge = Label(ui, "LiveBadge", string.Empty, 15f, DashTheme.Negative, TextHorizontalAlignment.Right);
        ui.PopStyle();

        ui.PushStyle().MinWidth(280f).PreferredWidth(280f).FlexibleWidth(0f);
        _statusLabel = Label(ui, "StatusLabel", string.Empty, 14f, DashTheme.TextDim, TextHorizontalAlignment.Right);
        ui.PopStyle();

        ui.NestOut();
        ui.PopStyle();
    }

    private void BuildTimelineRow(UIBuilder ui, VideoTextureProvider video)
    {
        ui.PushStyle();
        RowHeight(ui, 30f);
        ui.HorizontalLayout(10f);

        _timeline = ui.Slider(0f, 0f, 1f, null, DashTheme.Field);
        _timeline.Slot.Name.Value = "Timeline";
        Metrics(_timeline.Slot, 0f, 0f, 1f, 24f, 24f, 0f);

        // The playhead drives the slider and the slider seeks back through the same link: a hand write
        // is the only thing that can move the value away from what the drive put there last frame, so
        // that difference IS the scrub, and a drive that read its own output would seek every frame.
        var driver = _timeline.Slot.GetOrAttachComponent<PlaybackPositionDriver>();
        driver.Source.Target = video;
        driver.UseNormalizedPosition.Value = true;
        driver.WriteBack.Value = true;
        driver.Target.DriveTarget(_timeline.Value);

        ui.PushStyle().MinWidth(190f).PreferredWidth(190f).FlexibleWidth(0f);
        _timeLabel = Label(ui, "TimeLabel", "0:00 / 0:00", 15f, DashTheme.TextDim, TextHorizontalAlignment.Right);
        ui.PopStyle();

        ui.NestOut();
        ui.PopStyle();
    }

    private void BuildTransportRow(UIBuilder ui, WorldAudioOutput audio)
    {
        ui.PushStyle();
        RowHeight(ui, 40f);
        ui.HorizontalLayout(6f);

        ui.PushStyle().MinWidth(110f).PreferredWidth(110f).FlexibleWidth(0f);
        _playButton = ui.Button("Play", OnPlayPausePressed, DashTheme.Surface);
        _playButton.Slot.Name.Value = "PlayPause";
        _playLabel = ChildText(_playButton);

        ui.Button("Stop", OnStopPressed, DashTheme.Surface);

        ui.PushStyle().MinWidth(90f).PreferredWidth(90f);
        _loopButton = ui.Button("Loop", OnLoopPressed, DashTheme.Surface);
        _loopButton.Slot.Name.Value = "Loop";
        ui.PopStyle();

        ui.PushStyle().MinWidth(120f).PreferredWidth(120f);
        _spatialButton = ui.Button("3D Audio", OnSpatialPressed, DashTheme.Surface);
        _spatialButton.Slot.Name.Value = "Spatialize";
        ui.PopStyle();
        ui.PopStyle();

        // Volume is heard, not measured: a linear fader spends most of its travel in a range nobody can
        // tell apart, so the slider is squared into the gain and rooted back out of it.
        ui.PushStyle().MinWidth(0f).PreferredWidth(0f).FlexibleWidth(1f);
        float initial = MathF.Sqrt(Clamp01(audio.Volume.Value));
        _volumeSlider = ui.Slider(initial, 0f, 1f, OnVolumeChanged, DashTheme.Field);
        _volumeSlider.Slot.Name.Value = "Volume";
        Metrics(_volumeSlider.Slot, 90f, 90f, 1f, 24f, 24f, 0f);
        ui.PopStyle();

        ui.PushStyle().MinWidth(80f).PreferredWidth(80f).FlexibleWidth(0f);
        ui.Button("-10s", OnSeekBackPressed, DashTheme.Surface);
        ui.Button("+10s", OnSeekForwardPressed, DashTheme.Surface);
        _statsButton = ui.Button("VideoPlayer.Stats".AsLocale("Stats"), OnStatsPressed, DashTheme.Surface);
        _statsButton.Slot.Name.Value = "Stats";
        ui.PopStyle();

        ui.NestOut();
        ui.PopStyle();
    }

    private void BuildSourceRow(UIBuilder ui)
    {
        ui.PushStyle();
        RowHeight(ui, 34f);
        ui.HorizontalLayout(6f);

        ui.PushStyle().MinWidth(0f).PreferredWidth(0f).FlexibleWidth(1f);
        _sourceInput = ui.TextInput(string.Empty, "Paste a video URL or path", null, false, DashTheme.Field);
        // Renaming the FIELD is safe; its child is still called "Text", which is the name TextInput
        // resolves to find the component it drives.
        _sourceInput.Slot.Name.Value = "Source";
        _sourceInput.SetSubmitAction(OnSourceSubmitted);
        ui.PopStyle();

        ui.PushStyle().MinWidth(90f).PreferredWidth(90f).FlexibleWidth(0f);
        ui.Button("Load", OnLoadPressed, DashTheme.Accent);
        ui.PopStyle();

        ui.PushStyle().MinWidth(80f).PreferredWidth(80f).FlexibleWidth(0f);
        ui.Button("Flat", OnFlatPressed, DashTheme.Surface);
        ui.Button("360", OnSphere360Pressed, DashTheme.Surface);
        ui.Button("180", OnSphere180Pressed, DashTheme.Surface);
        ui.PushStyle().MinWidth(100f).PreferredWidth(100f);
        ui.Button("Stereo", OnStereoPressed, DashTheme.Surface);
        ui.PopStyle();
        ui.PopStyle();

        ui.NestOut();
        ui.PopStyle();
    }

    // ACTIONS
    // Method groups on this component, not closures: a Button stores its action as target + method
    // name, so a closure would survive only in this process and a duplicated player would come up dead.

    [SyncMethod]
    public void OnPlayPausePressed(Button button, UIInteractionContext context) => TogglePlayback();

    public void OnStatsPressed(Button button, UIInteractionContext context) => ShowStats.Value = !ShowStats.Value;

    [SyncMethod]
    public void OnStopPressed(Button button, UIInteractionContext context) => Stop();

    [SyncMethod]
    public void OnLoopPressed(Button button, UIInteractionContext context) => Loop = !Loop;

    [SyncMethod]
    public void OnSpatialPressed(Button button, UIInteractionContext context)
    {
        var audio = AudioOutput.Target;
        if (audio == null || audio.IsDestroyed)
            return;
        bool on = !audio.Spatialize.Value;
        audio.Spatialize.Value = on;
        audio.SpatialBlend.Value = on ? 1f : 0f;
    }

    [SyncMethod]
    public void OnSeekBackPressed(Button button, UIInteractionContext context) => SeekBy(-SeekStep);

    [SyncMethod]
    public void OnSeekForwardPressed(Button button, UIInteractionContext context) => SeekBy(SeekStep);

    [SyncMethod]
    public void OnLoadPressed(Button button, UIInteractionContext context)
    {
        if (_sourceInput != null && !_sourceInput.IsDestroyed)
            SetSource(_sourceInput.Text.Value);
    }

    [SyncMethod]
    public void OnSourceSubmitted(TextInput input, string text) => SetSource(text);

    [SyncMethod]
    public void OnVolumeChanged(Slider slider, float value)
    {
        var audio = AudioOutput.Target;
        if (audio == null || audio.IsDestroyed)
            return;
        float t = Clamp01(value);
        audio.Volume.Value = t * t;
    }

    [SyncMethod]
    public void OnFlatPressed(Button button, UIInteractionContext context)
        => Projection.Value = VideoProjectionMode.Flat;

    [SyncMethod]
    public void OnSphere360Pressed(Button button, UIInteractionContext context)
        => Projection.Value = VideoProjectionMode.Sphere360;

    [SyncMethod]
    public void OnSphere180Pressed(Button button, UIInteractionContext context)
        => Projection.Value = VideoProjectionMode.Sphere180;

    [SyncMethod]
    public void OnStereoPressed(Button button, UIInteractionContext context)
    {
        StereoLayout.Value = StereoLayout.Value switch
        {
            StereoLayoutMode.None => StereoLayoutMode.Horizontal_LR,
            StereoLayoutMode.Horizontal_LR => StereoLayoutMode.Horizontal_RL,
            StereoLayoutMode.Horizontal_RL => StereoLayoutMode.Vertical_LR,
            StereoLayoutMode.Vertical_LR => StereoLayoutMode.Vertical_RL,
            _ => StereoLayoutMode.None,
        };
    }

    private void OnSourceEditingFinished(TextInput input, string text) => SetSource(text);

    private void SeekBy(float seconds)
    {
        var video = Provider;
        if (video == null || video.IsDestroyed || video.IsStreaming)
            return;
        float target = video.Position + seconds;
        if (target < 0f)
            target = 0f;
        double length = video.ClipLength;
        if (length > 0.0 && target > (float)length)
            target = (float)length;
        video.Position = target;
    }

    // STEREO

    // Horizontal is unambiguous: U runs left to right on the quad and on the texture. Vertical is the
    // one with a convention in it - V=0 is texture ROW 0 and sits on the quad's TOP edge here, so the
    // LEFT eye of a Vertical_LR frame is the TOP half and takes offset 0, which is the opposite of what
    // a bottom-origin renderer would want. Getting this backwards is not subtle to look at. -xlinka
    public static void ApplyStereoLayout(UnlitMaterial material, StereoLayoutMode layout)
        => ApplyStereoLayout(material, layout, float2.One, float2.Zero);

    // The surface transform (a 180's doubling) goes in first and the eye window is
    // applied on top of it, so a stereo 360 is one composed transform rather than two that fight.
    public static void ApplyStereoLayout(UnlitMaterial material, StereoLayoutMode layout, in float2 baseScale, in float2 baseOffset)
    {
        if (material == null || material.IsDestroyed)
            return;

        material.TextureScale.Value = baseScale;
        material.TextureOffset.Value = baseOffset;

        // Hand-set by whoever authored the material; nothing here should overwrite it.
        if (layout == StereoLayoutMode.Custom)
            return;

        float2 eyeScale = float2.One;
        float2 leftOffset = float2.Zero;
        float2 rightOffset = float2.Zero;
        bool stereo = true;

        switch (layout)
        {
            case StereoLayoutMode.Horizontal_LR:
                eyeScale = new float2(0.5f, 1f);
                leftOffset = float2.Zero;
                rightOffset = new float2(0.5f, 0f);
                break;
            case StereoLayoutMode.Horizontal_RL:
                eyeScale = new float2(0.5f, 1f);
                leftOffset = new float2(0.5f, 0f);
                rightOffset = float2.Zero;
                break;
            case StereoLayoutMode.Vertical_LR:
                eyeScale = new float2(1f, 0.5f);
                leftOffset = float2.Zero;
                rightOffset = new float2(0f, 0.5f);
                break;
            case StereoLayoutMode.Vertical_RL:
                eyeScale = new float2(1f, 0.5f);
                leftOffset = new float2(0f, 0.5f);
                rightOffset = float2.Zero;
                break;
            default:
                stereo = false;
                break;
        }

        material.StereoTextureTransform.Value = stereo;
        material.LeftEyeTextureScale.Value = baseScale * eyeScale;
        material.LeftEyeTextureOffset.Value = baseOffset * eyeScale + leftOffset;
        material.RightEyeTextureScale.Value = baseScale * eyeScale;
        material.RightEyeTextureOffset.Value = baseOffset * eyeScale + rightOffset;
    }

    private void ApplyStereo()
    {
        var layout = StereoLayout.Value;
        if (_displayMaterial != null && !_displayMaterial.IsDestroyed)
            ApplyStereoLayout(_displayMaterial, layout);

        if (_sphereMaterial != null && !_sphereMaterial.IsDestroyed)
        {
            var (scale, offset) = SphereTransform(Projection.Value);
            ApplyStereoLayout(_sphereMaterial, layout, scale, offset);
        }
    }

    // The sphere mesh already lays a picture on the way a globe reads from outside, top row at the north
    // pole, and the orb is looked at from outside, so a 360 frame needs nothing.
    //
    // 180 is an approximation and deliberately so: doubling U squeezes the whole picture into the front
    // half and lets it repeat behind you, which is right where you are looking and wrong where you are
    // not. A real half-dome mesh is the honest fix and is not this component's job.
    private static (float2 scale, float2 offset) SphereTransform(VideoProjectionMode projection)
        => projection == VideoProjectionMode.Sphere180
            ? (new float2(2f, 1f), new float2(-1f, 0f))
            : (float2.One, float2.Zero);

    // The screen stays up in every mode; the orb joins it for 360 and 180 and stereo only changes
    // what both of them show.
    private void ApplyProjection()
    {
        bool flat = Projection.Value == VideoProjectionMode.Flat;
        SetActive(_sphereSlot, !flat);
    }

    // PER-FRAME

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();

        if (!_wired)
            Wire();

        var video = Provider;
        if (video == null || video.IsDestroyed)
            return;

        UpdateSurfaces(video);

        float delta = World?.Time?.Delta ?? 0f;
        bool showStats = ShowStats.Value;
        if (_statsSlot != null && !_statsSlot.IsDestroyed && _statsSlot.ActiveSelf.Value != showStats)
        {
            SetActive(_statsSlot, showStats);
            _lastDisplaySize = new float2(-1f, -1f);
        }
        if (_chatSlot == null || _chatSlot.IsDestroyed)
        {
            _chatSlot = Slot.FindChild("Chat", recursive: false);
            if (_chatSlot != null)
                _lastDisplaySize = new float2(-1f, -1f);
        }
        SetActive(_chatSlot, ShowChat.Value);

        _labelPoll -= delta;
        if (_labelPoll > 0f)
            return;
        _labelPoll = 0.1f;
        UpdateLabels(video);
        // The source can arrive by any path (a paste, a saved item, a peer's edit), so the host keeps
        // the chat in step with the URL rather than trusting one entry point to have done it.
        if (World != null && World.IsAuthority)
            SyncChat(video.URL.Value);
    }

    // Sizing runs off the video's own dimensions, so it is computed locally on every peer from a number
    // they all read out of the same file, and written silently - a replicated quad size would be four
    // machines posting the same answer at each other.
    private void UpdateSurfaces(VideoTextureProvider video)
    {
        if (_displayMesh == null || _displayMesh.IsDestroyed)
            return;

        var asset = video.Asset;
        bool ready = asset != null && asset.IsReady;
        int videoWidth = ready ? asset!.VideoWidth : 0;
        int videoHeight = ready ? asset!.VideoHeight : 0;
        bool hasPicture = videoWidth > 0 && videoHeight > 0;

        float aspect = hasPicture ? (float)videoHeight / videoWidth : DefaultAspect;
        float width = DisplayWidth.Value;
        if (width < 0.05f)
            width = 0.05f;

        var compensation = SizeCompensation.Value;
        var size = new float2(width * compensation.x, width * aspect * compensation.y);
        SetActive(_audioOnlySlot, ready && !hasPicture);

        if (size == _lastDisplaySize)
            return;
        _lastDisplaySize = size;

        _displayMesh.Size.SetValueSilently(size);

        if (_displayCollider != null && !_displayCollider.IsDestroyed)
            _displayCollider.Size.SetValueSilently(new float3(size.x, size.y, 0.02f));

        if (_frameMesh != null && !_frameMesh.IsDestroyed)
            _frameMesh.Size.SetValueSilently(new float2(size.x + FrameMargin * 2f, size.y + FrameMargin * 2f));

        if (_chatSlot != null && !_chatSlot.IsDestroyed)
        {
            float scale = size.y / TwitchChatPanel.PanelHeight;
            float chatWidth = TwitchChatPanel.PanelWidth * scale;
            float yaw = ChatYawDegrees * (MathF.PI / 180f);
            float half = chatWidth * 0.5f;
            _chatSlot.LocalScale.SetValueSilently(new float3(scale, scale, scale), change: true);
            _chatSlot.LocalRotation.SetValueSilently(floatQ.AxisAngle(float3.Up, yaw), change: true);
            // Turned about its own middle, so the inner edge is pulled back to the screen's plane and
            // the outer edge comes forward by the same amount.
            _chatSlot.LocalPosition.SetValueSilently(
                new float3(-(size.x * 0.5f + FrameMargin + ChatGap + half * MathF.Cos(yaw)), 0f, half * MathF.Sin(yaw)), change: true);
        }

        float rightEdge = size.x * 0.5f + FrameMargin;
        if (_statsSlot != null && !_statsSlot.IsDestroyed)
        {
            float scale = size.x / CanvasWidth;
            float yaw = ChatYawDegrees * (MathF.PI / 180f);
            float half = VideoStatsPanel.PanelWidth * scale * 0.5f;
            _statsSlot.LocalScale.SetValueSilently(new float3(scale, scale, scale), change: true);
            _statsSlot.LocalRotation.SetValueSilently(floatQ.AxisAngle(float3.Up, -yaw), change: true);
            _statsSlot.LocalPosition.SetValueSilently(
                new float3(rightEdge + ChatGap + half * MathF.Cos(yaw), size.y * 0.5f - VideoStatsPanel.PanelHeight * scale * 0.5f - FrameMargin, half * MathF.Sin(yaw)), change: true);
            if (ShowStats.Value)
                rightEdge += ChatGap + VideoStatsPanel.PanelWidth * scale * MathF.Cos(yaw) + ControlsFrameMargin * scale;
        }

        if (_liveBadgeSlot != null && !_liveBadgeSlot.IsDestroyed)
        {
            float scale = size.x / CanvasWidth;
            float badgeWidth = LiveBadgeWidth * scale;
            float badgeHeight = LiveBadgeHeight * scale;
            float margin = LiveBadgeMargin * scale;
            _liveBadgeSlot.LocalScale.SetValueSilently(new float3(scale, scale, scale), change: true);
            _liveBadgeSlot.LocalPosition.SetValueSilently(
                new float3(size.x * 0.5f - badgeWidth * 0.5f - margin, size.y * 0.5f - badgeHeight * 0.5f - margin, 0.003f), change: true);
        }

        if (_sphereSlot != null && !_sphereSlot.IsDestroyed)
        {
            float radius = size.y * OrbHeightRatio * 0.5f;
            if (_sphereMesh != null && !_sphereMesh.IsDestroyed)
                _sphereMesh.Radius.SetValueSilently(radius);
            _sphereSlot.LocalPosition.SetValueSilently(
                new float3(rightEdge + OrbGap + radius, 0f, 0f), change: true);
        }

        if (_controlsSlot != null && !_controlsSlot.IsDestroyed)
        {
            float scale = size.x / CanvasWidth;
            _controlsSlot.LocalScale.SetValueSilently(new float3(scale, scale, scale), change: true);
            float controlsHeight = CanvasHeight * scale;
            float tilt = ControlsTiltDegrees * (MathF.PI / 180f);
            float sin = MathF.Sin(tilt);
            float cos = MathF.Cos(tilt);
            float half = controlsHeight * 0.5f;
            // Pitched about the top edge, which stays on the screen's plane one gap below it.
            var controlsPosition = new float3(0f, -(size.y * 0.5f + ControlsGap + half * cos), half * sin);
            var controlsRotation = floatQ.AxisAngle(new float3(1f, 0f, 0f), -tilt);
            _controlsSlot.LocalPosition.SetValueSilently(controlsPosition, change: true);
            _controlsSlot.LocalRotation.SetValueSilently(controlsRotation, change: true);
            if (_controlsFrameSlot != null && !_controlsFrameSlot.IsDestroyed)
            {
                var normal = new float3(0f, sin, cos);
                _controlsFrameSlot.LocalScale.SetValueSilently(new float3(scale, scale, scale), change: true);
                _controlsFrameSlot.LocalRotation.SetValueSilently(controlsRotation, change: true);
                _controlsFrameSlot.LocalPosition.SetValueSilently(controlsPosition - normal * FrameDepth, change: true);
            }
        }
    }

    private void UpdateLabels(VideoTextureProvider video)
    {
        bool streaming = video.IsStreaming;
        double length = video.ClipLength;

        SetText(_titleLabel, TitleFor(video));
        SetText(_engineLabel, video.CurrentPlaybackEngine.Value);
        SetText(_liveBadge, string.Empty);
        SetActive(_liveBadgeSlot, streaming);

        string error = video.LoadError.Value;
        if (!string.IsNullOrEmpty(error))
        {
            SetText(_statusLabel, error);
            SetColor(_statusLabel, DashTheme.Negative);
        }
        else if (video.IsLoadPending)
        {
            SetText(_statusLabel, "LOADING...");
            SetColor(_statusLabel, DashTheme.TextDim);
        }
        else
        {
            SetText(_statusLabel, string.Empty);
        }

        SetActive(_timeline?.Slot, !streaming);
        SetText(_timeLabel, streaming
            ? "LIVE"
            : $"{FormatTime(video.Position)} / {FormatTime(length > 0.0 ? length : 0.0)}");

        SetText(_playLabel, video.IsPlaying ? "Pause" : "Play");

        var audio = AudioOutput.Target;
        bool loop = video.Loop;
        bool spatial = audio != null && !audio.IsDestroyed && audio.Spatialize.Value;
        bool stats = ShowStats.Value;
        if (!_toggleStateKnown || loop != _lastLoop || spatial != _lastSpatialize || stats != _lastStats)
        {
            _toggleStateKnown = true;
            _lastLoop = loop;
            _lastSpatialize = spatial;
            _lastStats = stats;
            Highlight(_loopButton, loop);
            Highlight(_spatialButton, spatial);
            Highlight(_statsButton, stats);
        }

        if (_volumeSlider != null && !_volumeSlider.IsDestroyed && audio != null && !audio.IsDestroyed)
        {
            float wanted = MathF.Sqrt(Clamp01(audio.Volume.Value));
            if (MathF.Abs(wanted - _volumeSlider.Value.Value) > 0.01f)
                _volumeSlider.Value.SetValueSilently(wanted);
        }
    }

    private static string TitleFor(VideoTextureProvider video)
    {
        string title = video.VideoTitle.Value;
        if (!string.IsNullOrEmpty(title))
            return title;

        var url = video.URL.Value;
        if (url == null)
            return "No video";
        if (url.IsAbsoluteUri && !url.IsFile && !string.IsNullOrEmpty(url.Host))
            return url.Host;

        string name = System.IO.Path.GetFileName(url.IsAbsoluteUri && url.IsFile ? url.LocalPath : url.OriginalString);
        return string.IsNullOrEmpty(name) ? url.OriginalString : name;
    }

    // RECONNECT

    // Everything here is a lookup, never a create: this runs on a peer that RECEIVED the item, and a
    // build on that side would give the world two of every part.
    private void Wire()
    {
        if (_wired || Slot == null || Slot.IsDestroyed)
            return;

        _displaySlot = Slot.FindChild("Display", recursive: false);
        _frameSlot = Slot.FindChild("Frame", recursive: false);
        _sphereSlot = Slot.FindChild("Sphere", recursive: false);
        _controlsSlot = Slot.FindChild("Controls", recursive: false);
        _controlsFrameSlot = Slot.FindChild("ControlsFrame", recursive: false);
        // The panel arrives a slot at a time, so latching on the canvas alone would cache a screenful
        // of nulls and leave that peer's labels frozen for good. Wait for the rows.
        var content = _controlsSlot?.FindChild("Content", recursive: false);
        if (_displaySlot == null || _controlsSlot == null || content == null || content.ChildCount == 0)
            return;

        _wired = true;
        _built = true;

        _displayMesh = _displaySlot.GetComponent<QuadMesh>();
        _displayCollider = _displaySlot.GetComponent<BoxCollider>();
        _displayMaterial = _displaySlot.GetComponent<UnlitMaterial>();
        _audioOnlySlot = _displaySlot.FindChild("AudioOnly", recursive: false);
        _liveBadgeSlot = Slot.FindChild("LiveBadge", recursive: false);
        _statsSlot = Slot.FindChild("Stats", recursive: false);
        _titleFont = Slot.FindChild("Font", recursive: false)?.FindChild("FontBold", recursive: false)?.GetComponent<FontProvider>();
        _frameMesh = _frameSlot?.GetComponent<RoundedQuadMesh>();
        _sphereMesh = _sphereSlot?.GetComponent<SphereMesh>();
        _sphereMaterial = _sphereSlot?.GetComponent<UnlitMaterial>();
        _chatSlot = Slot.FindChild("Chat", recursive: false);
        _chat = _chatSlot?.GetComponent<TwitchChat>();
        _chatPanel = _chatSlot?.GetComponent<TwitchChatPanel>();
        _font = Slot.FindChild("Font", recursive: false)?.GetComponent<FontProvider>();

        _titleLabel = FindText("TitleLabel");
        _engineLabel = FindText("EngineLabel");
        _liveBadge = FindText("LiveBadge");
        _statusLabel = FindText("StatusLabel");
        _timeLabel = FindText("TimeLabel");

        _timeline = _controlsSlot.FindChild("Timeline", recursive: true)?.GetComponent<Slider>();
        _volumeSlider = _controlsSlot.FindChild("Volume", recursive: true)?.GetComponent<Slider>();
        _sourceInput = _controlsSlot.FindChild("Source", recursive: true)?.GetComponent<TextInput>();
        _playButton = FindButton("PlayPause");
        _loopButton = FindButton("Loop");
        _spatialButton = FindButton("Spatialize");
        _statsButton = FindButton("Stats");
        _playLabel = ChildText(_playButton);

        // A local event, bound per peer, because the peer doing the typing is the only one that has
        // anything to finish editing.
        if (!_subscribed && _sourceInput != null && !_sourceInput.IsDestroyed)
        {
            _sourceInput.EditingFinished += OnSourceEditingFinished;
            _subscribed = true;
        }
    }

    private Text? FindText(string name)
        => _controlsSlot?.FindChild(name, recursive: true)?.GetComponent<Text>();

    private Button? FindButton(string name)
        => _controlsSlot?.FindChild(name, recursive: true)?.GetComponent<Button>();

    // HELPERS

    // The slot name is load-bearing: a peer that received this item finds its labels by these names,
    // and the builder would otherwise hand every one of them the same "Text".
    private Text Label(UIBuilder ui, string name, string content, float size, in color tint, TextHorizontalAlignment alignment)
    {
        var text = ui.Text(content, size, tint);
        text.Slot.Name.Value = name;
        text.HorizontalAlignment.Value = alignment;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        text.WordWrap.Value = false;
        return text;
    }

    private static Text? ChildText(Button? button)
        => button == null || button.IsDestroyed
            ? null
            : button.Slot?.FindChild("Text", recursive: false)?.GetComponent<Text>();

    private static void RowHeight(UIBuilder ui, float height)
        => ui.MinHeight(height).PreferredHeight(height).FlexibleHeight(0f);

    private static void Metrics(Slot slot, float minWidth, float preferredWidth, float flexibleWidth,
        float minHeight, float preferredHeight, float flexibleHeight)
    {
        var element = slot.GetComponent<LayoutElement>() ?? slot.AttachComponent<LayoutElement>();
        element.MinWidth.Value = minWidth;
        element.PreferredWidth.Value = preferredWidth;
        element.FlexibleWidth.Value = flexibleWidth;
        element.MinHeight.Value = minHeight;
        element.PreferredHeight.Value = preferredHeight;
        element.FlexibleHeight.Value = flexibleHeight;
    }

    private static void FillRect(Slot slot)
    {
        var rect = slot.GetComponent<RectTransform>() ?? slot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }

    private static void SetActive(Slot? slot, bool active)
    {
        if (slot == null || slot.IsDestroyed)
            return;
        if (slot.ActiveSelf.Value != active)
            slot.ActiveSelf.SetValueSilently(active);
    }

    private static void SetText(Text? text, string content)
    {
        if (text == null || text.IsDestroyed)
            return;
        if (!string.Equals(text.Content.Value, content, StringComparison.Ordinal))
            text.Content.SetValueSilently(content);
    }

    private static void SetColor(Text? text, in color tint)
    {
        if (text == null || text.IsDestroyed)
            return;
        text.Color.SetValueSilently(tint);
    }

    // Local-only, like everything else the panel paints: the state behind it already replicates, so
    // every peer arrives at the same colour without anyone broadcasting it.
    private static void Highlight(Button? button, bool on)
    {
        if (button == null || button.IsDestroyed)
            return;
        color normal = on ? DashTheme.Accent : DashTheme.Surface;
        color hover = on ? DashTheme.AccentHover : DashTheme.SurfaceHover;
        color pressed = on ? DashTheme.AccentPressed : DashTheme.SurfacePressed;
        foreach (var driver in button.ColorDrivers)
        {
            if (driver == null || driver.IsDestroyed)
                continue;
            driver.NormalColor.SetValueSilently(normal);
            driver.HighlightColor.SetValueSilently(hover);
            driver.PressedColor.SetValueSilently(pressed);
            driver.DisabledColor.SetValueSilently(DashTheme.Field);
            driver.Apply();
        }
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

    private static colorHDR ToHDR(in color value) => new colorHDR(value.r, value.g, value.b, value.a);

    private static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
}
