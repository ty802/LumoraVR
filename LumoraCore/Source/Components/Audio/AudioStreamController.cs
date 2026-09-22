// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Lumora.Core.Assets;
using Lumora.Core.Audio;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;
using Lumora.Core.Networking.Streams;
using GroupKind = Lumora.Core.Audio.AudioTypeGroup;

namespace Lumora.Core.Components.Audio;

// An input device streamed into the world as an object you can pick up and put down. The stream is the
// speaker's (only a stream's owner may create one), the item is ordinary world content, and the panel
// is the handle everybody in the room gets on it.
//
// The capture is deliberately NOT the voice path: it takes the raw device samples, because a noise gate
// and a normalizer are shaped for speech and would eat the quiet half of anything else. -xlinka
[ComponentCategory("Audio")]
public sealed class AudioStreamController : Component
{
    public readonly SyncRef<AudioStream> Stream;

    public readonly SyncRef<Lumora.Core.Components.Audio.AudioOutput> AudioOutput;

    // The adapter between the two: an AudioOutput points at a component, and a stream is not one.
    public readonly SyncRef<UserAudioStream> Source;

    public readonly Sync<float> PanelWidth;

    // Which device is going out. Replicated, because everybody in the room should be able to see what
    // they are listening to rather than only the person who started it.
    public readonly Sync<string> DeviceName;

    private const float CanvasWidth = 900f;
    private const float CanvasHeight = 160f;

    private const float PlateMargin = 0.012f;
    private const float PlateDepth = 0.005f;

    // How fast the meter falls. Instant on the way up, because a peak you missed is a peak you cannot
    // see; slow on the way down, because a meter that snaps back reads as a strobe rather than a level.
    private const float MeterRelease = 6f;

    private Slot? _plateSlot;
    private Slot? _controlsSlot;

    private RoundedQuadMesh? _plateMesh;
    private BoxCollider? _plateCollider;
    private FontProvider? _font;

    private Text? _titleLabel;
    private Text? _statusLabel;
    private Slot? _meterFill;
    private Slider? _volumeSlider;
    private Button? _spatialButton;
    private Button? _playForMeButton;

    private float _meterLevel;
    private float _labelPoll;
    private float _lastPanelWidth = -1f;
    private Slot? _waveformSlot;
    private Meshes.AudioWaveformMesh? _waveformMesh;
    private bool _wired;
    private bool _toggleStateKnown;
    private bool _lastSpatial;
    private bool _lastPlayForMe;

    public AudioStreamController()
    {
        Stream = new SyncRef<AudioStream>(this);
        AudioOutput = new SyncRef<Lumora.Core.Components.Audio.AudioOutput>(this);
        Source = new SyncRef<UserAudioStream>(this);
        PanelWidth = new Sync<float>(this, 0.6f);
        DeviceName = new Sync<string>(this, string.Empty);
    }

    public User? Owner => Stream.Target?.Owner;

    public bool IsOwnedByLocalUser => Owner != null && ReferenceEquals(Owner, World?.LocalUser);

    // BUILD

    // Everything the item is, built under one slot. Called once by whoever spawned it; every other peer
    // receives the result and only LOOKS the parts up (see Wire).
    public void BuildUI(Slot root)
    {
        if (root == null || root.IsDestroyed || World == null)
            return;

        var source = Source.Target;
        if (source == null || source.IsDestroyed)
        {
            source = root.GetComponent<UserAudioStream>() ?? root.AttachComponent<UserAudioStream>();
            Source.Target = source;
        }
        if (Stream.Target != null && source.Stream.Target != Stream.Target)
            source.Stream.Target = Stream.Target;

        var output = AudioOutput.Target;
        if (output == null || output.IsDestroyed)
        {
            output = root.GetComponent<Lumora.Core.Components.Audio.AudioOutput>()
                     ?? root.AttachComponent<Lumora.Core.Components.Audio.AudioOutput>();
            AudioOutput.Target = output;
        }

        output.Source.Target = source;
        output.AudioTypeGroup.Value = GroupKind.Multimedia;
        output.Spatialize.Value = true;
        output.SpatialBlend.Value = 1f;
        output.MinDistance.Value = 1f;
        output.MaxDistance.Value = 25f;
        // An item somebody carries across the room must not pitch-shift while they walk.
        output.DopplerLevel.Value = 0f;

        BuildFont(root);
        BuildPlate(root);
        BuildControls(root, output);
        BuildWaveform(root, output);

        _wired = true;
        UpdateSurfaces();
    }

