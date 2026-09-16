// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;

namespace Lumora.Core.Audio;

// What the voice filter chain is set to. Copied out of the settings once a frame on the world thread
// and read field by field on the capture thread: every member is a float or a bool, so a torn read is
// not possible and the worst a mid-frame change costs is one frame filtered with a mixed set. -xlinka
public struct VoiceFilterSettings
{
    public bool Normalization;
    public float GateThreshold;
    public float GateAttack;
    public float GateHold;
    public float GateRelease;
    public float NormalizationTarget;

    public static VoiceFilterSettings Default => new VoiceFilterSettings
    {
        Normalization = true,
        GateThreshold = 0.02f,
        GateAttack = 0.01f,
        GateHold = 0.2f,
        GateRelease = 0.2f,
        NormalizationTarget = 0.1f,
    };
}

// A capture device as the engine sees it: samples come in at whatever the platform runs at and leave
// as two 48 kHz stereo streams, one untouched and one filtered.
//
// Everything past SamplesCaptured runs on the capture thread. It allocates on the first frame and on
// a frame larger than any before it, and never again: the scratch buffers are fields, the filter
// state is fields, and the settings are plain values pushed in from the world thread. No datamodel,
// no engine locks, nothing that can block behind a world update.
//
// The device only runs while somebody is listening. The events count their own subscribers and start
// or stop the platform device on the edges, so sitting in a menu with nothing subscribed leaves the
// microphone closed rather than open and discarded. -xlinka
public sealed class AudioInput : IDisposable
{
    private const float HighPassHz = 80f;

    // Below this a frame is silence as far as the normalizer is concerned; letting it chase a target
    // through a silent frame is how a noise floor becomes a roar.
    private const float NormalizationFloor = 0.001f;

    // How fast the normalizer walks toward its new gain, in seconds to cover most of the distance.
    private const float NormalizationTimeConstant = 0.75f;

    private const float MaxNormalizationGain = 4f;
    private const float MinNormalizationGain = 0.1f;

    private readonly IAudioDeviceInput _device;
    private readonly object _sync = new object();

    private AudioSamplesHandler? _raw;
    private AudioSamplesHandler? _filtered;
    private int _subscribers;
    private bool _disposed;

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
    private float _normalizationGain = 1f;

    private VoiceFilterSettings _settings = VoiceFilterSettings.Default;

