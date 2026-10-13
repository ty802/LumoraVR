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
// The audio server keeps what the microphone records in a ring of its own, seconds deep, and hands
// frames out on request. So nothing plays the microphone anywhere: no player, no muted bus, no
// capture effect, and none of the mix periods those cost on the way through. Somebody has to come and
// take the frames, and that somebody is a node in the scene tree, so samples arrive on the main thread
// once a frame, in one block of however much piled up. The node runs ahead of the engine runner, so a
// frame's audio is on the chain thread before that frame's world update starts rather than one frame
// after it. Everything above this treats that as "the capture thread" and never touches the datamodel
// from it, which stays true whichever thread it turns out to be. -xlinka
public sealed class GodotAudioDeviceInput : IAudioDeviceInput
{
    // Ahead of LumoraEngineRunner, which runs at the default of 0.
    private const int PumpPriority = -1000;

    private volatile LumoraCaptureNode? _node;
    private volatile bool _started;
    private volatile bool _open;
    private volatile int _sampleRate = 48000;

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

    // The rate the server fills its input ring at, which is the input's and not the mixer's. The voice
    // chain resamples it.
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

            if (!_open)
            {
                var error = AudioServer.SetInputDeviceActive(true);
                if (error != Error.Ok)
                {
                    LumoraLogger.Warn($"GodotAudioDeviceInput: the audio server would not open the microphone ({error}); it stays closed");
                    _started = false;
                    return;
                }
                _open = true;
            }

            int rate;
            try
            {
                rate = (int)AudioServer.GetInputMixRate();
                if (rate <= 0)
                    rate = (int)AudioServer.GetMixRate();
            }
            catch
            {
                rate = 48000;
            }
            _sampleRate = rate > 0 ? rate : 48000;

            var node = _node;
            if (node == null || !GodotObject.IsInstanceValid(node))
            {
                node = new LumoraCaptureNode
                {
                    Name = "LumoraAudioInputPump",
                    ProcessMode = Node.ProcessModeEnum.Always,
                    ProcessPriority = PumpPriority,
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
        // A Start that landed while this close was deferred wins: the device it opened stays open.
        if (_started)
            return;

        var node = _node;
        _node = null;

        try
        {
            if (node != null && GodotObject.IsInstanceValid(node))
            {
                node.Bind(null);
                node.QueueFree();
            }

            if (_open)
            {
                _open = false;
                AudioServer.SetInputDeviceActive(false);
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"GodotAudioDeviceInput: could not close the microphone cleanly: {ex.Message}");
        }
    }

    // MAIN THREAD, once a frame. The ring is drained whether or not anyone is listening, so a listener
    // that arrives later is not handed seconds of old room.
    internal void Poll()
    {
        if (!_started || !_open)
            return;

        try
        {
            int available = AudioServer.GetInputFramesAvailable();
            if (available <= 0)
                return;

            var frames = AudioServer.GetInputFrames(available);
            var handler = SamplesCaptured;
            if (frames == null || frames.Length == 0 || handler == null)
                return;

            // A stereo sample is two sequential floats and so is the vector the platform hands back,
            // so the block goes up as a view of the same memory rather than a copy.
            handler(MemoryMarshal.Cast<Vector2, StereoSample>(frames.AsSpan()));
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
