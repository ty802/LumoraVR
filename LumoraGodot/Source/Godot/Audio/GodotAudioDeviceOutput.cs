// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.InteropServices;
using System.Threading;
using Godot;
using Lumora.Audio;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Source.Godot;

// The mixer's one way out to the speakers: a generator stream on the Master bus that the audio
// thread fills directly. Everything above this - group volumes, spatialisation, effects - is the
// mixer's, so the device claims no bus of its own and never touches the bus layout.
//
// Threading: the mixer thread polls FramesWritable and calls Write; both only ever touch the
// playback object, which the platform explicitly allows to be fed from a thread. Node work - making
// the player, parenting it, Play, Stop, free - is main-thread-only and is deferred when the caller
// is on any other thread. -xlinka
public sealed class GodotAudioDeviceOutput : IAudioDeviceOutput
{
    private const int MixRate = 48000;

    // Two tenths of a second of slack between a render and the hardware. Less and a hitched frame
    // is audible; more and a volume change takes a noticeable moment to land.
    private const float BufferSeconds = 0.2f;

    private volatile AudioStreamPlayer? _player;
    private volatile AudioStreamGeneratorPlayback? _playback;
    private volatile bool _started;

    // One queued resume at a time. The mixer thread notices a dead player every millisecond it polls,
    // and without the latch that is a thousand deferred calls a second. -xlinka
    private int _resumePending;
    private int _restarts;

    public int SampleRate => MixRate;

    // The mixer keeps the generator full, so its whole length is in front of every frame written,
    // and the platform's own output latency sits behind that.
    public double LatencySeconds => BufferSeconds + _outputLatency;

    private volatile float _outputLatency;

    public int Restarts => Volatile.Read(ref _restarts);

    public int FramesWritable
    {
        get
        {
            if (!_started) return 0;

            var playback = _playback;
            if (playback == null) return 0;

            try
            {
                var player = _player;
                if (player == null || !GodotObject.IsInstanceValid(player) || !player.Playing)
                {
                    RequestResume();
                    return 0;
                }

                if (!GodotObject.IsInstanceValid(playback))
                {
                    RequestResume();
                    return 0;
                }

                return playback.GetFramesAvailable();
            }
            catch
            {
                // A device that cannot answer has no room. Throwing here would take the mixer thread
                // down and the session with it.
                return 0;
            }
        }
    }

    public void Write(ReadOnlySpan<StereoSample> frames)
    {
        if (frames.IsEmpty) return;

        var playback = _playback;
        if (playback == null) return;

        try
        {
            if (!GodotObject.IsInstanceValid(playback))
            {
                RequestResume();
                return;
            }

            // A stereo sample is two sequential floats and so is the vector the ring wants, so the
            // whole block goes across as a view of the mixer's own buffer.
            playback.PushBuffer(MemoryMarshal.Cast<StereoSample, Vector2>(frames));
        }
        catch
        {
            // A refused buffer is a dropped frame, not a crash.
        }
    }

    public void Start()
    {
        _started = true;
        if (IsMainThread())
            OpenOnMainThread();
        else
            Callable.From(OpenOnMainThread).CallDeferred();
    }

    public void Stop()
    {
        _started = false;
        _playback = null;
        if (IsMainThread())
            CloseOnMainThread();
        else
            Callable.From(CloseOnMainThread).CallDeferred();
    }

    private static bool IsMainThread() => OS.GetThreadCallerId() == OS.GetMainThreadId();

    private void OpenOnMainThread()
    {
        Volatile.Write(ref _resumePending, 0);
        if (!_started) return;

        try
        {
            if (global::Godot.Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
                return;

            var player = _player;
            if (player == null || !GodotObject.IsInstanceValid(player))
            {
                player = new AudioStreamPlayer
                {
                    Name = "LumoraAudioOutput",
                    Bus = "Master",
                    ProcessMode = Node.ProcessModeEnum.Always,
                    Stream = new AudioStreamGenerator
                    {
                        MixRate = MixRate,
                        BufferLength = BufferSeconds,
                    },
                };
                _player = player;
            }

            if (player.GetParent() == null)
                tree.Root.AddChild(player);

            if (!player.Playing)
                player.Play();

            try { _outputLatency = (float)AudioServer.GetOutputLatency(); }
            catch (Exception) { _outputLatency = 0f; }

            // The playback only exists while the player is playing and a fresh one comes with every
            // Play, so it is fetched here rather than cached at build time.
            _playback = player.HasStreamPlayback()
                ? player.GetStreamPlayback() as AudioStreamGeneratorPlayback
                : null;

            if (_playback == null)
                LumoraLogger.Warn("GodotAudioDeviceOutput: the output stream gave no playback; nothing will be heard");
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"GodotAudioDeviceOutput: could not open the audio device: {ex.Message}");
        }
    }

    private void CloseOnMainThread()
    {
        var player = _player;
        _player = null;
        _playback = null;
        Volatile.Write(ref _resumePending, 0);

        if (player == null || !GodotObject.IsInstanceValid(player)) return;

        try
        {
            if (player.Playing)
                player.Stop();
            player.QueueFree();
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"GodotAudioDeviceOutput: could not close the audio device cleanly: {ex.Message}");
        }
    }

    private void RequestResume()
    {
        if (Interlocked.Exchange(ref _resumePending, 1) != 0) return;

        // Parking the playback stops the mixer pushing into a ring nobody drains; it comes back when
        // the resume lands.
        _playback = null;
        Callable.From(ResumeOnMainThread).CallDeferred();
    }

    private void ResumeOnMainThread()
    {
        Volatile.Write(ref _resumePending, 0);
        if (!_started) return;

        // Counted before the reopen, while the player still shows why it needed one: a generator that
        // ran dry long enough for the platform to end the stream.
        var player = _player;
        if (player != null && GodotObject.IsInstanceValid(player) && !player.Playing)
            Interlocked.Increment(ref _restarts);
            LumoraLogger.Log($"GodotAudioDeviceOutput: generator starved, restarted the player (restart {Restarts})");

        OpenOnMainThread();
    }
}
