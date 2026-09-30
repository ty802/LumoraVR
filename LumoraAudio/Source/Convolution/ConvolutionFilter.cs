// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;

namespace Lumora.Audio;

// A stereo convolution reverb: the input played through a recorded room, by uniformly partitioned
// overlap-save (ConvolutionEngine). Mix, Level and PreDelay behave as they do on ReverbFilter.
//
// The room arrives as an ImpulseResponse built on a worker and handed over with SetResponse, which also
// builds the engine there, so the audio thread only ever swaps one object for another. Until a room has
// arrived the reverb is silent and the dry signal passes. A new room fades in over 50 ms while the old
// one fades out, and when the two share a block size the new one takes over the old one's input history
// before it starts, so it sounds straight away as if it had been listening all along instead of starting
// from an empty room. A response made for another sample rate is treated as no response until the right
// one comes.
//
// Settings reach the audio thread as an immutable box swapped whole, the dry and wet gains glide
// per sample, a pre-delay change crossfades from the old tap to the new one, input that is not finite is
// silenced before it can reach the delay line, and input near the denormal range is flushed to zero there.
// With the mix at dry the engine is not run at all. Process allocates nothing unless the sample rate it is
// handed changes. Latency: none when the mixer's frames are whole blocks, one block (never more than a
// mixer frame, see ImpulseResponse.BlockSizeFor) when they are not. -xlinka
public sealed class ConvolutionFilter : IAudioDspFilter, IAudioTailFilter
{
    private const int Chunk = 1024;
    private const double GainSeconds = 0.015;
    private const double SwapSeconds = 0.05;
    private const double PreDelayFadeSeconds = 0.02;
    private const float SilenceFloor = 1e-6f;
    private const float GainSnap = 1e-6f;

    private sealed class SettingsBox
    {
        public readonly ConvolutionSettings Value;

        public SettingsBox(in ConvolutionSettings value) => Value = value;
    }

    private sealed class EngineBox
    {
        public readonly ConvolutionEngine? Engine;

        public EngineBox(ConvolutionEngine? engine) => Engine = engine;
    }

    private SettingsBox _staged;
    private SettingsBox _applied;
    private EngineBox _stagedEngine;
    private EngineBox _appliedEngine;
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;

    private int _sampleRate;
    private ConvolutionEngine? _engine;
    private ConvolutionEngine? _outgoing;
    private float _swap = 1f;
    private float _swapStep;

    private float[] _delayLeft = Array.Empty<float>();
    private float[] _delayRight = Array.Empty<float>();
    private int _delayWrite;
    private int _tap;
    private int _nextTap;
    private int _targetTap;
    private float _tapFade;
    private float _tapFadeStep;
    private bool _tapFading;

    private readonly float[] _inLeft = new float[Chunk];
    private readonly float[] _inRight = new float[Chunk];
    private readonly float[] _wetLeft = new float[Chunk];
    private readonly float[] _wetRight = new float[Chunk];
    private readonly float[] _oldLeft = new float[Chunk];
    private readonly float[] _oldRight = new float[Chunk];

    private float _dry = 1f;
    private float _dryTarget = 1f;
    private float _wet;
    private float _wetTarget;
    private float _gainCoefficient;
    private bool _primed;
    private bool _stale;
    private int _quietSamples = int.MaxValue / 2;

    public ConvolutionFilter()
        : this(AudioSimulator.DefaultSampleRate, ConvolutionSettings.Default)
    {
    }