    private void BuildFont(Slot root)
    {
        var fontSlot = root.FindChildOrAdd("Font");
        _font = fontSlot.GetOrAttachComponent<FontProvider>();
        // Text with no font draws nothing at all, so a face is picked here rather than left to whatever
        // the world happens to have registered.
        var url = ImportDialog.ResolveFontUrl(World) ?? DashTheme.FontRegular;
        _font.URL.Value = url;
        if (_font.FallbackURLs.Count == 0)
            _font.FallbackURLs.Add(DashTheme.FontRegular);
    }

    private void BuildPlate(Slot root)
    {
        _plateSlot = root.FindChildOrAdd("Plate");
        _plateSlot.LocalPosition.Value = new float3(0f, 0f, -PlateDepth);
        _plateMesh = _plateSlot.GetOrAttachComponent<RoundedQuadMesh>();
        _plateMesh.CornerRadius.Value = 0.02f;
        _plateMesh.CornerSegments.Value = 4;
        _plateMesh.DualSided.Value = false;

        // Glass, like every other panel body. An opaque slab reads as a placeholder next to them.
        var material = DashGlass.Attach(_plateSlot);

        var renderer = _plateSlot.GetOrAttachComponent<MeshRenderer>();
        renderer.Mesh.Target = _plateMesh;
        renderer.Material.Target = material;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;

        _plateCollider = _plateSlot.GetOrAttachComponent<BoxCollider>();
    }

    // The same live trace the audio player wears, for the same reason: a stream that is running and a
    // stream that has gone quiet look identical otherwise, and the level meter only says how loud.
    private void BuildWaveform(Slot root, Lumora.Core.Components.Audio.AudioOutput output)
    {
        _waveformSlot = root.FindChildOrAdd("Waveform");
        _waveformSlot.LocalPosition.Value = new float3(0f, 0f, -PlateDepth * 0.4f);

        _waveformMesh = _waveformSlot.GetOrAttachComponent<Meshes.AudioWaveformMesh>();
        _waveformMesh.Source.Target = output;
        _waveformMesh.Style.Value = Meshes.WaveformStyle.Spectrum;
        _waveformMesh.Resolution.Value = 96;
        _waveformMesh.Smoothing.Value = 0.4f;

        var material = _waveformSlot.GetOrAttachComponent<UnlitMaterial>();
        material.TintColor.Value = ToHDR(DashTheme.Accent);
        material.Culling.Value = Culling.None;
        material.BlendMode.Value = BlendMode.Alpha;

        var renderer = _waveformSlot.GetOrAttachComponent<MeshRenderer>();
        renderer.Mesh.Target = _waveformMesh;
        renderer.Material.Target = material;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
    }

    private void BuildControls(Slot root, Lumora.Core.Components.Audio.AudioOutput output)
    {
        _controlsSlot = root.FindChildOrAdd("Controls");
        var rect = _controlsSlot.GetOrAttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(CanvasWidth * -0.5f, CanvasHeight * -0.5f);
        rect.OffsetMax.Value = new float2(CanvasWidth * 0.5f, CanvasHeight * 0.5f);
        _controlsSlot.GetOrAttachComponent<Canvas>();

        // THE HOUSE BACKDROP, not a flat image: rounded, glass-tinted, outlined, the same one every
        // other panel in the product wears.
        var background = _controlsSlot.GetComponent<RoundedPanel>() ?? _controlsSlot.AttachComponent<RoundedPanel>();
        background.Color.Value = DashTheme.GlassBackdrop;
        background.CornerRadius.Value = DashTheme.RadiusPanel;
        background.OutlineColor.Value = DashTheme.OutlineStrong;
        background.OutlineThickness.Value = DashTheme.OutlineWidth;

        var content = _controlsSlot.FindChildOrAdd("Content");
        FillRect(content);
        // ITS OWN CHUNK, and this is what made the panel a black rectangle with nothing on it. The
        // canvas root carries the backdrop; without a chunk of its own the content is batched into
        // the root's chunk and drawn underneath the very thing it is supposed to sit on, so every
        // button, label and meter went behind an opaque panel. The video player hit this and fixed it
        // there; these two were built from the pattern that predates it. -xlinka
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
        BuildMeterRow(ui, output);
        BuildButtonRow(ui);

        ui.NestOut();
    }

