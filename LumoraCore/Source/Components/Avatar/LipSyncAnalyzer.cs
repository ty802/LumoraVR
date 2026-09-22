// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;
using Lumora.Core;
using Lumora.Core.Networking.Streams;
using AudioInput = Lumora.Core.Audio.AudioInput;

namespace Lumora.Core.Components.Avatar;

// Per-viseme weights from the owning user's voice. Consumed by VisemeWeightDriver.
//
// ANALYSE ONCE, SEND THE ANSWER. The machine that owns the voice runs the analysis on its own
// captured audio, before the codec, and puts the fifteen weights on a stream. Every other machine
// reads that stream. The alternative - each peer analysing the audio it decoded - is the same work
// repeated once per listener per speaker, on a signal that has already lost detail to the codec, and
// it drifts apart the moment one peer loses a packet. Fifteen quantized floats at twenty a second is
// a few hundred bytes a second per speaker, which is less than the audio it describes by two orders
// of magnitude.
//
// The capture tap is the FILTERED stream, the same one the voice codec gets, so the mouth moves for
// exactly the audio the room hears: gated frames move nothing, and a suppressed fan moves nothing
// either. -xlinka
[ComponentCategory("Users/Avatar")]
public sealed class LipSyncAnalyzer : Component
{
    private const string StreamName = "Visemes";

    // Twenty frames a second. Faster buys nothing a mouth can show; the smoothing on either end
    // carries the shape between them.
    private const uint StreamPeriod = 3;

    // How strong the mouth gets overall. A per-avatar trim for rigs whose shapes are authored hot.
    public readonly Sync<float> Strength;

    private readonly VisemeAnalysis _analysis = new();
    private readonly float[] _scratch = new float[VisemeAnalysis.Count];

    private FloatArrayValueStream? _stream;
    private AudioInput? _tapped;
    private bool _subscribed;
    private User? _user;

    public LipSyncAnalyzer()
    {
        Strength = new Sync<float>(this, 1f);
    }

    // Silence weight (1 = no speech). Drivers use it to suppress mouth tracking.
    public float Silence => _analysis[VisemeAnalysis.Silence];

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

    public override void OnStart()
    {
        base.OnStart();
        EnsureStream();
    }

    public override void OnDestroy()
    {
        Untap();
        base.OnDestroy();
    }

    private void EnsureStream()
    {
        if (_stream != null && !_stream.IsDestroyed)
            return;

        _user = Slot?.ActiveUserRoot?.ActiveUser;
        if (_user == null)
            return;

        if (IsUnderLocalUser)
        {
            _stream = _user.GetStreamOrAdd<FloatArrayValueStream>(StreamName, s =>
            {
                s.Count = VisemeAnalysis.Count;
                s.Encoding = ValueEncoding.Quantized;
                // Ten bits per weight. A mouth shape has nowhere near a thousand distinguishable
                // positions, so anything finer is bandwidth spent on numbers nobody can see.
                s.FullFrameBits = 10;
                s.FullFrameMin = 0f;
                s.FullFrameMax = 1f;
                s.SetInterpolation();
                s.SetUpdatePeriod(StreamPeriod, 0);
            });
        }
        else
        {
            _stream = _user.GetStream<FloatArrayValueStream>(s => s.Name == StreamName);
        }
    }

    // The local capture, tapped after the gate and the suppressor. Re-resolved rather than cached
    // forever because the device can change under us when somebody picks another microphone.
    private void EnsureTap()
    {
        var input = Engine.Current?.AudioSystem?.DefaultAudioInput;
        if (ReferenceEquals(input, _tapped) && _subscribed)
            return;

        Untap();
        if (input == null)
            return;

        _tapped = input;
        _tapped.NewFilteredSamples += OnFilteredSamples;
        _subscribed = true;
        _analysis.Reset();
    }

    private void Untap()
    {
        if (_subscribed && _tapped != null)
            _tapped.NewFilteredSamples -= OnFilteredSamples;
        _subscribed = false;
        _tapped = null;
    }

    // CAPTURE THREAD. The analysis owns its own buffers and touches nothing in the datamodel; the
    // weights are read back on the world thread, where a torn read of a float is not a thing.
    private void OnFilteredSamples(ReadOnlySpan<StereoSample> samples)
    {
        try
        {
            _analysis.Write(samples, _tapped?.TargetSampleRate ?? 48000);
        }
        catch
        {
            // A throw here rides up into the platform's capture callback. A bad frame is a bad frame.
        }
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();

        EnsureStream();

        float delta = World?.Time.Delta ?? 0f;

        if (IsUnderLocalUser)
        {
            EnsureTap();
            _analysis.Advance(delta);

            var stream = _stream;
            if (stream != null && !stream.IsDestroyed)
            {
                _analysis.CopyTo(_scratch);
                for (int i = 0; i < _scratch.Length; i++)
                    stream[i] = _scratch[i];
            }
            return;
        }

        // Remote: whatever the owner worked out, eased the same way so a dropped packet holds the
        // shape instead of snapping the mouth shut.
        var remote = _stream;
        if (remote == null || remote.IsDestroyed)
            return;

        int count = System.Math.Min(remote.Count, _scratch.Length);
        for (int i = 0; i < count; i++)
            _scratch[i] = remote[i];
        _analysis.Accept(_scratch.AsSpan(0, count));
        _analysis.Advance(delta);
    }
}
