// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;
using Lumora.Audio;

namespace Lumora.Core.Audio;

// What the voice filter chain is set to. Copied out of the settings once a frame on the world thread
// and read field by field on the capture thread: every member is a float or a bool, so a torn read is
// not possible and the worst a mid-frame change costs is one frame filtered with a mixed set. -xlinka
public struct VoiceFilterSettings
{
    public bool Normalization;
    public bool NoiseSuppression;
    public float GateThreshold;
    public float GateAttack;
    public float GateHold;
    public float GateRelease;
    // Voiced speech level the leveller steers toward, RMS in dBFS.
    public float TargetLevel;
    // Fixed gain on the microphone ahead of the gate and the leveller, dB.
    public float MicrophoneGain;

    public static VoiceFilterSettings Default => new VoiceFilterSettings
    {
        Normalization = true,
        NoiseSuppression = true,
        GateThreshold = 0.02f,
        GateAttack = 0.01f,
        GateHold = 0.2f,
        GateRelease = 0.2f,
        TargetLevel = -18.5f,
        MicrophoneGain = 0f,
    };
}

// A capture device as the engine sees it: samples come in at whatever the platform runs at and leave
// as 48 kHz stereo streams: untouched, high passed only, and filtered.
//
// SamplesCaptured only copies the samples into a backlog and wakes the chain thread, because the
// platform calls it from wherever it polls (Godot polls on the main thread) and the noise suppressor is
// a neural network: a frame of it there is a frame of VR budget. Every event fires on the chain thread.
// The chain allocates on the first frame and on a frame larger than any before it, and never again:
// the scratch buffers are fields, the filter state is fields, and the settings are plain values pushed
// in from the world thread. No datamodel, no engine locks, nothing that can block behind a world update.
//
// The device only runs while somebody is listening. The events count their own subscribers and start
// or stop the platform device on the edges, so sitting in a menu with nothing subscribed leaves the
// microphone closed rather than open and discarded. -xlinka
public sealed class AudioInput : IDisposable
{
    // A chain that falls this far behind drops the oldest audio rather than growing the backlog.
    private const int MaxBacklogSeconds = 1;

    private const float HighPassHz = 80f;

    // THE LEVELLER. A peak target makes a sharp-edged voice quiet and a soft one loud, and moves on
    // every frame over the gate, breaths included. This measures speech the way a listener hears it:
    // power over 10 ms frames that carry voice and sit within 20 dB of the running estimate, averaged
    // over a second and a half. The gain walks toward whatever puts that at the target, up slowly so a
    // pause never swells, down faster so a shout is caught, and only while the gate is open, so a pause
    // holds the gain where the last word left it. The -1 dBFS limiter after it takes the peaks the gain
    // pushes past full scale. -xlinka
    private const float LevelSeconds = 1.5f;
    private const float LevelWindowDb = 20f;
    private const float MinLevellerGainDb = -12f;
    private const float MaxLevellerGainDb = 24f;
    private const float RiseDbPerSecond = 6f;
    private const float FallDbPerSecond = 20f;

    // A frame this quiet is not speech whatever the VAD says.
    private const float LevelFloorDb = -70f;

    // Voiced frames held out by the window for this long mean the microphone got quieter for good (a
    // different device, a capsule turned away), so the next one is let in and the estimate follows.
    private const float WindowEscapeSeconds = 2f;

    // RNNoise's VAD at or above this is speech.
    private const float VoiceThreshold = 0.5f;

    // Seconds of speech before the leveller's gain is worth remembering for the device.
    private const float SettleSeconds = 3f;
    private const float SettleToleranceDb = 1f;

    // RNNoise, and it runs BEFORE the gate for a reason: the gate decides whether the frame is voice,
    // and it decides better when the fan has already been taken out from under it. A quiet talker in a
    // loud room reads as noise to a gate measuring the raw peak, and reads as voice once the room is
    // gone. Costs 20 to 30 ms of latency (the network's own 20 ms plus up to a frame of buffering) and
    // well under a millisecond of this thread per 10 ms frame. -xlinka
    private readonly VoiceDenoiser _noiseSuppressor = new();

