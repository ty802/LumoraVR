// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Components.Audio;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Start or stop the audio stream you put into the room, as its own home card. One card, one press:
// the label says which way the press goes and the fill says it is on.
[ComponentCategory("Hidden")]
public sealed class StreamAudioWidgetPreset : HomeWidgetPreset
{
    private Text? _title;
    private Text? _hint;
    private Button? _button;
    private ColorDriver? _driver;
    private bool _lastStreaming;
    private bool? _compact;
    private bool _stateKnown;

    public override string Tip => (FindLocalStream() != null
        ? "Widget.Voice.StopStreaming".AsLocale("Stop streaming") : "Widget.Voice.StreamAudio".AsLocale("Stream audio")).Resolve();

    public StreamAudioWidgetPreset()
    {
        MinSize.Value = new float2(150f, 32f);
        PreferredSize.Value = new float2(250f, 42f);
        MaxSize.Value = new float2(560f, 120f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        if (IconOnly)
        {
            IconOnlyGlyphAt(root, "stream-audio");
            _button = AddCardButton(Toggle);
            _driver = DriveCard(_button, color.Transparent, DashTheme.GlassCardHover, DashTheme.GlassCardPressed);
            _stateKnown = false;
            Poll();
            return;
        }
        IconTileAt(root, "stream-audio", DashTheme.IconPink, Pad - 6f, IconTile);
        _title = Label(root, "Title", "Widget.Voice.StreamAudio".AsLocale("Stream audio").Resolve(), DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(0.62f, 1f), new float2(IconTitleLeft, 0f), float2.Zero, SemiboldFont);
        _title.AutoSizeHorizontal.Value = true;
        _title.AutoSizeMin.Value = 11f;
        _hint = Label(root, "Hint", string.Empty, DashTheme.FontSmall, DashTheme.TextMuted,
            TextHorizontalAlignment.Right, new float2(0.62f, 0f), new float2(1f, 1f), float2.Zero, new float2(-Pad, 0f));
        _button = AddCardButton(Toggle);
        _driver = DriveCard(_button, DashTheme.GlassCard, DashTheme.GlassCardHover, DashTheme.GlassCardPressed);
        _stateKnown = false;
        Poll();
    }

    protected override void Poll()
    {
        if (_button == null || _button.IsDestroyed)
            return;
        ListingStyle.SetInteractable(_button, !WidgetPanel.EditMode);
        ApplyCompact();
        bool streaming = FindLocalStream() != null;
        if (_stateKnown && streaming == _lastStreaming)
            return;
        _stateKnown = true;
        _lastStreaming = streaming;

        if (IconOnly)
        {
            PaintIcon(_driver, streaming, true);
            Repaint();
            return;
        }

        color fill = streaming ? DashTheme.Accent : DashTheme.GlassCard;
        SetCardRamp(_driver, fill, streaming ? DashTheme.AccentHover : DashTheme.GlassCardHover, streaming ? DashTheme.AccentPressed : DashTheme.GlassCardPressed);
        if (_title != null && !_title.IsDestroyed)
        {
            ListingStyle.SetText(_title, (streaming ? "Widget.Voice.StopStreaming".AsLocale("Stop streaming") : "Widget.Voice.StreamAudio".AsLocale("Stream audio")).Resolve());
            ListingStyle.SetTextColor(_title, WidgetScreen.OnFill(fill));
        }
        if (_hint != null && !_hint.IsDestroyed)
            ListingStyle.SetText(_hint, streaming ? "Widget.Voice.Streaming".AsLocale("Streaming").Resolve() : string.Empty);
        Repaint();
    }

    // A tile on the workspace column is too narrow for the hint beside the title; the fill already says
    // when it is streaming.
    private void ApplyCompact()
    {
        float width = Slot.GetComponent<RectTransform>()?.LocalComputeRect.width ?? 0f;
        if (width <= 1f || _title == null || _title.IsDestroyed)
            return;
        bool compact = width < 210f;
        if (_compact == compact)
            return;
        _compact = compact;
        var rect = SettingsUI.Rect(_title.Slot);
        SettingsUI.SetAnchors(rect, float2.Zero, new float2(compact ? 1f : 0.62f, 1f));
        SettingsUI.SetOffsets(rect, new float2(IconTitleLeft, 0f), new float2(compact ? -Pad * 0.5f : 0f, 0f));
        if (_hint != null && !_hint.IsDestroyed)
            SetActive(_hint.Slot, !compact);
    }

    // The focused world, never this card's own: the card lives in userspace, and a stream spawned there
    // was local to this machine - nobody in the room heard it and no hand in the room could take it.
    private void Toggle()
    {
        var world = FocusedWorld;
        if (world == null || world.IsDestroyed)
            return;

        // Queued onto that world rather than written from here: this runs on userspace's update, and the
        // session's own threads may be holding the focused world at this moment.
        var existing = FindLocalStream();
        if (existing != null)
        {
            world.RunSynchronously(() =>
            {
                if (!existing.IsDestroyed)
                    existing.Slot?.Destroy();
            });
            _stateKnown = false;
            return;
        }

        AskForDevice(world);
    }

    // WHICH DEVICE, ASKED RATHER THAN ASSUMED.
    //
    // This used to spawn straight onto the default input, which is the microphone - so the one thing
    // "stream audio" is for, putting what your machine is playing into the room, was the one thing it
    // could not do. So it asks first: the answer is different every time and the person is the only
    // one who knows it.
    //
    // System audio sorts to the top because that is what somebody opening this is almost always after.
    // -xlinka
    private void AskForDevice(World world)
    {
        var audio = Engine.Current?.AudioSystem;
        var inputs = audio?.AudioInputs;
        if (audio == null || inputs == null || inputs.Count == 0)
        {
            ModalHost.For(Slot)?.Open(new ModalRequest
            {
                Title = "Widget.Voice.StreamAudio".AsLocale("Stream audio"),
                Message = "Widget.Voice.NoDevices".AsLocale("This machine has no audio input devices."),
                Invoker = Slot,
                CancelIndex = 0,
            }.AddOption("Dialog.Close".AsLocale("Close")));
            return;
        }

        // Where the platform enumerated its endpoints, those are the list: every output read back, the
        // whole machine minus this program, every microphone. The engine's own voice input is then one of
        // those microphones under another name, so it stays off rather than showing twice.
        bool haveEndpoints = false;
        for (int i = 0; i < inputs.Count; i++)
            haveEndpoints |= inputs[i] != null && inputs[i].IsConnected && inputs[i].EndpointKind != null;

        var candidates = new List<Lumora.Core.Audio.AudioInput>();
        for (int i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            if (input != null && input.IsConnected && (!haveEndpoints || input.EndpointKind != null))
                candidates.Add(input);
        }
        candidates.Sort((a, b) =>
        {
            int byRank = Rank(a).CompareTo(Rank(b));
            return byRank != 0 ? byRank : string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
        });

        if (candidates.Count == 0)
            candidates.AddRange(inputs);

        int remembered = 0;
        string last = EngineSettings.StreamDevice;
        for (int i = 0; i < candidates.Count && last.Length > 0; i++)
        {
            if (string.Equals(candidates[i].DeviceId, last, System.StringComparison.Ordinal))
            {
                remembered = i;
                break;
            }
        }

        bool anyLoopback = false;
        for (int i = 0; i < candidates.Count; i++)
            anyLoopback |= candidates[i].IsLoopback;

        var request = new ModalRequest
        {
            Title = "Widget.Voice.StreamAudio".AsLocale("Stream audio"),
            // The hint is the fix for the machine that has no loopback, which on Windows is most of
            // them until somebody turns one on. Saying nothing leaves people hunting for a setting
            // that is not ours to have.
            Message = anyLoopback
                ? "Widget.Voice.PickDevice".AsLocale("Which device should the room hear?")
                : "Widget.Voice.PickDeviceNoLoopback".AsLocale(
                    "Which device should the room hear? To share what this machine plays, turn on Stereo Mix in Windows sound settings or install a virtual audio cable."),
            Invoker = Slot,
            CancelIndex = 0,
            SelectedChoice = remembered,
        };

        for (int i = 0; i < candidates.Count; i++)
            request.AddChoice(candidates[i].Name, Describe(candidates[i]));

        request.AddOption("Dialog.Cancel".AsLocale("Cancel"));
        request.AddOption("Widget.Voice.StartStreaming".AsLocale("Start streaming"), ModalOptionStyle.Accent);

        var captured = candidates;
        request.Completed = result =>
        {
            if (result.Index != 1)
                return;
            int pick = result.Choice;
            if (pick < 0 || pick >= captured.Count)
                return;
            if (world.IsDestroyed)
                return;
            EngineSettings.StreamDevice = captured[pick].DeviceId;
            world.RunSynchronously(() =>
            {
                if (!world.IsDestroyed)
                    AudioStreamSpawner.SpawnForLocalUser(world, EngineSettings.DefaultStreamBitrateKbps, captured[pick]);
            });
            _stateKnown = false;
        };

        ModalHost.For(Slot)?.Open(request);
    }

    // System audio first, because it is what somebody opening this is almost always after and the only
    // choice that cannot echo the room back into itself; then each output device; then the rest.
    private static int Rank(Lumora.Core.Audio.AudioInput input) => input.EndpointKind switch
    {
        Lumora.Audio.AudioEndpointKind.SystemLoopback => -2,
        Lumora.Audio.AudioEndpointKind.OutputLoopback => -1,
        _ => Lumora.Core.Audio.AudioInputTypes.StreamOrder(input.InputType),
    };

    private static string Describe(Lumora.Core.Audio.AudioInput input) => input.EndpointKind switch
    {
        Lumora.Audio.AudioEndpointKind.SystemLoopback =>
            "Widget.Voice.SystemAudioHint".AsLocale("Everything this PC plays, except LumoraVR").Resolve(),
        Lumora.Audio.AudioEndpointKind.OutputLoopback =>
            "Widget.Voice.OutputHint".AsLocale("What this output plays, the room included").Resolve(),
        _ => Lumora.Core.Audio.AudioInputTypes.Describe(input.InputType),
    };

    // Direct children of the world root only: that is where the spawner parents the item.
    private AudioStreamController? FindLocalStream()
    {
        var root = FocusedWorld?.RootSlot;
        if (root == null || root.IsDestroyed)
            return null;
        foreach (var child in root.Children)
        {
            var controller = child.GetComponent<AudioStreamController>();
            if (controller != null && !controller.IsDestroyed && controller.IsOwnedByLocalUser)
                return controller;
        }
        return null;
    }
}
