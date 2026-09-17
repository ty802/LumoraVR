// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Helio.UI.Listing;
using Lumora.Core.Components.Audio;
using Lumora.Core.Localization;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Start or stop the audio stream you put into the room, as its own home card the way the reference
// files it. One card, one press: the label says which way the press goes and the fill says it is on.
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
            existing.Slot?.Destroy();
        else
            AudioStreamSpawner.SpawnForLocalUser(world, EngineSettings.DefaultStreamBitrateKbps, Engine.Current?.AudioSystem?.DefaultAudioInput);
        _stateKnown = false;
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