    // 5 ms of lookahead, which is 5 ms more latency on every voice.
    private readonly LimiterFilter _limiter = new(AudioSimulator.DefaultSampleRate, LimiterSettings.Default);

    private readonly IAudioDeviceInput _device;
    private readonly object _sync = new object();

    private AudioSamplesHandler? _raw;
    private AudioSamplesHandler? _filtered;
    private Action<AudioInput, StereoSample[], int>? _highPassed;
    private int _subscribers;
    private volatile bool _disposed;

    private StereoSample[] _resampled = Array.Empty<StereoSample>();
    private StereoSample[] _filterScratch = Array.Empty<StereoSample>();

    private double _resamplePosition;
    private StereoSample _resampleLast;

    private float _highPassLastInLeft;
    private float _highPassLastInRight;
    private float _highPassLastOutLeft;
    private float _highPassLastOutRight;

    private float _gateGain;
    private float _gateHoldRemaining;
    private bool _gateOpen;

    private float _levellerGainDb;
    // Mean square of voiced speech after the microphone gain; 0 until a voiced frame has been heard.
    private double _speechPower;
    private double _framePower;
    private int _frameFill;
    private bool _frameVoiced;
    private float _voicedSeconds;
    private float _windowHeldOut;
    private volatile float _settledGainDb = float.NaN;
    private volatile float _voiceProbability;

    // The leveller's gain this device settled at last time, for the next open to start from. Written on
    // the subscribing thread before the reset it is read in; NaN for a device never heard before.
    private volatile float _gainSeedDb = float.NaN;
    private string? _seedDeviceId;

    private VoiceFilterSettings _settings = VoiceFilterSettings.Default;

    private readonly object _handoff = new object();
    private readonly AutoResetEvent _wake = new AutoResetEvent(false);
    private StereoSample[] _backlog = Array.Empty<StereoSample>();
    private int _backlogCount;
    private StereoSample[] _batch = Array.Empty<StereoSample>();
    private Thread? _chain;
    // Set when the device starts; the chain clears its own state before the next batch, since it is the
    // only thread that touches that state.
    private volatile bool _resetPending;

    public AudioInput(IAudioDeviceInput device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _device.SamplesCaptured += OnSamplesCaptured;
    }

    public string Name => _device.Name;

    public string DeviceId => _device.DeviceId;

    public bool IsSystemDefault => _device.IsSystemDefault;

    // Set when the platform enumerated this as an endpoint and so knows what it carries. Null for the one
    // device the engine's own audio layer captures from, which is only ever a name.
    public AudioEndpointKind? EndpointKind => (_device as IAudioEndpointInput)?.Kind;

    // Microphone, system audio or virtual cable. An endpoint says which; anything else is worked out
    // from the name, because that is all there is. See AudioInputTypes. Cached: neither changes under
    // a device.
    public AudioInputType InputType => _inputType ??= EndpointKind is AudioEndpointKind.OutputLoopback or AudioEndpointKind.SystemLoopback
        ? AudioInputType.Loopback
        : AudioInputTypes.Classify(_device.Name);
    private AudioInputType? _inputType;

    public bool IsLoopback => AudioInputTypes.IsLoopback(InputType);

    // A device that stopped answering is still registered, so the list does not churn every time a
    // headset sleeps; it just stops being a candidate for the default.
    public bool IsConnected => !_disposed && _device.SampleRate > 0;

    public bool IsCapturing => _device.IsCapturing;

    public bool IsActive => !_disposed && _device.IsCapturing && _subscribers > 0;

    // The rate the mixer runs at, which is what both events are resampled to. Pushed in by the audio
    // system at registration; 48 kHz until then because that is the simulator's own default.
    public int TargetSampleRate { get; internal set; } = AudioSimulator.DefaultSampleRate;

    // A remove that finds no such handler changes nothing. Counting it anyway walked the count below
    // zero, and the next subscriber then left the microphone closed. -xlinka
    public event AudioSamplesHandler NewRawSamples
    {
        add
        {
            if (value == null) return;
            lock (_sync)
            {
                _raw += value;
                _subscribers++;
                ApplyCaptureState();
            }
        }
        remove
        {
            if (value == null) return;
            lock (_sync)
            {
                var before = _raw;
                _raw -= value;
                if (ReferenceEquals(before, _raw))
                    return;
                _subscribers--;
                ApplyCaptureState();
            }
        }
    }