    private void BuildTitleRow(UIBuilder ui)
    {
        ui.PushStyle();
        RowHeight(ui, 28f);
        ui.HorizontalLayout(8f);

        ui.PushStyle().MinWidth(0f).PreferredWidth(0f).FlexibleWidth(1f);
        _titleLabel = Label(ui, "TitleLabel", "Audio Stream", 20f, DashTheme.Text, TextHorizontalAlignment.Left);
        ui.PopStyle();

        ui.PushStyle().MinWidth(300f).PreferredWidth(300f).FlexibleWidth(0f);
        _statusLabel = Label(ui, "StatusLabel", string.Empty, 14f, DashTheme.TextDim, TextHorizontalAlignment.Right);
        ui.PopStyle();

        ui.NestOut();
        ui.PopStyle();
    }

    // The meter is a track with a fill inside it, and the fill is sized by its anchor rather than by a
    // layout: a layout rebuild every frame would re-tessellate the whole row for one rectangle.
    private void BuildMeterRow(UIBuilder ui, Lumora.Core.Components.Audio.AudioOutput output)
    {
        ui.PushStyle();
        RowHeight(ui, 26f);
        ui.HorizontalLayout(10f);

        ui.PushStyle().MinWidth(0f).PreferredWidth(0f).FlexibleWidth(1f);
        var track = ui.Panel(DashTheme.Field);
        track.Slot.Name.Value = "Meter";
        Metrics(track.Slot, 0f, 0f, 1f, 18f, 18f, 0f);

        var fill = track.Slot.AddSlot("Fill");
        var fillRect = fill.AttachComponent<RectTransform>();
        fillRect.AnchorMin.Value = float2.Zero;
        fillRect.AnchorMax.Value = new float2(0f, 1f);
        fillRect.OffsetMin.Value = float2.Zero;
        fillRect.OffsetMax.Value = float2.Zero;
        var fillImage = fill.AttachComponent<Image>();
        fillImage.Tint.Value = DashTheme.Accent;
        _meterFill = fill;
        ui.PopStyle();

        ui.PushStyle().MinWidth(260f).PreferredWidth(260f).FlexibleWidth(0f);
        float initial = MathF.Sqrt(Clamp01(output.Volume.Value));
        _volumeSlider = ui.Slider(initial, 0f, 1f, OnVolumeChanged, DashTheme.Field);
        _volumeSlider.Slot.Name.Value = "Volume";
        Metrics(_volumeSlider.Slot, 80f, 80f, 1f, 22f, 22f, 0f);
        ui.PopStyle();

        ui.NestOut();
        ui.PopStyle();
    }

    private void BuildButtonRow(UIBuilder ui)
    {
        ui.PushStyle();
        RowHeight(ui, 38f);
        ui.HorizontalLayout(6f);

        ui.PushStyle().MinWidth(160f).PreferredWidth(160f).FlexibleWidth(0f);
        _spatialButton = ui.Button("Spatialized", OnSpatialPressed, DashTheme.Surface);
        _spatialButton.Slot.Name.Value = "Spatialize";

        _playForMeButton = ui.Button("Play for me", OnPlayForMePressed, DashTheme.Surface);
        _playForMeButton.Slot.Name.Value = "PlayForMe";
        ui.PopStyle();

        ui.Spacer();

        ui.NestOut();
        ui.PopStyle();
    }

    // ACTIONS
    // Method groups on this component, not closures: a Button stores its action as target + method name,
    // so a closure would survive only in this process and a duplicated item would come up dead.

    [SyncMethod]
    public void OnVolumeChanged(Slider slider, float value)
    {
        var output = AudioOutput.Target;
        if (output == null || output.IsDestroyed)
            return;
        // Volume is heard, not measured: a linear fader spends most of its travel in a range nobody can
        // tell apart, so the slider is squared into the gain and rooted back out of it.
        float t = Clamp01(value);
        output.Volume.Value = t * t;
    }

