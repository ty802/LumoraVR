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

    // Headroom, not latency: Godot rounds this up to a power of two (8192 frames) and the mixer only
    // fills the ring to the target below.
    private const float BufferSeconds = 0.1f;

    // FILL TARGET. Godot's mix takes 512 frames at a time, so the queue swings between target - 512 and
    // target, and every frame in it is time between a render and the ear; a full ring would be a third
    // of a second of it. Start low; when the mix finds the queue short and plays silence (a skip) the
    // target goes up, and after 20 s without one it creeps back down. Mobile mixes in bigger bursts and
    // starts higher. Counts are frames at MixRate. -xlinka
    private const int DesktopTarget = 1536;
    private const int MobileTarget = 3072;
    private const int TargetFloor = 1024;
    private const int TargetCeiling = 4096;
    private const int TargetRaise = 512;
    private const int TargetLower = 256;
    private const long LowerAfterMs = 20_000;
    // A fresh playback is mixed empty until the first write lands; those skips are not starvation.
    private const long SkipGraceMs = 250;

    private volatile AudioStreamPlayer? _player;
    private volatile AudioStreamGeneratorPlayback? _playback;
    private volatile bool _started;

    // One queued resume at a time. The mixer thread notices a dead player every millisecond it polls,
    // and without the latch that is a thousand deferred calls a second. -xlinka
    private int _resumePending;
    private int _restarts;

    // The mixer thread's, except what Start and a fresh open set on the main thread before the playback
    // is published.
    private volatile int _capacity;
    private volatile int _target;
    private volatile int _queued;
    private int _skipsSeen;
    private int _underruns;
    private long _openedAt;
    private long _lastStarvedAt;

    public int SampleRate => MixRate;

    // As of the mixer's last poll, a millisecond ago at most.
    public int FramesQueued => _started && _playback != null ? _queued : 0;

    // What is queued in the generator now, and the platform's own output latency behind it.
    public double LatencySeconds => (double)FramesQueued / MixRate + _outputLatency;

    public int Underruns => Volatile.Read(ref _underruns);

    private volatile float _outputLatency;

    public int Restarts => Volatile.Read(ref _restarts);

    private const long LiveCheckMs = 50;
    private long _liveCheckedAt;

    public int FramesWritable
    {
        get
        {
            if (!_started) return 0;

            var playback = _playback;
            if (playback == null) return 0;

            try
            {
                // Whether the player still plays is asked a few times a second, not on every poll. The
                // pump polls every millisecond, and each ask took the audio server's lock, which Godot's
                // own mixing thread holds while it mixes, plus the object table's lock that every object
                // made or freed anywhere takes: a third of this thread's time went to waiting on them. A
                // player that stops is noticed within a check, and the skip count rides the same one. -xlinka
                long now = System.Environment.TickCount64;
                if (now - _liveCheckedAt >= LiveCheckMs)
                {
                    var player = _player;
                    if (player == null || !GodotObject.IsInstanceValid(player) || !player.Playing
                        || !GodotObject.IsInstanceValid(playback))
                    {
                        RequestResume();
                        return 0;
                    }
                    _liveCheckedAt = now;
                    CheckSkips(playback, now);
                }

                int available = playback.GetFramesAvailable();
                int capacity = _capacity;
                // Nothing can have more room than the whole ring, so a larger answer corrects the size.
                if (available > capacity)
                    _capacity = capacity = available;
                int queued = capacity - available;
                _queued = queued;
                int target = Math.Min(_target, capacity);
                return Math.Max(0, target - queued);
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
            _queued += frames.Length;
        }
        catch
        {
            // A refused buffer is a dropped frame, not a crash.
        }
    }

    public void Start()
    {
        if (_target == 0)
        {
            _target = Lumora.Core.Engine.IsMobilePlatform ? MobileTarget : DesktopTarget;
            Volatile.Write(ref _lastStarvedAt, System.Environment.TickCount64);
        }
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

            bool fresh = !player.Playing;
            if (fresh)
                player.Play();

            try { _outputLatency = (float)AudioServer.GetOutputLatency(); }
            catch (Exception) { _outputLatency = 0f; }

            // The playback only exists while the player is playing and a fresh one comes with every
            // Play, so it is fetched here rather than cached at build time.
            var playback = player.HasStreamPlayback()
                ? player.GetStreamPlayback() as AudioStreamGeneratorPlayback
                : null;

            if (playback != null && fresh)
            {
                // A fresh ring is empty and the mixer cannot reach it until it is published below, so
                // its room right now is the size Godot really gave it.
                _capacity = playback.GetFramesAvailable();
                _queued = 0;
                Volatile.Write(ref _openedAt, System.Environment.TickCount64);
            }

            _playback = playback;

            if (playback == null)
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

    // Mixer thread, from the live check. Every Play starts the count again at zero, so a count that went
    // down only sets the baseline.
    private void CheckSkips(AudioStreamGeneratorPlayback playback, long now)
    {
        int skips = playback.GetSkips();
        int seen = _skipsSeen;
        _skipsSeen = skips;
        if (skips < seen || now - Volatile.Read(ref _openedAt) < SkipGraceMs)
            return;

        if (skips > seen)
        {
            Interlocked.Add(ref _underruns, skips - seen);
            _target = Math.Min(TargetCeiling, _target + TargetRaise);
            Volatile.Write(ref _lastStarvedAt, now);
        }
        else if (now - Volatile.Read(ref _lastStarvedAt) >= LowerAfterMs && _target > TargetFloor)
        {
            _target = Math.Max(TargetFloor, _target - TargetLower);
            Volatile.Write(ref _lastStarvedAt, now);
        }
    }
}