    public event AudioSamplesHandler NewFilteredSamples
    {
        add
        {
            if (value == null) return;
            lock (_sync)
            {
                _filtered += value;
                _subscribers++;
                ApplyCaptureState();
            }
        }
        remove
        {
            if (value == null) return;
            lock (_sync)
            {
                var before = _filtered;
                _filtered -= value;
                if (ReferenceEquals(before, _filtered))
                    return;
                _subscribers--;
                ApplyCaptureState();
            }
        }
    }

    // The microphone past the high pass and BEFORE the suppressor, on the chain thread, on the same
    // blocks the chain runs: (input, buffer, count). The buffer is the chain's own scratch, so read it
    // during the call and leave it as it is. A subscriber keeps the whole chain running, so
    // VoiceProbability stays live for it. -xlinka
    public event Action<AudioInput, StereoSample[], int>? HighPassedSamples
    {
        add
        {
            if (value == null) return;
            lock (_sync)
            {
                _highPassed += value;
                _subscribers++;
                ApplyCaptureState();
            }
        }
        remove
        {
            if (value == null) return;
            lock (_sync)
            {
                var before = _highPassed;
                _highPassed -= value;
                if (ReferenceEquals(before, _highPassed))
                    return;
                _subscribers--;
                ApplyCaptureState();
            }
        }
    }

    // RNNoise's latest read of the microphone, 0 for noise and 1 for a voice. With suppression off
    // there is no VAD, so it is 1 while the gate is open and 0 while it is shut. 0 while the device
    // is closed.
    public float VoiceProbability => _voiceProbability;

    // Whether this machine may capture a voice for user in world right now: not with the microphone
    // muted here, not for a user who muted themselves or whom the host silenced, and not into a world
    // that is not the one being looked at. World thread.
    public static bool IsVoiceCaptureAllowed(User? user, World? world)
        => user != null && world != null && world.IsFocused
           && !EngineSettings.MicrophoneMuted && !user.IsMuted.Value && !user.IsSilenced.Value;

    // A level meter wants the device open without wanting the samples, and an event handler that does
    // nothing would still be invoked once a frame per capture buffer. These count on the same edge the
    // events count on, so the device starts and stops the same way. Balanced calls only: a stray
    // release would close the device out from under a live voice stream. -xlinka
    public void AddSubscriber()
    {
        if (_disposed)
            return;
        lock (_sync)
        {
            _subscribers++;
            ApplyCaptureState();
        }
    }

    public void RemoveSubscriber()
    {
        if (_disposed)
            return;
        lock (_sync)
        {
            if (_subscribers <= 0)
                return;
            _subscribers--;
            ApplyCaptureState();
        }
    }

    public void ApplySettings(in VoiceFilterSettings settings)
    {
        _settings.Normalization = settings.Normalization;
        _settings.NoiseSuppression = settings.NoiseSuppression;
        _settings.GateThreshold = Sanitize(settings.GateThreshold, 0f, 1f, 0.02f);
        _settings.GateAttack = Sanitize(settings.GateAttack, 0.001f, 1f, 0.01f);
        _settings.GateHold = Sanitize(settings.GateHold, 0f, 5f, 0.2f);
        _settings.GateRelease = Sanitize(settings.GateRelease, 0.001f, 5f, 0.2f);
        _settings.TargetLevel = Sanitize(settings.TargetLevel, SettingsCatalog.VoiceTargetLevel.Min, SettingsCatalog.VoiceTargetLevel.Max, -18.5f);
        _settings.MicrophoneGain = Sanitize(settings.MicrophoneGain, SettingsCatalog.MicrophoneGain.Min, SettingsCatalog.MicrophoneGain.Max, 0f);
    }

    // PLATFORM THREAD