    public AudioInput(IAudioDeviceInput device)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _device.SamplesCaptured += OnSamplesCaptured;
    }

    public string Name => _device.Name;

    public string DeviceId => _device.DeviceId;

    public bool IsSystemDefault => _device.IsSystemDefault;

    // A device that stopped answering is still registered, so the list does not churn every time a
    // headset sleeps; it just stops being a candidate for the default.
    public bool IsConnected => !_disposed && _device.SampleRate > 0;

    public bool IsCapturing => _device.IsCapturing;

    public bool IsActive => !_disposed && _device.IsCapturing && _subscribers > 0;

    // The rate the mixer runs at, which is what both events are resampled to. Pushed in by the audio
    // system at registration; 48 kHz until then because that is the simulator's own default.
    public int TargetSampleRate { get; internal set; } = AudioSimulator.DefaultSampleRate;

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
                _raw -= value;
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
                _filtered -= value;
                _subscribers--;
                ApplyCaptureState();
            }
        }
    }

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
        _settings.GateThreshold = Sanitize(settings.GateThreshold, 0f, 1f, 0.02f);
        _settings.GateAttack = Sanitize(settings.GateAttack, 0.001f, 1f, 0.01f);
        _settings.GateHold = Sanitize(settings.GateHold, 0f, 5f, 0.2f);
        _settings.GateRelease = Sanitize(settings.GateRelease, 0.001f, 5f, 0.2f);
        _settings.NormalizationTarget = Sanitize(settings.NormalizationTarget, 0.01f, 1f, 0.1f);
    }

    // CAPTURE THREAD

    private void OnSamplesCaptured(ReadOnlySpan<StereoSample> samples)
    {
        if (_disposed || samples.IsEmpty)
            return;

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

            var filtered = _filtered;
            if (filtered == null)
            {
                // Nobody wants the filtered stream, but a meter still wants a number. The peak is taken
                // where the filter would take it, past the high pass, so the bar reads the same whether
                // or not a voice stream is running. -xlinka
                HighPass(work, rate);
                LastPeak = Peak(work);
                return;
            }

            Filter(work, rate);
            filtered.Invoke(work);
        }
        catch
        {
            // A throw here goes up into the platform's capture callback and takes the device with it.
            // A bad frame is a dropped frame.
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

    // Peak of the last captured frame before the gate, for a level meter. Written on the audio
    // thread, read anywhere; a torn read of one float is not a thing.
    public float LastPeak { get; private set; }

    private void Filter(Span<StereoSample> buffer, int sampleRate)
    {
        HighPass(buffer, sampleRate);

        float peak = Peak(buffer);
        LastPeak = peak;

        float frameSeconds = buffer.Length / (float)sampleRate;

        // GATE. Measured on the frame's peak, held open for a while after the last loud frame so a
        // pause between words is not a hole in the sentence, then ramped per sample so the open and
        // the close are slopes rather than steps.
        float threshold = _settings.GateThreshold;
        bool loud = peak >= threshold;
        if (loud)
            _gateHoldRemaining = _settings.GateHold;
        else
            _gateHoldRemaining -= frameSeconds;

        bool open = loud || _gateHoldRemaining > 0f;
        float gateTarget = open ? 1f : 0f;
        float rampSeconds = open ? _settings.GateAttack : _settings.GateRelease;
        float gateStep = rampSeconds <= 0f ? 1f : 1f / (rampSeconds * sampleRate);

        // NORMALIZATION. Only a frame that is actually carrying voice moves the target, and the walk
        // toward it is slow enough that a shout does not duck the next word.
        float normalizationFrom = _normalizationGain;
        float normalizationTo = _normalizationGain;
        if (_settings.Normalization)
        {
            if (peak >= NormalizationFloor && peak >= threshold)
            {
                float desired = _settings.NormalizationTarget / peak;
                if (desired > MaxNormalizationGain) desired = MaxNormalizationGain;
                if (desired < MinNormalizationGain) desired = MinNormalizationGain;

                float blend = frameSeconds / NormalizationTimeConstant;
                if (blend > 1f) blend = 1f;
                normalizationTo = _normalizationGain + (desired - _normalizationGain) * blend;
            }
        }
        else
        {
            normalizationTo = 1f;
            normalizationFrom = _normalizationGain;
        }

        float inverseLength = buffer.Length > 0 ? 1f / buffer.Length : 0f;
        for (int i = 0; i < buffer.Length; i++)
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

            float normalization = normalizationFrom + (normalizationTo - normalizationFrom) * (i * inverseLength);
            buffer[i] = buffer[i].Multiply(_gateGain * normalization);
        }

        _normalizationGain = normalizationTo;
    }

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
                    ResetFilterState();
                    _device.Start();
                }
            }
            else if (_device.IsCapturing)
            {
                _device.Stop();
                // Otherwise the last frame before the close stays on every meter reading this. -xlinka
                LastPeak = 0f;
            }
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"AudioInput: '{Name}' refused a capture state change: {ex.Message}");
        }
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
        _normalizationGain = 1f;
    }

    private static void EnsureCapacity(ref StereoSample[] buffer, int length)
    {
        if (buffer.Length >= length)
            return;
        // Grown in steps rather than to the exact size, so a frame that wobbles by a sample does not
        // allocate every time it wobbles up.
        int size = buffer.Length == 0 ? 1024 : buffer.Length;
        while (size < length)
            size *= 2;
        buffer = new StereoSample[size];
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
            _subscribers = 0;
        }

        try
        {
            _device.SamplesCaptured -= OnSamplesCaptured;
            if (_device.IsCapturing)
                _device.Stop();
        }
        catch
        {
            // Shutdown: a device that will not close cleanly is not worth a throw on the way out.
        }
    }
}