    [SyncMethod]
    public void OnSpatialPressed(Button button, UIInteractionContext context)
    {
        var output = AudioOutput.Target;
        if (output == null || output.IsDestroyed)
            return;
        bool on = !output.Spatialize.Value;
        output.Spatialize.Value = on;
        output.SpatialBlend.Value = on ? 1f : 0f;
        output.Global.Value = !on;
    }

    // Per person, not per item. Nothing about this leaves the machine it was pressed on.
    [SyncMethod]
    public void OnPlayForMePressed(Button button, UIInteractionContext context)
        => _playForMe = !PlayForMe;

    // Null is the default answer rather than a stored one: whoever spawned this is already hearing the
    // device out of their own speakers, so playing it back to them is a feedback loop with a delay on
    // it. Everybody else is here to listen.
    private bool? _playForMe;

    public bool PlayForMe => _playForMe ?? !IsOwnedByLocalUser;

    // Local exclusion, not the replicated list: whether I hear this is mine to decide, and writing it
    // into the item would decide it for everybody and be a write to somebody else's object besides.
    private void ExcludeLocalListeners(Lumora.Core.Components.Audio.AudioOutput output, bool excluded)
    {
        var manager = World?.Audio;
        if (manager == null)
            return;

        _listeners.Clear();
        manager.CollectLocalUserListeners(_listeners);
        for (int i = 0; i < _listeners.Count; i++)
            output.SetLocallyExcluded(_listeners[i], excluded);
        _listeners.Clear();
    }

    private static readonly System.Collections.Generic.List<Lumora.Core.Components.Audio.AudioListener> _listeners = new();

    // PER-FRAME

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();

        if (!_wired)
            Wire();

        UpdateSurfaces();
        UpdateMeter();

        // Re-asserted every frame rather than set once at build: a listener can appear late (the local
        // user's head is built after their root) or be replaced, and a one-shot exclusion would miss it.
        var output = AudioOutput.Target;
        if (output != null && !output.IsDestroyed)
            ExcludeLocalListeners(output, !PlayForMe);