    private void OnSamplesCaptured(ReadOnlySpan<StereoSample> samples)
    {
        if (_disposed || samples.IsEmpty)
            return;

        try
        {
            lock (_handoff)
            {
                int cap = MaxBacklogSeconds * System.Math.Max(_device.SampleRate, 8000);
                if (samples.Length >= cap)
                {
                    samples = samples.Slice(samples.Length - cap);
                    _backlogCount = 0;
                }
                else if (_backlogCount + samples.Length > cap)
                {
                    int drop = _backlogCount + samples.Length - cap;
                    Array.Copy(_backlog, drop, _backlog, 0, _backlogCount - drop);
                    _backlogCount -= drop;
                }

                if (_backlog.Length < _backlogCount + samples.Length)
                    Array.Resize(ref _backlog, GrownSize(_backlog.Length, _backlogCount + samples.Length));
                samples.CopyTo(_backlog.AsSpan(_backlogCount));
                _backlogCount += samples.Length;

                if (_chain == null)
                {
                    _chain = new Thread(RunChain) { IsBackground = true, Name = "Lumora Voice Chain" };
                    _chain.Start();
                }
            }
            _wake.Set();
        }
        catch
        {
            // A throw here goes up into the platform's capture callback and takes the device with it.
        }
    }

    // CHAIN THREAD

    private void RunChain()
    {
        while (!_disposed)
        {
            _wake.WaitOne(250);

            int count;
            lock (_handoff)
            {
                count = _backlogCount;
                if (count > 0)
                {
                    if (_batch.Length < count)
                        _batch = new StereoSample[GrownSize(_batch.Length, count)];
                    Array.Copy(_backlog, _batch, count);
                    _backlogCount = 0;
                }
            }

            if (_resetPending)
            {
                _resetPending = false;
                ResetFilterState();
            }

            if (count > 0 && !_disposed)
                Process(_batch.AsSpan(0, count));
        }
    }

    private void Process(ReadOnlySpan<StereoSample> samples)
    {
        try
        {
            int deviceRate = _device.SampleRate;
            int targetRate = TargetSampleRate;

            ReadOnlySpan<StereoSample> atTargetRate;
            if (deviceRate > 0 && targetRate > 0 && deviceRate != targetRate)
            {
                int produced = Resample(samples, (double)deviceRate / targetRate);
                atTargetRate = _resampled.AsSpan(0, produced);
            }
            else
            {
                atTargetRate = samples;
            }

            if (atTargetRate.IsEmpty)
                return;

            _raw?.Invoke(atTargetRate);

            int rate = targetRate > 0 ? targetRate : AudioSimulator.DefaultSampleRate;
            EnsureCapacity(ref _filterScratch, atTargetRate.Length);
            var work = _filterScratch.AsSpan(0, atTargetRate.Length);
            atTargetRate.CopyTo(work);

            HighPass(work, rate);

            var filtered = _filtered;
            var highPassed = _highPassed;
            if (filtered == null && highPassed == null)
            {
                // Nobody wants the filtered stream, but a meter still wants a number. The peak is taken
                // where the filter would take it, past the high pass and the microphone gain, so the bar
                // reads the same whether or not a voice stream is running. -xlinka
                LastPeak = Peak(work) * DbToGain(_settings.MicrophoneGain);
                _voiceProbability = 0f;
                return;
            }

            if (highPassed != null)
            {
                try
                {
                    highPassed.Invoke(this, _filterScratch, work.Length);
                }
                catch
                {
                    // A tap that throws loses its own block, not the voice.
                }
            }

            // The suppressor buffers, so what comes back is not the length that went in and the rest
            // of the chain has to run on what it actually produced.
            int filteredLength = Filter(work, rate);
            if (filteredLength > 0)
                filtered?.Invoke(work.Slice(0, filteredLength));
        }
        catch
        {
            // A throw here would end the chain thread, and every frame after it with it. A bad frame is
            // a dropped frame.
        }
    }