    public ConvolutionFilter(int sampleRate, in ConvolutionSettings settings)
    {
        _staged = new SettingsBox(settings.Clamped());
        _applied = _staged;
        _stagedEngine = new EngineBox(null);
        _appliedEngine = _stagedEngine;
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    // Written from any thread, picked up at the top of the next Process.
    public ConvolutionSettings Settings
    {
        get => Volatile.Read(ref _staged).Value;
        set
        {
            var clamped = value.Clamped();
            if (Volatile.Read(ref _staged).Value.Equals(clamped))
                return;
            Volatile.Write(ref _staged, new SettingsBox(clamped));
        }
    }

    // The room last handed over, whether or not the audio thread has taken it up yet.
    public ImpulseResponse? Response => Volatile.Read(ref _stagedEngine).Engine?.Response;

    // The room the audio thread is playing through, for the thread that runs it.
    public ImpulseResponse? ActiveResponse => _engine?.Response;

    public int SampleRate => _sampleRate;

    // Samples the reverb comes out behind where it would be with no block framing: 0 or one block.
    public int Latency => _engine?.Latency ?? 0;

    // Audio thread only: input still in the pre-delay, still ringing in the room, or a swap still fading.
    public bool HasTail => (_wet > 0f || _wetTarget > 0f)
        && (_swap < 1f || _engine?.HasTail == true || _quietSamples <= Math.Max(_tap, Math.Max(_nextTap, _targetTap)));

    // Any thread. Builds the engine on the calling thread, since that allocates; null takes the room away.
    public void SetResponse(ImpulseResponse? response)
    {
        var engine = response == null ? null : new ConvolutionEngine(response);
        Volatile.Write(ref _stagedEngine, new EngineBox(engine));
    }

    public void Reset() => _resetRequested = true;

    public void Process(Span<StereoSample> buffer, int sampleRate)
    {
        if (buffer.IsEmpty)
            return;
        if (sampleRate > 0 && sampleRate != _sampleRate)
            Configure(sampleRate);
        if (_resetRequested)
        {
            _resetRequested = false;
            ClearState();
        }
        PullSettings();
        PullEngine();

        float peak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            var sample = buffer[i];
            if (!float.IsFinite(sample.left) || !float.IsFinite(sample.right))
            {
                sample = new StereoSample(float.IsFinite(sample.left) ? sample.left : 0f, float.IsFinite(sample.right) ? sample.right : 0f);
                buffer[i] = sample;
            }
            float amplitude = sample.AbsoluteAmplitude;
            if (amplitude > peak)
                peak = amplitude;
        }
        _quietSamples = peak > SilenceFloor ? 0 : Math.Min(int.MaxValue / 2, _quietSamples + buffer.Length);
        _primed = true;

        if (_wetTarget == 0f && _wet == 0f)
        {
            // The reverb is off: nothing to compute, and what the engine holds is stale by the time it is back.
            _stale = true;
            ApplyDry(buffer);
            return;
        }
        if (_stale)
        {
            _stale = false;
            _engine?.Reset();
            _outgoing = null;
            _swap = 1f;
            ClearDelay();
        }
        if (_engine == null && _swap >= 1f)
        {
            ApplyDry(buffer);
            return;
        }

        var framing = _engine ?? _outgoing!;
        int step = Math.Max(framing.BlockSize, Chunk - Chunk % framing.BlockSize);
        int offset = 0;
        while (offset < buffer.Length)
        {
            int count = Math.Min(step, buffer.Length - offset);
            Render(buffer.Slice(offset, count));
            offset += count;
        }
    }

    private void Render(Span<StereoSample> buffer)
    {
        int count = buffer.Length;
        FillDelayed(buffer);

        var input = _inLeft.AsSpan(0, count);
        var inputRight = _inRight.AsSpan(0, count);
        if (_engine != null)
            _engine.Process(input, inputRight, _wetLeft.AsSpan(0, count), _wetRight.AsSpan(0, count));
        else
        {
            Array.Clear(_wetLeft, 0, count);
            Array.Clear(_wetRight, 0, count);
        }
        bool swapping = _swap < 1f;
        if (swapping)
        {
            if (_outgoing != null)
                _outgoing.Process(input, inputRight, _oldLeft.AsSpan(0, count), _oldRight.AsSpan(0, count));
            else
            {
                Array.Clear(_oldLeft, 0, count);
                Array.Clear(_oldRight, 0, count);
            }
        }

        float swap = _swap;
        for (int i = 0; i < count; i++)
        {
            float left = _wetLeft[i];
            float right = _wetRight[i];
            if (swapping)
            {
                if (swap < 1f)
                    swap = MathF.Min(1f, swap + _swapStep);
                left = _oldLeft[i] + (left - _oldLeft[i]) * swap;
                right = _oldRight[i] + (right - _oldRight[i]) * swap;
            }
            float dry = _dry;
            if (dry != _dryTarget)
                _dry = dry = Approach(dry, _dryTarget, _gainCoefficient);
            float wet = _wet;
            if (wet != _wetTarget)
                _wet = wet = Approach(wet, _wetTarget, _gainCoefficient);
            var x = buffer[i];
            buffer[i] = new StereoSample(x.left * dry + left * wet, x.right * dry + right * wet);
        }
        _swap = swap;
        if (swap >= 1f)
            _outgoing = null;
    }

