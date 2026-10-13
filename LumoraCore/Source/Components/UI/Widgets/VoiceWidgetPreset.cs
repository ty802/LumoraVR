// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Helio.UI.Layout;
using Helio.UI.Listing;
using Lumora.Core.Audio;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// The voice strip on the dash's top bar: mute and the four ranges you can talk at, one row, one
// glance from every screen. The active range is lit in its own colour; mute is lit red; a range the
// host does not allow is greyed. -xlinka
[ComponentCategory("Hidden")]
public sealed class VoiceWidgetPreset : HomeWidgetPreset
{
    private const float Inset = 4f;
    private const float Gap = 4f;

    private static readonly VoiceMode[] Modes = { VoiceMode.Whisper, VoiceMode.Normal, VoiceMode.Shout, VoiceMode.Broadcast };

    private static readonly string[] ModeIcons = { "whisper", "mic", "shout", "broadcast" };

    // One hue per range, so the strip says which way you are talking before you read the glyph.
    private static readonly color[] ModeLit = { DashTheme.IconBlue, DashTheme.Positive, DashTheme.Warning, DashTheme.IconPink };

    // Each switch gets the same fixed cell; everything left over goes to the meter, so the level is the
    // widest thing on the strip instead of a stub at the end of it.
    private const float ButtonWidth = 44f;
    private const float MeterMinWidth = 120f;
    // On the dash's top bar: laser-sized switches and a short meter.
    private const float CompactButtonWidth = 28f;
    private const float CompactMeterWidth = 40f;
    private const float CompactGlyph = 16f;

    // The strip at its compact width, and at its own.
    public static float CompactWidth => 5f * CompactButtonWidth + 5f * Gap + CompactMeterWidth + 2f * Inset;
    public static float FullWidth => 5f * ButtonWidth + 5f * Gap + MeterMinWidth + 2f * Inset;
    public static float ControlsWidth => 5f * ButtonWidth + 4f * Gap + 2f * Inset;
    private const float MeterHeight = 12f;
    private const float MeterPad = 2f;
    private const float PeakWidth = 3f;
    private const float MeterInterval = 0.05f;

    // A rect write is a re-tessellation of the chunk, so the bar only moves when the eye could tell.
    private const float MeterEpsilon = 0.01f;

    // Full-scale units the bar is allowed to fall per second. The rise is instant so a syllable lands
    // on it; a matching instant fall would read as flicker rather than as a meter. -xlinka
    private const float MeterRelease = 4f;

    // The peak marker falls far slower than the bar: that lag is the whole point of it, it is how you
    // see the loudest thing you just did after the bar has already dropped. -xlinka
    private const float PeakRelease = 0.6f;

    private readonly Button?[] _modeButtons = new Button?[4];
    private Button? _muteButton;
    private AudioInput? _metered;
    private RectTransform? _meterFill;
    private RectTransform? _meterPeak;
    private float _meterTimer;
    private float _meterLevel;
    private float _peakLevel;
    private float _shownLevel = -1f;
    private float _shownPeak = -1f;
    private VoiceMode _lastActive;
    private VoiceMode _lastMax;
    private bool _lastMuted;
    private bool _stateKnown;

    // Set before it builds by a host that has little room for it.
    public bool Compact { get; set; }

    // Set before it builds by a host that keeps to one accent (the voice popover on the dash's top bar): the
    // lit switch is the accent whichever range it is.
    public bool Quiet { get; set; }

    // The top bar owns the visible meter; its dropdown only needs the voice switches.
    public bool ShowMeter { get; set; } = true;