    // Linear, with the fractional cursor and the sample before it carried across calls so consecutive
    // buffers interpolate across the seam instead of restarting at zero every time. rate is source
    // samples advanced per output sample.
    private int Resample(ReadOnlySpan<StereoSample> source, double rate)
    {
        if (!(rate > 0.0) || double.IsInfinity(rate))
            rate = 1.0;

        double position = _resamplePosition;
        if (!(position >= -1.0))
            position = -1.0;

        int last = source.Length - 1;
        int capacity = (int)((source.Length - position) / rate) + 2;
        EnsureCapacity(ref _resampled, capacity);

        int produced = 0;
        var target = _resampled;
        while (position <= last && produced < target.Length)
        {
            int index = (int)System.Math.Floor(position);
            float fraction = (float)(position - index);
            StereoSample a = index < 0 ? _resampleLast : source[index];
            StereoSample b = index < last ? source[index + 1] : a;
            target[produced++] = a.LerpTo(b, fraction);
            position += rate;
        }

        _resampleLast = source[last];
        // Rebase onto the next call's span; the cursor lands in (-1, rate - 1].
        _resamplePosition = position - source.Length;
        return produced;
    }

    // Peak of the last captured frame past the microphone gain and before the gate, for a level meter.
    // Written on the audio thread, read anywhere; a torn read of one float is not a thing.
    public float LastPeak { get; private set; }

    // Returns how many samples of buffer are valid on the way out, which is not always how many went
    // in: the suppressor works in whole 10 ms frames and runs behind. The high pass has already run.
    private int Filter(Span<StereoSample> buffer, int sampleRate)
    {
        bool voiceActivity = false;
        if (_settings.NoiseSuppression)
        {
            int produced = _noiseSuppressor.Process(buffer, sampleRate);
            if (produced <= 0)
            {
                // Still filling the first window. The meter should not flatline while that happens.
                LastPeak = 0f;
                return 0;
            }
            buffer = buffer.Slice(0, produced);
            voiceActivity = _noiseSuppressor.OutputRunCount > 0;
        }

        float trim = DbToGain(_settings.MicrophoneGain);
        LastPeak = Peak(buffer) * trim;

        // The gate and the leveller step in 10 ms frames. With the suppressor running those are its own
        // frames, each with the VAD it gave.
        int frameSize = System.Math.Max(1, sampleRate / 100);
        int runs = voiceActivity ? _noiseSuppressor.OutputRunCount : 0;
        int run = 0;
        int at = 0;
        while (at < buffer.Length)
        {
            int length;
            float voice;
            if (run < runs)
            {
                length = _noiseSuppressor.OutputRunLength(run);
                voice = _noiseSuppressor.OutputRunVoiceProbability(run);
                run++;
            }
            else
            {
                length = System.Math.Max(1, frameSize - _frameFill);
                voice = -1f;
            }
            length = System.Math.Min(length, buffer.Length - at);
            Level(buffer.Slice(at, length), sampleRate, frameSize, trim, voice);
            at += length;
        }

        _voiceProbability = voiceActivity ? _noiseSuppressor.VoiceProbability : (_gateOpen ? 1f : 0f);
        _limiter.Process(buffer, sampleRate);
        return buffer.Length;
    }

