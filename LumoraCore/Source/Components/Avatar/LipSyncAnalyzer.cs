// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;
using Lumora.Core;
using Lumora.Core.Components.Audio;
using Lumora.Core.Networking.Streams;
using AudioInput = Lumora.Core.Audio.AudioInput;

namespace Lumora.Core.Components.Avatar;

// Per-viseme weights from the wearer's voice. Consumed by VisemeWeightDriver and the animator, which
// is why it updates ahead of both: they read this frame's mouth, not the last one.
//
// EVERY MACHINE ANALYSES THE VOICE IT HEARS. The wearer's own machine taps the microphone straight
// after the high pass, ahead of the suppressor and the level control, because the suppressor alone
// runs 20 ms behind; that tap is measured against the voice's own running level, and the
// suppressor's voice probability closes the mouth on steady noise. Everybody else analyses the voice
// as it is decoded, stamps each window with its place in the stream, and shows the window for the
// sample coming out of the speakers right now. A mouth timed off a separate stream of weights ran
// ahead of the voice by whatever sat in the jitter buffer and the device, and was eased twice on the
// way; timed off the playback cursor it lands with the sound whatever those buffers do. A muted or
// silenced talker sends nothing, nothing is decoded, and the mouth closes by itself. -xlinka
[ComponentCategory("Users/Avatar")]
[DefaultUpdateOrder(-200)]
public sealed class LipSyncAnalyzer : Component
{
    // Mouth shut once nothing new has come off the microphone for this long.
    private const float CaptureIdleSeconds = 0.25f;

    // How strong the mouth gets overall. A per-avatar trim for rigs whose shapes are authored hot.
    public readonly Sync<float> Strength;

    // Shown on the world thread.
    private readonly VisemeAnalysis _analysis = new();
    private readonly float[] _scratch = new float[VisemeAnalysis.Count];

    // Fed on the voice chain thread or the sync thread, always under _feedLock. Built on first use so
    // an avatar sitting in an inventory carries no window buffers.
    private readonly object _feedLock = new();
    private VisemeAnalysis? _source;
    private VisemeFrames? _frames;
    private AudioInput? _tapped;
    private AudioStream? _remote;
    private bool _hasDecoded;
    private long _nextDecoded;

    private readonly VisemeClock _clock = new();
    private bool _hasShown;
    private long _shownStamp;
    private float _sinceCapture;

    private User? _user;
    private readonly AvatarFaceWearer _wearer = new();

    // Whose mouth this is follows who wears the avatar, never whose hierarchy it sits in, so only the wearer's
    // own machine taps a microphone.
    private bool IsWornLocally => _user != null && ReferenceEquals(_user, World?.LocalUser);

    public LipSyncAnalyzer()
    {
        Strength = new Sync<float>(this, 1f);
    }

    // Silence weight (1 = no speech). Drivers use it to suppress mouth tracking.
    public float Silence => _analysis[VisemeAnalysis.Silence];

    // How loud the voice is this frame, 0..1: everything the mouth is doing that is not silence, which the
    // analysis already scales by loudness. A jaw that only opens and shuts follows this.
    public float Level => System.Math.Clamp((1f - _analysis[VisemeAnalysis.Silence]) * System.Math.Max(Strength.Value, 0f), 0f, 1f);

    // The viseme the mouth is closest to this frame, 0 (silence) to 14, in viseme order.
    public int Dominant
    {
        get
        {
            int dominant = VisemeAnalysis.Silence;
            float highest = _analysis[VisemeAnalysis.Silence];
            for (int i = 1; i < VisemeAnalysis.Count; i++)
            {
                if (_analysis[i] > highest)
                {
                    highest = _analysis[i];
                    dominant = i;
                }
            }
            return dominant;
        }
    }

    public float this[Viseme viseme]
    {
        get
        {
            int i = (int)viseme;
            if (i < 0 || i >= VisemeAnalysis.Count)
                return 0f;
            return System.Math.Clamp(_analysis[i] * System.Math.Max(Strength.Value, 0f), 0f, 1f);
        }
    }

    public override void OnAwake()
    {
        base.OnAwake();
        if (Slot != null)
            Slot.ActiveChanged += OnSlotActiveChanged;
    }

    // Switched off, it lets go of the microphone and the stream now rather than when it next updates.
    private void OnSlotActiveChanged(Slot slot)
    {
        if (Slot == null || Slot.IsActive)
            return;
        StopFeeds();
        _user = null;
        _analysis.Reset();
    }

    public override void OnDestroy()
    {
        if (Slot != null)
            Slot.ActiveChanged -= OnSlotActiveChanged;
        StopFeeds();
        base.OnDestroy();
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();

        // A new wearer, or none: let go of the last one's microphone and stream and start from silence.
        if (_wearer.Update(Slot))
        {
            StopFeeds();
            _user = null;
            _analysis.Reset();
        }
        var wearer = _wearer.Current;
        if (wearer == null)
            return;
        _user = wearer.ActiveUser;
        if (_user == null)
            return;

        float delta = World?.Time.Delta ?? 0f;

        if (IsWornLocally)
        {
            StopListening();
            // The voice stream's own rule: muted, silenced or looking at another world, the microphone
            // is let go and the mouth stays shut.
            if (AudioInput.IsVoiceCaptureAllowed(_user, World))
            {
                EnsureTap(wearer);
                ShowCaptured(delta);
            }
            else
            {
                Untap();
                _analysis.AcceptSilence();
            }
        }
        else
        {
            Untap();
            EnsureListening(wearer);
            ShowHeard(delta);
        }

        _analysis.Advance(delta);
    }