    // The input through the pre-delay into the engine's input buffers, a finished tap fade handing over to
    // the new tap as it completes.
    private void FillDelayed(ReadOnlySpan<StereoSample> buffer)
    {
        float[] lineLeft = _delayLeft;
        float[] lineRight = _delayRight;
        int size = lineLeft.Length;
        if (!_tapFading && _tap != _targetTap)
        {
            _nextTap = _targetTap;
            _tapFade = 0f;
            _tapFading = true;
        }
        for (int i = 0; i < buffer.Length; i++)
        {
            int write = _delayWrite;
            lineLeft[write] = buffer[i].left;
            lineRight[write] = buffer[i].right;
            int read = write - _tap;
            if (read < 0)
                read += size;
            float left = lineLeft[read];
            float right = lineRight[read];
            if (_tapFading)
            {
                int other = write - _nextTap;
                if (other < 0)
                    other += size;
                float t = _tapFade;
                left += (lineLeft[other] - left) * t;
                right += (lineRight[other] - right) * t;
                t += _tapFadeStep;
                if (t >= 1f)
                {
                    _tap = _nextTap;
                    _tapFading = false;
                    t = 0f;
                    if (_tap != _targetTap)
                    {
                        _nextTap = _targetTap;
                        _tapFading = true;
                    }
                }
                _tapFade = t;
            }
            _inLeft[i] = left;
            _inRight[i] = right;
            _delayWrite = write + 1 == size ? 0 : write + 1;
        }
    }

    private void PullSettings()
    {
        var box = Volatile.Read(ref _staged);
        if (ReferenceEquals(box, _applied))
            return;
        _applied = box;
        Retarget(box.Value);
        if (!_primed)
            Snap();
    }

    // A new room waits for the last swap to finish, so a fade is only ever between two rooms. A room arriving
    // where there was none fades in from silence too: its opening samples are the input times the direct
    // sound, all at once.
    private void PullEngine()
    {
        if (_swap < 1f)
            return;
        var box = Volatile.Read(ref _stagedEngine);
        if (ReferenceEquals(box, _appliedEngine))
            return;
        _appliedEngine = box;
        var incoming = box.Engine;
        if (incoming != null && incoming.Response.SampleRate != _sampleRate)
            incoming = null;
        if (ReferenceEquals(incoming, _engine))
            return;
        if (incoming != null)
        {
            if (_engine != null && _engine.BlockSize == incoming.BlockSize && !_stale)
                incoming.CopyStateFrom(_engine);
            else
                incoming.Reset();
        }
        _outgoing = _engine;
        _engine = incoming;
        _swap = _primed ? 0f : 1f;
        if (_swap >= 1f)
            _outgoing = null;
    }

    private void Retarget(in ConvolutionSettings settings)
    {
        _targetTap = Math.Clamp((int)Math.Round(settings.PreDelay * 0.001 * _sampleRate), 0, _delayLeft.Length - 1);
        float level = MathF.Pow(10f, settings.Level / 20f);
        _dryTarget = MathF.Min(1f, 2f * (1f - settings.Mix));
        _wetTarget = MathF.Min(1f, 2f * settings.Mix) * level;
    }

    private void Snap()
    {
        _tap = _targetTap;
        _nextTap = _targetTap;
        _tapFading = false;
        _tapFade = 0f;
        _dry = _dryTarget;
        _wet = _wetTarget;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        int size = (int)Math.Ceiling(ConvolutionSettings.MaxPreDelay * 0.001 * sampleRate) + 1;
        _delayLeft = new float[size];
        _delayRight = new float[size];
        _gainCoefficient = (float)(1.0 - Math.Exp(-1.0 / (GainSeconds * sampleRate)));
        _tapFadeStep = (float)(1.0 / (PreDelayFadeSeconds * sampleRate));
        _swapStep = (float)(1.0 / (SwapSeconds * sampleRate));
        // A room made for the old rate would play at the wrong pitch; it is dropped until one for this rate comes.
        if (_engine != null && _engine.Response.SampleRate != sampleRate)
            _engine = null;
        if (_outgoing != null && _outgoing.Response.SampleRate != sampleRate)
            _outgoing = null;
        ClearState();
    }

    private void ClearState()
    {
        _engine?.Reset();
        _outgoing = null;
        _swap = 1f;
        ClearDelay();
        _quietSamples = int.MaxValue / 2;
        _stale = false;
        Retarget(_applied.Value);
        Snap();
    }

    private void ClearDelay()
    {
        Array.Clear(_delayLeft);
        Array.Clear(_delayRight);
        _delayWrite = 0;
    }

    private void ApplyDry(Span<StereoSample> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            float dry = _dry;
            if (dry != _dryTarget)
                _dry = dry = Approach(dry, _dryTarget, _gainCoefficient);
            float wet = _wet;
            if (wet != _wetTarget)
                _wet = Approach(wet, _wetTarget, _gainCoefficient);
            if (dry != 1f)
                buffer[i] = buffer[i].Multiply(dry);
        }
    }

    // A one-pole glide that lands exactly once a move stops changing the value.
    private static float Approach(float value, float target, float coefficient)
    {
        float next = value + (target - value) * coefficient;
        return next == value || MathF.Abs(target - next) < GainSnap ? target : next;
    }
}