    // One frame, or the part of one a call boundary left, through the gate and the leveller. voice is
    // that frame's VAD, negative when nothing measured one.
    private void Level(Span<StereoSample> segment, int sampleRate, int frameSize, float trim, float voice)
    {
        float seconds = segment.Length / (float)sampleRate;

        double power = 0.0;
        float peak = 0f;
        for (int i = 0; i < segment.Length; i++)
        {
            float left = segment[i].left * trim;
            float right = segment[i].right * trim;
            power += (left * left + right * right) * 0.5;
            float amplitude = MathF.Max(MathF.Abs(left), MathF.Abs(right));
            if (amplitude > peak)
                peak = amplitude;
        }

        // GATE. Opens on the VAD when the suppressor gives one, or on the peak AFTER all the gain, so a
        // quiet microphone the leveller brings up is judged by how it will sound and not shut out by a
        // threshold that was only ever right for a loud one. Held open for a while after the last voiced
        // frame so a pause between words is not a hole in the sentence, then ramped per sample so the
        // open and the close are slopes rather than steps. -xlinka
        bool voiced = voice >= VoiceThreshold;
        bool loud = peak * DbToGain(_levellerGainDb) >= _settings.GateThreshold;
        if (voiced || loud)
            _gateHoldRemaining = _settings.GateHold;
        else
            _gateHoldRemaining -= seconds;
        bool open = voiced || loud || _gateHoldRemaining > 0f;
        _gateOpen = open;

        // A frame counts toward the speech level when the VAD calls it voice, or when the gate is open
        // if there is no VAD.
        _framePower += power;
        _frameFill += segment.Length;
        if (voice >= 0f ? voiced : open)
            _frameVoiced = true;
        if (_frameFill >= frameSize)
        {
            if (_frameVoiced)
                MeasureSpeech(_framePower / _frameFill, _frameFill / (float)sampleRate);
            _framePower = 0.0;
            _frameFill = 0;
            _frameVoiced = false;
        }

        float from = _levellerGainDb;
        float to = from;
        if (!_settings.Normalization)
        {
            to = 0f;
        }
        else if (open && _speechPower > 0.0)
        {
            float want = System.Math.Clamp(_settings.TargetLevel - PowerToDb(_speechPower), MinLevellerGainDb, MaxLevellerGainDb);
            to = want > from
                ? MathF.Min(want, from + RiseDbPerSecond * seconds)
                : MathF.Max(want, from - FallDbPerSecond * seconds);
            if (_voicedSeconds >= SettleSeconds && MathF.Abs(want - to) <= SettleToleranceDb)
                _settledGainDb = to;
        }
        _levellerGainDb = to;

        float gateTarget = open ? 1f : 0f;
        float rampSeconds = open ? _settings.GateAttack : _settings.GateRelease;
        float gateStep = rampSeconds <= 0f ? 1f : 1f / (rampSeconds * sampleRate);
        float gainFrom = trim * DbToGain(from);
        float gainTo = trim * DbToGain(to);
        float inverseLength = 1f / segment.Length;
        for (int i = 0; i < segment.Length; i++)
        {
            if (_gateGain < gateTarget)
            {
                _gateGain += gateStep;
                if (_gateGain > gateTarget) _gateGain = gateTarget;
            }
            else if (_gateGain > gateTarget)
            {
                _gateGain -= gateStep;
                if (_gateGain < gateTarget) _gateGain = gateTarget;
            }

            float gain = gainFrom + (gainTo - gainFrom) * (i * inverseLength);
            segment[i] = segment[i].Multiply(_gateGain * gain);
        }
    }

    // A voiced frame's mean square into the speech level. One far under the level is a breath or a
    // trailing consonant and is left out, until enough of them in a row say the level itself is stale.
    private void MeasureSpeech(double meanSquare, float seconds)
    {
        if (!(meanSquare > 0.0) || PowerToDb(meanSquare) < LevelFloorDb)
            return;

        if (_speechPower <= 0.0)
        {
            _speechPower = meanSquare;
        }
        else if (PowerToDb(meanSquare) < PowerToDb(_speechPower) - LevelWindowDb)
        {
            _windowHeldOut += seconds;
            if (_windowHeldOut < WindowEscapeSeconds)
                return;
            _speechPower = meanSquare;
        }
        else
        {
            _speechPower += (meanSquare - _speechPower) * System.Math.Min(1.0, seconds / LevelSeconds);
        }

        _windowHeldOut = 0f;
        _voicedSeconds += seconds;
    }

    private static float DbToGain(float db) => MathF.Pow(10f, db / 20f);

    private static float PowerToDb(double power) => 10f * (float)System.Math.Log10(power);

    private static double DbToPower(float db) => System.Math.Pow(10.0, db / 10.0);