    private void ShowCaptured(float delta)
    {
        if (_frames != null && _frames.TakeLatest(_scratch))
        {
            _analysis.Accept(_scratch);
            _sinceCapture = 0f;
            return;
        }

        _sinceCapture += delta;
        if (_sinceCapture >= CaptureIdleSeconds)
            _analysis.AcceptSilence();
    }

    // The window for the sample being heard: the stream's read cursor less the output latency, both
    // live, so a jitter buffer or a device that grows or shrinks moves the mouth with the sound.
    private void ShowHeard(float delta)
    {
        var stream = _remote;
        var frames = _frames;
        if (stream == null || frames == null)
        {
            _analysis.AcceptSilence();
            return;
        }

        double latency = Engine.Current?.AudioSystem?.OutputLatencySeconds ?? 0.0;
        long heard = _clock.Update(stream.PlaybackIndex, latency, stream.SampleRate, delta);
        if (frames.TakeUpTo(heard, _scratch, out long stamp))
        {
            _analysis.Accept(_scratch);
            _shownStamp = stamp;
            _hasShown = true;
        }

        // Nothing new reaching the ear, or past the last window analysed: the voice has stopped.
        if (_clock.IsIdle || !_hasShown || heard - _shownStamp >= VisemeAnalysis.WindowSize)
            _analysis.AcceptSilence();
    }

    // The input the voice stream captures from, so the mouth listens to the microphone the room hears.
    private static UserAudioStream? VoiceOf(UserRoot wearer)
        => wearer.GetRegisteredComponent<AvatarVoiceInfo>()?.AudioSource.Target as UserAudioStream;

    private void EnsureTap(UserRoot wearer)
    {
        var input = VoiceOf(wearer)?.CaptureInput ?? Engine.Current?.AudioSystem?.DefaultAudioInput;
        if (ReferenceEquals(input, _tapped))
            return;

        Untap();
        if (input == null)
            return;

        lock (_feedLock)
        {
            var frames = _frames ??= new VisemeFrames();
            var source = _source ??= new VisemeAnalysis();
            source.Reset();
            source.TracksLevel = true;
            source.VoiceProbability = 1f;
            source.Output = frames;
            frames.Clear();
            _tapped = input;
        }
        _sinceCapture = 0f;
        input.HighPassedSamples += OnHighPassedSamples;
    }

    private void Untap()
    {
        var input = _tapped;
        if (input == null)
            return;
        input.HighPassedSamples -= OnHighPassedSamples;
        lock (_feedLock)
            _tapped = null;
    }

    private void EnsureListening(UserRoot wearer)
    {
        var stream = VoiceOf(wearer)?.Stream.Target;
        if (stream != null && stream.IsDestroyed)
            stream = null;
        if (ReferenceEquals(stream, _remote))
            return;

        StopListening();
        if (stream == null)
            return;

        lock (_feedLock)
        {
            var frames = _frames ??= new VisemeFrames();
            var source = _source ??= new VisemeAnalysis();
            source.Reset();
            source.TracksLevel = false;
            source.VoiceProbability = 1f;
            source.Output = frames;
            frames.Clear();
            _hasDecoded = false;
            _remote = stream;
        }
        _clock.Reset();
        _hasShown = false;
        stream.SamplesDecoded += OnSamplesDecoded;
    }

    private void StopListening()
    {
        var stream = _remote;
        if (stream == null)
            return;
        stream.SamplesDecoded -= OnSamplesDecoded;
        lock (_feedLock)
            _remote = null;
    }

    private void StopFeeds()
    {
        Untap();
        StopListening();
    }

    // VOICE CHAIN THREAD. The analysis owns its buffers and touches nothing in the datamodel.
    private void OnHighPassedSamples(AudioInput input, StereoSample[] buffer, int count)
    {
        if (buffer == null || count <= 0)
            return;

        lock (_feedLock)
        {
            var source = _source;
            if (source == null || !ReferenceEquals(input, _tapped))
                return;
            try
            {
                source.VoiceProbability = input.VoiceProbability;
                source.Write(buffer.AsSpan(0, System.Math.Min(count, buffer.Length)), input.TargetSampleRate);
            }
            catch
            {
                // A throw here lands in the voice chain. A bad frame is a bad frame.
            }
        }
    }

    // SYNC THREAD, as each packet is decoded.
    private void OnSamplesDecoded(AudioStream stream, ReadOnlySpan<StereoSample> samples, long firstIndex)
    {
        lock (_feedLock)
        {
            var source = _source;
            if (source == null || !ReferenceEquals(stream, _remote))
                return;
            try
            {
                // Audio that does not follow on from the last packet, or that lands in a ring the mixer
                // had already read dry (the talker paused), starts a fresh window rather than finishing
                // one with the tail of the last sentence.
                if (!_hasDecoded || firstIndex != _nextDecoded || stream.PlaybackIndex >= firstIndex)
                {
                    if (_hasDecoded && firstIndex < _nextDecoded)
                        _frames?.Clear();
                    source.Restart();
                }
                source.Write(samples, stream.SampleRate, firstIndex);
                _nextDecoded = firstIndex + samples.Length;
                _hasDecoded = true;
            }
            catch
            {
                // A throw here lands in the sync loop. A bad packet is a still mouth.
            }
        }
    }
}
