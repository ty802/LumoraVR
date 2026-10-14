// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;
using Lumora.Core.Assets;

namespace Lumora.Core.Components.Audio;

// A sound that answers to something else: switched on, it plays from the top; switched off, it stops. Point an
// AudioOutput at it and it is heard from its slot.
//
// Each machine plays it on its own clock. What starts it (a switch, a gesture, an animator state, a touch)
// already reaches every machine, so every machine starts it the moment that happens there, and nothing about
// the playback itself is sent or written. AudioClipPlayer is the other kind: one shared playhead everybody
// follows, for music and recordings that have to line up between people.
//
// Cue and the Cue* values are this machine's own and never sync: they are how something running on every
// machine at once (an animator, a script) picks a clip, a volume and a pitch for one play. The clips in Clips
// are kept loaded for that.
//
// Nothing on the audio thread touches a sync member: the world frame copies what to play into plain fields
// under a lock, and the mixer reads those. -xlinka
[ComponentCategory("Audio")]
public sealed class AudioCuePlayer : Component, IWorldAudioDataSource
{
    public readonly AssetRef<AudioClip> Clip;

    // More clips a cue may play. Listed so every machine has them decoded before they are wanted.
    public readonly SyncAssetList<AudioClip> Clips;

    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Volume;

    // Playback rate: 2 is an octave up and twice as fast.
    [Range(0.05f, 4f, "0.00")]
    public readonly Sync<float> Pitch;

    public readonly Sync<bool> Loop;

    // Plays from the top whenever it comes on: the component switched on, or its slot made active.
    public readonly Sync<bool> PlayOnEnable;

    // Frames of fade at a start and a stop, so neither clicks.
    private const int EdgeFrames = 96;

    // A cue still waiting for its clip to load after this long is dropped rather than played late.
    private const double CueWaitSeconds = 1.5;

    // Past this much silence from the world thread the snapshot is stale: go quiet rather than play on.
    private const double SnapshotStaleAfter = 0.5;

    private readonly object _lock = new();

    // Audio-thread snapshot, written under the lock once per world frame.
    private AudioClipData? _data;
    private bool _loop;
    private float _gain;
    private double _rate;
    private int _generation;
    private bool _playing;
    private double _stamp;

    // Audio thread only.
    private int _heardGeneration = -1;
    private double _cursor;
    private int _fadeIn;
    private int _fadeOut;
    private bool _wasPlaying;
    private AudioClipData? _lastData;

    // Set by the audio thread when a clip that does not loop has run out.
    private volatile int _finishedGeneration = -1;
    private volatile bool _isActive;

    // World thread.
    private ulong _lastUpdate;
    private bool _cued;
    private int _cueGeneration;
    private double _cueAt;
    private double _cueRequested;
    private bool _started;
    private int _lastPick = -1;
    private int _nextInOrder;
    private readonly Random _random = new();

    public AudioCuePlayer()
    {
        Clip = new AssetRef<AudioClip>(this);
        Clips = new SyncAssetList<AudioClip>(this);
        Volume = new Sync<float>(this, 1f);
        Pitch = new Sync<float>(this, 1f);
        Loop = new Sync<bool>(this, false);
        PlayOnEnable = new Sync<bool>(this, true);
    }

    // THIS MACHINE'S CUE

    // The clip the next cue plays; null plays Clip.
    public IAssetProvider<AudioClip>? CueClip { get; set; }

    // Times Volume, for the next cues.
    public float CueVolume { get; set; } = 1f;

    // Times Pitch, for the next cues.
    public float CuePitch { get; set; } = 1f;

    // Null follows Loop.
    public bool? CueLoop { get; set; }

    // Playing here, or waiting to start.
    public bool IsCuePlaying => _cued && _finishedGeneration != _cueGeneration;

    // The clip a cue plays here.
    public IAssetProvider<AudioClip>? CurrentClip => CueClip ?? Clip.Target;

    // From the top, after a delay in seconds. A player that is switched off plays nothing.
    public void Cue(float delay = 0f)
    {
        var world = World;
        if (world == null || IsDestroyed || !Enabled.Value || Slot?.IsActive != true)
            return;
        double now = world.Time.TotalTime;
        _cued = true;
        _started = false;
        _cueGeneration++;
        _cueRequested = now;
        _cueAt = now + (float.IsFinite(delay) && delay > 0f ? delay : 0f);
        Push();
    }

    public void Cue(IAssetProvider<AudioClip>? clip, float volume = 1f, float pitch = 1f, bool? loop = null, float delay = 0f)
    {
        CueClip = clip;
        CueVolume = volume;
        CuePitch = pitch;
        CueLoop = loop;
        Cue(delay);
    }

    public void StopCue()
    {
        if (!_cued)
            return;
        _cued = false;
        _started = false;
        Push();
    }

    // One of Clips (Clip when the list is empty) by the order given; index is for ByIndex. The choice is this
    // machine's own.
    public IAssetProvider<AudioClip>? PickClip(AudioCueOrder order, int index = 0)
    {
        int count = Clips.Count;
        if (count == 0)
            return Clip.Target;
        int pick = order switch
        {
            AudioCueOrder.InOrder => _nextInOrder++ % count,
            AudioCueOrder.ByIndex => ((index % count) + count) % count,
            AudioCueOrder.RandomNoRepeat when count > 1 && _lastPick >= 0 && _lastPick < count
                => (_lastPick + 1 + _random.Next(count - 1)) % count,
            _ => _random.Next(count),
        };
        if (_nextInOrder >= count)
            _nextInOrder = 0;
        _lastPick = pick;
        return Clips.GetElement(pick).Target;
    }