    private static float Peak(ReadOnlySpan<StereoSample> buffer)
    {
        float peak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float amplitude = buffer[i].AbsoluteAmplitude;
            if (amplitude > peak)
                peak = amplitude;
        }
        return peak;
    }

    // One pole, so it is two multiplies a sample and takes the rumble, the desk thump and the DC
    // offset a cheap capsule carries without touching anything a voice lives in.
    private void HighPass(Span<StereoSample> buffer, int sampleRate)
    {
        float rc = 1f / (2f * MathF.PI * HighPassHz);
        float dt = 1f / sampleRate;
        float alpha = rc / (rc + dt);

        for (int i = 0; i < buffer.Length; i++)
        {
            var sample = buffer[i];

            float outLeft = alpha * (_highPassLastOutLeft + sample.left - _highPassLastInLeft);
            float outRight = alpha * (_highPassLastOutRight + sample.right - _highPassLastInRight);

            _highPassLastInLeft = sample.left;
            _highPassLastInRight = sample.right;
            _highPassLastOutLeft = outLeft;
            _highPassLastOutRight = outRight;

            buffer[i] = new StereoSample(outLeft, outRight);
        }
    }

    private void ApplyCaptureState()
    {
        if (_disposed)
            return;

        try
        {
            if (_subscribers > 0)
            {
                if (!_device.IsCapturing)
                {
                    lock (_handoff)
                        _backlogCount = 0;
                    _seedDeviceId = DeviceId;
                    _gainSeedDb = EngineSettings.MicrophoneGainSeed(_seedDeviceId);
                    _settledGainDb = float.NaN;
                    _resetPending = true;
                    _device.Start();
                }
            }
            else if (_device.IsCapturing)
            {
                _device.Stop();
                // Otherwise the last frame before the close stays on every meter reading this. -xlinka
                LastPeak = 0f;
                _voiceProbability = 0f;
                KeepGainSeed();
            }
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"AudioInput: '{Name}' refused a capture state change: {ex.Message}");
        }
    }

    // What the leveller settled at goes back under the device it was opened on. A device switched under
    // a running capture keeps no seed from this run, since the gain may still be the old one's.
    private void KeepGainSeed()
    {
        float settled = _settledGainDb;
        string? device = _seedDeviceId;
        if (!float.IsFinite(settled) || string.IsNullOrEmpty(device) || !string.Equals(device, DeviceId, StringComparison.Ordinal))
            return;
        EngineSettings.SetMicrophoneGainSeed(device, settled);
    }

    private void ResetFilterState()
    {
        _resamplePosition = 0.0;
        _resampleLast = default;
        _highPassLastInLeft = 0f;
        _highPassLastInRight = 0f;
        _highPassLastOutLeft = 0f;
        _highPassLastOutRight = 0f;
        _gateGain = 0f;
        _gateHoldRemaining = 0f;
        _gateOpen = false;

        // A device heard before starts at the gain it settled at, with the speech level that gain was
        // settled for, so the first words are not left at unity while the leveller climbs.
        float seed = _gainSeedDb;
        bool seeded = float.IsFinite(seed);
        _levellerGainDb = seeded ? System.Math.Clamp(seed, MinLevellerGainDb, MaxLevellerGainDb) : 0f;
        _speechPower = seeded ? DbToPower(_settings.TargetLevel - _levellerGainDb) : 0.0;
        _framePower = 0.0;
        _frameFill = 0;
        _frameVoiced = false;
        _voicedSeconds = 0f;
        _windowHeldOut = 0f;
        _settledGainDb = float.NaN;
        _voiceProbability = 0f;
        _limiter.Reset();
        // A new device is a new room. The network's recurrent state carries the old one over and
        // suppresses the wrong noise until it re-learns, which sounds like the new microphone is broken.
        _noiseSuppressor.Reset();
    }

    private static void EnsureCapacity(ref StereoSample[] buffer, int length)
    {
        if (buffer.Length >= length)
            return;
        buffer = new StereoSample[GrownSize(buffer.Length, length)];
    }

    // Grown in steps rather than to the exact size, so a frame that wobbles by a sample does not
    // allocate every time it wobbles up.
    private static int GrownSize(int current, int length)
    {
        int size = current == 0 ? 1024 : current;
        while (size < length)
            size *= 2;
        return size;
    }

    private static float Sanitize(float value, float min, float max, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return fallback;
        return value < min ? min : (value > max ? max : value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        lock (_sync)
        {
            _raw = null;
            _filtered = null;
            _highPassed = null;
            _subscribers = 0;
        }

        try
        {
            _device.SamplesCaptured -= OnSamplesCaptured;
            if (_device.IsCapturing)
            {
                _device.Stop();
                KeepGainSeed();
            }
        }
        catch
        {
            // Shutdown: a device that will not close cleanly is not worth a throw on the way out.
        }

        _wake.Set();
        Thread? chain;
        lock (_handoff)
            chain = _chain;
        // The event goes only once the thread is out of its wait, or the wait throws on a dead handle.
        if (chain == null || chain.Join(500))
            _wake.Dispose();
    }
}