        _labelPoll -= World?.Time?.Delta ?? 0f;
        if (_labelPoll > 0f)
            return;
        _labelPoll = 0.1f;
        UpdateLabels();
    }

    private void UpdateMeter()
    {
        var fill = _meterFill;
        if (fill == null || fill.IsDestroyed)
            return;

        float peak = Clamp01(Stream.Target?.LastPeak ?? 0f);
        float delta = World?.Time?.Delta ?? 0f;

        if (peak >= _meterLevel)
            _meterLevel = peak;
        else
            _meterLevel = System.Math.Max(peak, _meterLevel - MeterRelease * delta);

        var rect = fill.GetComponent<RectTransform>();
        if (rect == null || rect.IsDestroyed)
            return;

        // Local, silent, every frame: a level meter is a picture of what this machine already has and
        // nobody else needs a delta per frame telling them how loud it was here.
        float wanted = Clamp01(_meterLevel);
        var anchor = rect.AnchorMax.Value;
        if (MathF.Abs(anchor.x - wanted) > 0.002f)
            rect.AnchorMax.SetValueSilently(new float2(wanted, 1f), change: true);
    }

    private void UpdateSurfaces()
    {
        float width = PanelWidth.Value;
        if (width < 0.05f)
            width = 0.05f;
        if (width == _lastPanelWidth)
            return;
        _lastPanelWidth = width;

        float scale = width / CanvasWidth;
        float height = CanvasHeight * scale;

        if (_controlsSlot != null && !_controlsSlot.IsDestroyed)
            _controlsSlot.LocalScale.SetValueSilently(new float3(scale, scale, scale), change: true);

        if (_plateMesh != null && !_plateMesh.IsDestroyed)
            _plateMesh.Size.SetValueSilently(new float2(width + PlateMargin * 2f, height + PlateMargin * 2f));

        if (_plateCollider != null && !_plateCollider.IsDestroyed)
            _plateCollider.Size.SetValueSilently(new float3(width + PlateMargin * 2f, height + PlateMargin * 2f, 0.02f));

        if (_waveformMesh != null && !_waveformMesh.IsDestroyed)
        {
            _waveformMesh.Width.SetValueSilently(width * 0.94f);
            _waveformMesh.Height.SetValueSilently(height * 0.40f);
            _waveformMesh.Thickness.SetValueSilently(System.Math.Max(height * 0.012f, 0.0015f));
        }
    }

    private void UpdateLabels()
    {
        var owner = Owner;
        SetText(_titleLabel, owner == null ? "Audio Stream" : $"{owner.UserName.Value}'s Stream");

        var stream = Stream.Target;
        string device = DeviceName.Value ?? string.Empty;
        if (stream is OpusStream opus)
        {
            string channels = opus.Channels.Value == 2 ? "stereo" : "mono";
            string detail = $"{opus.BitRate.Value / 1000} kbps {channels}";
            // The device is the useful half of this line: two streams in a room are told apart by what
            // they are carrying, not by their bitrate.
            SetText(_statusLabel, device.Length > 0 ? $"{device} - {detail}" : detail);
        }
        else
        {
            SetText(_statusLabel, string.Empty);
        }

        var output = AudioOutput.Target;
        bool spatial = output != null && !output.IsDestroyed && output.Spatialize.Value;
        bool playForMe = PlayForMe;
        if (!_toggleStateKnown || spatial != _lastSpatial || playForMe != _lastPlayForMe)
        {
            _toggleStateKnown = true;
            _lastSpatial = spatial;
            _lastPlayForMe = playForMe;
            Highlight(_spatialButton, spatial);
            Highlight(_playForMeButton, playForMe);
            SetText(ChildText(_spatialButton), spatial ? "Spatialized" : "Broadcast");
        }

        if (_volumeSlider != null && !_volumeSlider.IsDestroyed && output != null && !output.IsDestroyed)
        {
            float wanted = MathF.Sqrt(Clamp01(output.Volume.Value));
            if (MathF.Abs(wanted - _volumeSlider.Value.Value) > 0.01f)
                _volumeSlider.Value.SetValueSilently(wanted);
        }
    }

    // RECONNECT
    // Lookups only: this runs on a peer that RECEIVED the item, and a build on that side would give the
    // world two of every part.
    private void Wire()
    {
        if (_wired || Slot == null || Slot.IsDestroyed)
            return;

        _plateSlot = Slot.FindChild("Plate", recursive: false);
        _controlsSlot = Slot.FindChild("Controls", recursive: false);
        var content = _controlsSlot?.FindChild("Content", recursive: false);
        if (_controlsSlot == null || content == null || content.ChildCount == 0)
            return;

        _wired = true;

        _plateMesh = _plateSlot?.GetComponent<RoundedQuadMesh>();
        _plateCollider = _plateSlot?.GetComponent<BoxCollider>();
        _waveformSlot = Slot.FindChild("Waveform", recursive: false);
        _waveformMesh = _waveformSlot?.GetComponent<Meshes.AudioWaveformMesh>();
        _font = Slot.FindChild("Font", recursive: false)?.GetComponent<FontProvider>();

        _titleLabel = FindText("TitleLabel");
        _statusLabel = FindText("StatusLabel");
        _meterFill = _controlsSlot.FindChild("Meter", recursive: true)?.FindChild("Fill", recursive: false);
        _volumeSlider = _controlsSlot.FindChild("Volume", recursive: true)?.GetComponent<Slider>();
        _spatialButton = FindButton("Spatialize");
        _playForMeButton = FindButton("PlayForMe");
    }

    private Text? FindText(string name)
        => _controlsSlot?.FindChild(name, recursive: true)?.GetComponent<Text>();

    private Button? FindButton(string name)
        => _controlsSlot?.FindChild(name, recursive: true)?.GetComponent<Button>();

    // HELPERS

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

    private static void SetText(Text? text, string content)
    {
        if (text == null || text.IsDestroyed)
            return;
        if (!string.Equals(text.Content.Value, content, StringComparison.Ordinal))
            text.Content.SetValueSilently(content);
    }

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

    private static colorHDR ToHDR(in color value) => new colorHDR(value.r, value.g, value.b, value.a);

    private static float Clamp01(float value)
        => float.IsNaN(value) ? 0f : (value < 0f ? 0f : (value > 1f ? 1f : value));
}
