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
    private bool _stateKnown;

    public StreamAudioWidgetPreset()
    {
        MinSize.Value = new float2(150f, 32f);
        PreferredSize.Value = new float2(250f, 42f);
        MaxSize.Value = new float2(560f, 120f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        IconTileAt(root, "stream-audio", DashTheme.IconPink, Pad - 6f, IconTile);
        _title = Label(root, "Title", "Widget.Voice.StreamAudio".AsLocale("Stream audio").Resolve(), DashTheme.FontBody, DashTheme.Text,
            TextHorizontalAlignment.Left, new float2(0f, 0f), new float2(0.62f, 1f), new float2(IconTitleLeft, 0f), float2.Zero, SemiboldFont);
        _hint = Label(root, "Hint", string.Empty, DashTheme.FontSmall, DashTheme.TextMuted,
            TextHorizontalAlignment.Right, new float2(0.62f, 0f), new float2(1f, 1f), float2.Zero, new float2(-Pad, 0f));
        _button = AddCardButton(Toggle);
        _driver = DriveCard(_button, DashTheme.Surface, DashTheme.SurfaceHover, DashTheme.SurfacePressed);
        _stateKnown = false;
        Poll();
    }

    protected override void Poll()
    {
        if (_button == null || _button.IsDestroyed)
            return;
        ListingStyle.SetInteractable(_button, !WidgetPanel.EditMode);
        bool streaming = FindLocalStream() != null;
        if (_stateKnown && streaming == _lastStreaming)
            return;
        _stateKnown = true;
        _lastStreaming = streaming;

        color fill = streaming ? DashTheme.Accent : DashTheme.Surface;
        SetCardRamp(_driver, fill, streaming ? DashTheme.AccentHover : DashTheme.SurfaceHover, streaming ? DashTheme.AccentPressed : DashTheme.SurfacePressed);
        if (_title != null && !_title.IsDestroyed)
        {
            ListingStyle.SetText(_title, (streaming ? "Widget.Voice.StopStreaming".AsLocale("Stop streaming") : "Widget.Voice.StreamAudio".AsLocale("Stream audio")).Resolve());
            ListingStyle.SetTextColor(_title, WidgetScreen.OnFill(fill));
        }
        if (_hint != null && !_hint.IsDestroyed)
            ListingStyle.SetText(_hint, streaming ? "Widget.Voice.Streaming".AsLocale("Streaming").Resolve() : string.Empty);
        Repaint();
    }

    private void Toggle()
    {
        var world = World;
        if (world == null || world.IsDestroyed)
            return;

        var existing = FindLocalStream();
        if (existing != null)
        {
            existing.Slot?.Destroy();
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

        var candidates = new List<Lumora.Core.Audio.AudioInput>();
        for (int i = 0; i < inputs.Count; i++)
        {
            if (inputs[i] != null && inputs[i].IsConnected)
                candidates.Add(inputs[i]);
        }
        candidates.Sort((a, b) =>
        {
            int byType = Lumora.Core.Audio.AudioInputTypes.StreamOrder(a.InputType)
                .CompareTo(Lumora.Core.Audio.AudioInputTypes.StreamOrder(b.InputType));
            return byType != 0 ? byType : string.Compare(a.Name, b.Name, System.StringComparison.OrdinalIgnoreCase);
        });

        if (candidates.Count == 0)
            candidates.AddRange(inputs);

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
            SelectedChoice = 0,
        };

        for (int i = 0; i < candidates.Count; i++)
        {
            var device = candidates[i];
            request.AddChoice(device.Name,
                Lumora.Core.Audio.AudioInputTypes.Describe(device.InputType));
        }

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
            AudioStreamSpawner.SpawnForLocalUser(world, EngineSettings.DefaultStreamBitrateKbps, captured[pick]);
            _stateKnown = false;
        };

        ModalHost.For(Slot)?.Open(request);
    }

    // Direct children of the world root only: that is where the spawner parents the item.
    private AudioStreamController? FindLocalStream()
    {
        var root = World?.RootSlot;
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