    public VoiceWidgetPreset()
    {
        MinSize.Value = new float2(5f * ButtonWidth + 5f * Gap + MeterMinWidth + 2f * Inset, 32f);
        PreferredSize.Value = new float2(480f, 40f);
        MaxSize.Value = new float2(700f, 60f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        var strip = root.AddSlot("Strip");
        var rect = strip.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = new float2(Inset, Inset);
        rect.OffsetMax.Value = new float2(-Inset, -Inset);

        var ui = new UIBuilder(strip);
        ui.Font(SemiboldFont);
        ui.FontSize(DashTheme.FontSmall);
        ui.TextColor(DashTheme.Text);
        ui.HorizontalLayout(Gap);
        FillRect(ui.Current.GetComponent<RectTransform>() ?? ui.Current.AttachComponent<RectTransform>());
        float buttonWidth = Compact ? CompactButtonWidth : ButtonWidth;
        float glyph = Compact ? CompactGlyph : 22f;
        float meterWidth = Compact ? CompactMeterWidth : MeterMinWidth;
        ui.PushStyle().MinWidth(buttonWidth).PreferredWidth(buttonWidth).FlexibleWidth(0f);
        _muteButton = IconButton(ui, "mute", glyph, ToggleMute);
        for (int i = 0; i < Modes.Length; i++)
        {
            var mode = Modes[i];
            _modeButtons[i] = IconButton(ui, ModeIcons[i], glyph, () => SetMode(mode));
        }
        ui.PopStyle();

        if (!ShowMeter)
        {
            ui.NestOut();
            _stateKnown = false;
            Poll();
            return;
        }

        // The microphone's level beside the switches, so you can see you are being heard: one continuous
        // bar taking every unit the switches did not.
        ui.PushStyle().MinWidth(meterWidth).PreferredWidth(meterWidth).FlexibleWidth(1f);
        var meter = ui.Empty("Meter");
        ui.PopStyle();
        var track = meter.AddSlot("Track");
        var trackRect = track.AttachComponent<RectTransform>();
        trackRect.AnchorMin.Value = new float2(0f, 0.5f);
        trackRect.AnchorMax.Value = new float2(1f, 0.5f);
        trackRect.OffsetMin.Value = new float2(0f, -MeterHeight * 0.5f);
        trackRect.OffsetMax.Value = new float2(0f, MeterHeight * 0.5f);
        SettingsUI.Panel(track, DashTheme.MeterTrack, DashTheme.Outline, MeterHeight * 0.5f);

        // The fill and the marker ride an inner slot rather than the track itself, so they can be
        // anchored 0..1 across it and still keep the track's padding without signed offset maths that
        // inverts the rect at zero level. -xlinka
        var inner = track.AddSlot("Inner");
        var innerRect = inner.AttachComponent<RectTransform>();
        innerRect.AnchorMin.Value = float2.Zero;
        innerRect.AnchorMax.Value = float2.One;
        innerRect.OffsetMin.Value = new float2(MeterPad, MeterPad);
        innerRect.OffsetMax.Value = new float2(-MeterPad, -MeterPad);

        var fill = inner.AddSlot("Fill");
        _meterFill = fill.AttachComponent<RectTransform>();
        _meterFill.AnchorMin.Value = float2.Zero;
        _meterFill.AnchorMax.Value = new float2(0f, 1f);
        _meterFill.OffsetMin.Value = float2.Zero;
        _meterFill.OffsetMax.Value = float2.Zero;
        SettingsUI.Panel(fill, DashTheme.Positive, color.Transparent, (MeterHeight - 2f * MeterPad) * 0.5f);

        var peak = inner.AddSlot("Peak");
        _meterPeak = peak.AttachComponent<RectTransform>();
        _meterPeak.AnchorMin.Value = float2.Zero;
        _meterPeak.AnchorMax.Value = new float2(0f, 1f);
        _meterPeak.OffsetMin.Value = float2.Zero;
        _meterPeak.OffsetMax.Value = new float2(PeakWidth, 0f);
        SettingsUI.Panel(peak, DashTheme.MeterPeak, color.Transparent, PeakWidth * 0.5f);

        _meterLevel = 0f;
        _peakLevel = 0f;
        _shownLevel = 0f;
        _shownPeak = 0f;
        ui.NestOut();

        // OnUpdate does not run while the slot is inactive, so closing the dash would leave the
        // subscription held and the capture device open. The effective-active signal is the only
        // cheap way to hear about it. -xlinka
        Slot.ActiveChanged += OnSlotActiveChanged;

        _stateKnown = false;
        Poll();
    }

    protected override void Poll()
    {
        var user = World?.LocalUser;
        if (user == null || user.IsDestroyed)
            return;

        bool muted = EngineSettings.MicrophoneMuted;
        var active = user.ActiveVoiceMode;
        var max = user.MaxAllowedVoiceMode;
        if (_stateKnown && muted == _lastMuted && active == _lastActive && max == _lastMax)
            return;
        _stateKnown = true;
        _lastMuted = muted;
        _lastActive = active;
        _lastMax = max;

        for (int i = 0; i < Modes.Length; i++)
        {
            var button = _modeButtons[i];
            if (button == null || button.IsDestroyed)
                continue;
            bool allowed = Modes[i] <= max;
            Paint(button, !muted && active == Modes[i], allowed, Quiet ? DashTheme.Accent : ModeLit[i]);
            ListingStyle.SetInteractable(button, allowed && !WidgetPanel.EditMode);
        }

        // Mute is the capture switch, not a range: it leaves the range you picked intact, so
        // unmuting drops you back where you were.
        if (_muteButton != null && !_muteButton.IsDestroyed)
            Paint(_muteButton, muted || active == VoiceMode.Mute, true, Quiet ? DashTheme.Accent : DashTheme.Negative);
    }

    private static Button IconButton(UIBuilder ui, string icon, float size, Action onClick)
    {
        var button = ui.Button(string.Empty, (_, _) => onClick(), DashTheme.GlassCard);
        var glyph = button.Slot.AddSlot("Icon");
        var rect = glyph.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(-size * 0.5f, -size * 0.5f);
        rect.OffsetMax.Value = new float2(size * 0.5f, size * 0.5f);
        var picture = glyph.AttachComponent<Lumora.Core.Assets.ImageProvider>();
        picture.GenerateMipmaps.Value = false;
        picture.URL.Value = new Uri("res://Assets/Textures/Icons/" + icon + ".svg");
        var image = glyph.AttachComponent<Image>();
        image.Texture.Target = picture;
        image.Tint.Value = color.White;
        return button;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (IsDestroyed || !ShowMeter)
            return;
        _meterTimer += delta;
        if (_meterTimer < MeterInterval)
            return;
        float step = _meterTimer;
        _meterTimer = 0f;

        bool muted = EngineSettings.MicrophoneMuted;
        // The capture device only runs while something is subscribed, so the meter has to hold its own
        // subscription or it reads a closed device and sits flat. Muted drops it: a level meter is not a
        // reason to keep somebody's microphone open. -xlinka
        SetMetering(Slot.IsActive && !muted);
        if (!Slot.IsActive)
            return;

        float peak = _metered?.LastPeak ?? 0f;
        // Square root so a quiet room still moves the bar; a linear meter sits at zero until you shout.
        float target = muted ? 0f : MathF.Min(1f, MathF.Sqrt(MathF.Max(0f, peak)));
        _meterLevel = target >= _meterLevel ? target : MathF.Max(target, _meterLevel - MeterRelease * step);
        _peakLevel = _meterLevel >= _peakLevel ? _meterLevel : MathF.Max(_meterLevel, _peakLevel - PeakRelease * step);

        PaintMeter(_meterLevel, _peakLevel);
    }

    public override void OnDestroy()
    {
        Slot.ActiveChanged -= OnSlotActiveChanged;
        SetMetering(false);
        base.OnDestroy();
    }

    private void OnSlotActiveChanged(Slot slot)
    {
        if (!IsDestroyed && !slot.IsActive)
            SetMetering(false);
    }

    // Holds at most one subscription at a time, and re-takes it on the other device if the default
    // changes under us, so a device swap does not leave the old one held open.
    private void SetMetering(bool on)
    {
        var wanted = on ? Engine.Current?.AudioSystem?.DefaultAudioInput : null;
        if (ReferenceEquals(wanted, _metered))
            return;
        _metered?.RemoveSubscriber();
        _metered = wanted;
        _metered?.AddSubscriber();
    }

    private void PaintMeter(float level, float peak)
    {
        bool moved = false;

        if (_meterFill != null && !_meterFill.IsDestroyed && MathF.Abs(level - _shownLevel) > MeterEpsilon)
        {
            _shownLevel = level;
            _meterFill.AnchorMax.Value = new float2(Clamp01(level), 1f);
            moved = true;
        }

        if (_meterPeak != null && !_meterPeak.IsDestroyed && MathF.Abs(peak - _shownPeak) > MeterEpsilon)
        {
            _shownPeak = peak;
            float at = Clamp01(peak);
            _meterPeak.AnchorMin.Value = new float2(at, 0f);
            _meterPeak.AnchorMax.Value = new float2(at, 1f);
            // The marker is pulled back into the bar as it approaches the right end, so it never hangs
            // off the track's rounded cap. -xlinka
            _meterPeak.OffsetMin.Value = new float2(-PeakWidth * at, 0f);
            _meterPeak.OffsetMax.Value = new float2(PeakWidth * (1f - at), 0f);
            moved = true;
        }

        if (moved)
            Repaint();
    }

    private void SetMode(VoiceMode mode)
    {
        var user = World?.LocalUser;
        if (user == null || user.IsDestroyed)
            return;
        // Picking a range is also the way back from mute.
        EngineSettings.MicrophoneMuted = false;
        user.VoiceMode = mode;
        _stateKnown = false;
    }

    private void ToggleMute()
    {
        EngineSettings.MicrophoneMuted = !EngineSettings.MicrophoneMuted;
        _stateKnown = false;
    }

    private static void Paint(Button button, bool on, bool allowed, in color lit)
    {
        color normal = !allowed ? DashTheme.Field : on ? lit : DashTheme.GlassCard;
        color hover = !allowed ? DashTheme.Field : on ? Shade(lit, 0.08f) : DashTheme.GlassCardHover;
        color pressed = !allowed ? DashTheme.Field : on ? Shade(lit, -0.07f) : DashTheme.GlassCardPressed;
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
        var glyph = button.Slot?.FindChild("Icon", recursive: false)?.GetComponent<Image>();
        if (glyph != null && !glyph.IsDestroyed)
            glyph.Tint.Value = !allowed ? DashTheme.TextMuted : on ? WidgetScreen.OnFill(normal) : color.White;
    }

    // Same step the accent ramp uses, applied to whatever hue the button is lit with, so every range
    // reacts to the pointer the same way. These are linear values, which is where the ramp was measured.
    private static color Shade(in color value, float amount)
        => new color(Clamp01(value.r + amount), Clamp01(value.g + amount), Clamp01(value.b + amount), value.a);

    private static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
}