    // WORLD FRAME

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        var world = World;
        if (world == null)
            return;

        // Updates stop while the component is off or its slot is inactive, so a gap means it just came back on.
        ulong now = world.Time.UpdateIndex;
        bool cameOn = _lastUpdate == 0 || now > _lastUpdate + 1;
        _lastUpdate = now;
        if (cameOn && PlayOnEnable.Value)
            Cue();

        Push();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        Silence();
    }

    public override void OnStart()
    {
        base.OnStart();
        if (Slot != null)
            Slot.OnActiveChanged += HandleActiveChanged;
    }

    public override void OnDestroy()
    {
        if (Slot != null)
            Slot.OnActiveChanged -= HandleActiveChanged;
        Silence();
        lock (_lock)
            _data = null;
        base.OnDestroy();
    }

    // A slot made inactive stops its updates at once; without this the sound would ring on until the snapshot
    // went stale.
    private void HandleActiveChanged(Slot slot, bool active)
    {
        if (!active || !(Slot?.IsActive ?? false))
            Silence();
    }

    private void Silence()
    {
        _cued = false;
        _started = false;
        _lastUpdate = 0;
        lock (_lock)
            _playing = false;
    }

    private void Push()
    {
        var world = World;
        var provider = CurrentClip;
        var asset = provider is { IsDestroyed: false } ? provider.Asset : null;
        var data = asset != null && asset.IsReady ? asset.Data : null;
        double time = world?.Time.TotalTime ?? 0.0;
        bool background = world != null && world.Focus == Lumora.Core.World.WorldFocus.Background;

        if (_cued && !_started)
        {
            if (time - _cueRequested > CueWaitSeconds + System.Math.Max(0.0, _cueAt - _cueRequested))
                _cued = false;
            else if (time >= _cueAt && data != null)
                _started = true;
        }

        bool playing = _cued && _started && data != null && !background && Enabled.Value
                       && _finishedGeneration != _cueGeneration;
        float gain = Sanitize(Volume.Value) * Sanitize(CueVolume);
        double rate = Sanitize(Pitch.Value) * Sanitize(CuePitch);
        if (rate < 0.01)
            rate = 0.01;

        lock (_lock)
        {
            _data = data;
            _loop = CueLoop ?? Loop.Value;
            _gain = gain;
            _rate = rate;
            _generation = _cueGeneration;
            _playing = playing;
            _stamp = Engine.Current?.AudioSystem?.Simulator.DspTime ?? 0.0;
        }
        _isActive = playing || _wasPlaying;
    }

    private static float Sanitize(float value) => float.IsFinite(value) && value > 0f ? value : 0f;

    // AUDIO THREAD

    public int ChannelCount => 2;

    public bool IsActive => _isActive;

    public void Read(Span<StereoSample> buffer, AudioSimulator simulator)
    {
        AudioClipData? data;
        bool loop, playing;
        float gain;
        double rate;
        int generation;
        lock (_lock)
        {
            data = _data;
            loop = _loop;
            gain = _gain;
            rate = _rate;
            generation = _generation;
            playing = _playing;
            double elapsed = simulator.DspTime - _stamp;
            if (elapsed > SnapshotStaleAfter)
                playing = false;
        }

        if (generation != _heardGeneration && playing)
        {
            _heardGeneration = generation;
            _cursor = 0.0;
            _fadeIn = EdgeFrames;
            _fadeOut = 0;
        }

        if (!playing && _wasPlaying)
        {
            // Fade out what was sounding instead of cutting it off mid-wave.
            _fadeOut = EdgeFrames;
            _wasPlaying = false;
            data = _lastData;
        }
        else if (playing)
        {
            _wasPlaying = true;
            _lastData = data;
        }

        if (data == null || data.FrameCount == 0 || (!playing && _fadeOut <= 0))
        {
            buffer.Clear();
            _isActive = playing;
            return;
        }

        int frames = data.FrameCount;
        double step = rate * data.SampleRate / simulator.SampleRate;
        if (!double.IsFinite(step) || step <= 0.0)
            step = 0.0;

        for (int i = 0; i < buffer.Length; i++)
        {
            if (!playing)
            {
                if (_fadeOut <= 0)
                {
                    buffer[i] = default;
                    continue;
                }
            }

            double position = _cursor;
            if (position >= frames)
            {
                if (loop)
                {
                    position %= frames;
                    _cursor = position;
                }
                else
                {
                    buffer[i] = default;
                    if (playing)
                        _finishedGeneration = generation;
                    _fadeOut = 0;
                    continue;
                }
            }

            int index = (int)position;
            int next = index + 1;
            if (next >= frames)
                next = loop ? 0 : frames - 1;
            var sample = data.FrameAt(index).LerpTo(data.FrameAt(next), (float)(position - index));

            float edge = 1f;
            if (_fadeIn > 0)
            {
                edge = 1f - _fadeIn / (float)EdgeFrames;
                _fadeIn--;
            }
            if (!playing)
            {
                edge *= _fadeOut / (float)EdgeFrames;
                _fadeOut--;
            }
            buffer[i] = sample.Multiply(gain * edge);
            _cursor += step;
        }

        if (!playing && _fadeOut <= 0)
            _isActive = false;
    }
}
