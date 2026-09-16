// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;
using Godot;
using Lumora.Audio;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot;

// The engine's one way in from the microphone.
//
// The platform has no callback for captured audio: the capture effect fills a ring and somebody has
// to come and take it. That somebody is a node in the scene tree, so samples arrive on the main
// thread once a frame, in one block of however much piled up. Everything above this treats that as
// "the capture thread" and never touches the datamodel from it, which stays true whichever thread it
// turns out to be.
//
// The microphone gets its own bus, muted, because the only way to hear a capture bus is to route it
// somewhere and the only thing that would do is put the room back through the speakers. The mute is
// on the bus send, not on the effect chain, so the capture effect still sees every sample. -xlinka
public sealed class GodotAudioDeviceInput : IAudioDeviceInput
{
    private const string CaptureBusName = "LumoraCapture";

    // Half a second of slack. The poll runs once a frame and a frame that hitches for a quarter of a
    // second should cost latency, not a hole in what somebody said.
    private const float CaptureBufferSeconds = 0.5f;

    private volatile AudioStreamPlayer? _player;
    private volatile AudioEffectCapture? _capture;
    private volatile LumoraCaptureNode? _node;
    private volatile bool _started;
    private volatile int _sampleRate = 48000;

    private Vector2[] _frames = Array.Empty<Vector2>();

    public event AudioSamplesHandler? SamplesCaptured;

    public string Name
    {
        get
        {
            try
            {
                var device = AudioServer.InputDevice;
                return string.IsNullOrEmpty(device) ? "Default" : device;
            }
            catch
            {
                return "Default";
            }
        }
    }

    public string DeviceId => Name;

    // The capture runs at the mixer's rate, not the hardware's: the platform resamples the
    // microphone into the bus before any effect sees it.
    public int SampleRate => _sampleRate;

    public bool IsSystemDefault
    {
        get
        {
            try
            {
                var device = AudioServer.InputDevice;
                return string.IsNullOrEmpty(device) || device == "Default";
            }
            catch
            {
                return true;
            }
        }
    }

    public bool IsCapturing => _started;

    public void Start()
    {
        if (_started)
            return;
        _started = true;

        if (IsMainThread())
            OpenOnMainThread();
        else
            Callable.From(OpenOnMainThread).CallDeferred();
    }

    public void Stop()
    {
        if (!_started)
            return;
        _started = false;

        if (IsMainThread())
            CloseOnMainThread();
        else
            Callable.From(CloseOnMainThread).CallDeferred();
    }

    private static bool IsMainThread() => OS.GetThreadCallerId() == OS.GetMainThreadId();

    private void OpenOnMainThread()
    {
        if (!_started)
            return;

        try
        {
            if (global::Godot.Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
                return;

            try
            {
                _sampleRate = (int)AudioServer.GetMixRate();
            }
            catch
            {
                _sampleRate = 48000;
            }

            int bus = EnsureCaptureBus();
            if (bus < 0)
            {
                LumoraLogger.Warn("GodotAudioDeviceInput: could not make a capture bus; the microphone stays closed");
                _started = false;
                return;
            }

            _capture = EnsureCaptureEffect(bus);
            if (_capture == null)
            {
                LumoraLogger.Warn("GodotAudioDeviceInput: the capture bus refused a capture effect; the microphone stays closed");
                _started = false;
                return;
            }

            _capture.ClearBuffer();

            var player = _player;
            if (player == null || !GodotObject.IsInstanceValid(player))
            {
                player = new AudioStreamPlayer
                {
                    Name = "LumoraAudioInput",
                    Bus = CaptureBusName,
                    ProcessMode = Node.ProcessModeEnum.Always,
                    Stream = new AudioStreamMicrophone(),
                };
                _player = player;
            }

            if (player.GetParent() == null)
                tree.Root.AddChild(player);

            if (!player.Playing)
                player.Play();

            var node = _node;
            if (node == null || !GodotObject.IsInstanceValid(node))
            {
                node = new LumoraCaptureNode
                {
                    Name = "LumoraAudioInputPump",
                    ProcessMode = Node.ProcessModeEnum.Always,
                };
                node.Bind(this);
                _node = node;
            }

            if (node.GetParent() == null)
                tree.Root.AddChild(node);
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"GodotAudioDeviceInput: could not open the microphone: {ex.Message}");
            _started = false;
        }
    }

    private void CloseOnMainThread()
    {
        var node = _node;
        _node = null;
        var player = _player;
        _player = null;
        _capture = null;

        try
        {
            if (node != null && GodotObject.IsInstanceValid(node))
            {
                node.Bind(null);
                node.QueueFree();
            }

            if (player != null && GodotObject.IsInstanceValid(player))
            {
                if (player.Playing)
                    player.Stop();
                player.QueueFree();
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"GodotAudioDeviceInput: could not close the microphone cleanly: {ex.Message}");
        }
    }

    private static int EnsureCaptureBus()
    {
        int bus = AudioServer.GetBusIndex(CaptureBusName);
        if (bus < 0)
        {
            AudioServer.AddBus();
            bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, CaptureBusName);
        }

        // Muted on the send, so nothing routes the microphone back into the room. The effect chain
        // still runs, which is the whole point of putting the capture here.
        AudioServer.SetBusMute(bus, true);
        return bus;
    }

    private static AudioEffectCapture? EnsureCaptureEffect(int bus)
    {
        int effects = AudioServer.GetBusEffectCount(bus);
        for (int i = 0; i < effects; i++)
        {
            if (AudioServer.GetBusEffect(bus, i) is AudioEffectCapture existing)
            {
                existing.BufferLength = CaptureBufferSeconds;
                return existing;
            }
        }

        var capture = new AudioEffectCapture { BufferLength = CaptureBufferSeconds };
        AudioServer.AddBusEffect(bus, capture);
        return capture;
    }

    // MAIN THREAD, once a frame.
    internal void Poll()
    {
        if (!_started)
            return;

        var capture = _capture;
        var handler = SamplesCaptured;
        if (capture == null || handler == null)
            return;

        try
        {
            if (!GodotObject.IsInstanceValid(capture))
            {
                // The bus layout changed underneath us. Reopening is the only way back, and doing it
                // from here keeps the mixer thread out of it.
                _capture = null;
                OpenOnMainThread();
                return;
            }

            int available = capture.GetFramesAvailable();
            if (available <= 0)
                return;

            _frames = capture.GetBuffer(available);
            if (_frames == null || _frames.Length == 0)
                return;

            // A stereo sample is two sequential floats and so is the vector the platform hands back,
            // so the block goes up as a view of the same memory rather than a copy.
            handler(MemoryMarshal.Cast<Vector2, StereoSample>(_frames.AsSpan()));
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"GodotAudioDeviceInput: dropped a capture block: {ex.Message}");
        }
    }
}

// The pump. Nothing but a place in the tree to be ticked from; it holds no state of its own so the
// driver can drop and rebuild it without losing anything.
public partial class LumoraCaptureNode : Node
{
    private GodotAudioDeviceInput? _input;

    internal void Bind(GodotAudioDeviceInput? input) => _input = input;

    public override void _Ready()
    {
        // Asked for explicitly rather than left to the platform's own detection of an overridden
        // frame callback, because this node is built in code and never comes from a scene.
        SetProcess(true);
    }

    public override void _Process(double delta) => _input?.Poll();
}
