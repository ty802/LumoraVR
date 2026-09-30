// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;

namespace Lumora.Audio;

// Runs a nested filter at a weight between 0 (dry) and 1 (the nested filter alone), gliding to a new
// weight instead of stepping.
//
// THE WEIGHT GOES ON THE WAY IN. The output is (1 - w) * dry + nested(w * dry). For a steady weight and a
// linear nested filter that is the same crossfade as weighting the nested output, but when the weight
// falls the nested filter keeps sounding what it already took in: a reverb whose weight drops to zero
// lets its tail die away on its own instead of having it faded out from under it. Once the weight has
// reached zero and the nested filter has nothing left to say, the wrapper stops running it and hands the
// buffer back untouched; the nested filter is reset then, so waking it later starts from a clean room.
//
// Weight and Nested are written from the world thread and read here without a lock: the weight travels
// packed with a version number in one 64-bit word, and IsIdle is only true for the version the audio
// thread last saw asleep, so a weight written after that can never be mistaken for idle. -xlinka
public sealed class BlendFilter : IAudioDspFilter, IAudioTailFilter
{
    private const int ScratchFrames = 256;
    private const double GlideSeconds = 0.02;
    private const float SnapDistance = 1e-5f;
    private const float SilenceFloor = 1e-6f;

    // A nested filter that cannot say whether it is still sounding is given this long after its output
    // went quiet.
    private const double QuietSeconds = 0.25;

    private readonly StereoSample[] _scratch = new StereoSample[ScratchFrames];
    private readonly float[] _weights = new float[ScratchFrames];

    private IAudioDspFilter? _nested;
    private long _target;
    private int _idleVersion;
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;

    private IAudioDspFilter? _running;
    private float _weight;
    private bool _asleep = true;
    private int _quietSamples;
    private int _sampleRate;
    private float _coefficient;

    public BlendFilter(IAudioDspFilter? nested = null)
    {
        _nested = ReferenceEquals(nested, this) ? null : nested;
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    // One writer at a time, which the world thread is.
    public float Weight
    {
        get => Unpack(Interlocked.Read(ref _target), out _);
        set
        {
            float weight = float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f);
            if (weight == 0f)
                weight = 0f;
            long current = Interlocked.Read(ref _target);
            float old = Unpack(current, out int version);
            if (BitConverter.SingleToInt32Bits(old) == BitConverter.SingleToInt32Bits(weight))
                return;
            Interlocked.Exchange(ref _target, Pack(unchecked(version + 1), weight));
        }
    }

    public IAudioDspFilter? Nested
    {
        get => Volatile.Read(ref _nested);
        set => Volatile.Write(ref _nested, ReferenceEquals(value, this) ? null : value);
    }

    // World-thread view: the weight has been at zero long enough for the nested filter to go quiet, and
    // nothing has been written since. Safe to drop from a chain without cutting anything off.
    public bool IsIdle
    {
        get
        {
            long target = Interlocked.Read(ref _target);
            Unpack(target, out int version);
            return Volatile.Read(ref _idleVersion) == version;
        }
    }

    // Audio-thread view, for a blend nested inside another one.
    public bool HasTail => !_asleep;

    public void Reset() => _resetRequested = true;

    public void Process(Span<StereoSample> buffer, int sampleRate)
    {
        if (buffer.IsEmpty)
            return;
        if (sampleRate != _sampleRate && sampleRate > 0)
        {
            _sampleRate = sampleRate;
            _coefficient = (float)(1.0 - Math.Exp(-1.0 / (GlideSeconds * sampleRate)));
        }

        float target = Unpack(Interlocked.Read(ref _target), out int version);
        var nested = Volatile.Read(ref _nested);

        if (_resetRequested)
        {
            _resetRequested = false;
            _weight = target;
            _asleep = target == 0f;
            _quietSamples = 0;
            nested?.Reset();
        }
        if (!ReferenceEquals(nested, _running))
        {
            // Another filter may have run before it came here; it starts from silence.
            _running = nested;
            nested?.Reset();
            _quietSamples = 0;
        }

        if (_asleep && target == 0f && _weight == 0f)
        {
            Volatile.Write(ref _idleVersion, version);
            return;
        }
        _asleep = false;

        bool active = nested != null && IsEnabled(nested);
        float weight = _weight;
        float k = _coefficient;

        if (!active)
        {
            // Nothing to blend with: the dry signal is the answer at every weight.
            weight = Glide(weight, target, k, buffer.Length);
        }
        else
        {
            float nestedPeak = 0f;
            for (int offset = 0; offset < buffer.Length; offset += ScratchFrames)
            {
                int count = Math.Min(ScratchFrames, buffer.Length - offset);
                Span<StereoSample> dry = buffer.Slice(offset, count);
                Span<StereoSample> sent = _scratch.AsSpan(0, count);
                for (int i = 0; i < count; i++)
                {
                    if (weight != target)
                        weight = Approach(weight, target, k);
                    _weights[i] = weight;
                    sent[i] = dry[i].Multiply(weight);
                }

                nested!.Process(sent, sampleRate);

                for (int i = 0; i < count; i++)
                {
                    float keep = 1f - _weights[i];
                    StereoSample x = dry[i];
                    StereoSample n = sent[i];
                    dry[i] = new StereoSample(x.left * keep + n.left, x.right * keep + n.right);
                    float amplitude = n.AbsoluteAmplitude;
                    if (amplitude > nestedPeak)
                        nestedPeak = amplitude;
                }
            }
            _quietSamples = nestedPeak > SilenceFloor ? 0 : Math.Min(int.MaxValue / 2, _quietSamples + buffer.Length);
        }
        _weight = weight;

        if (target == 0f && weight == 0f && !(active && Sounding(nested!)))
        {
            _asleep = true;
            _quietSamples = 0;
            nested?.Reset();
            Volatile.Write(ref _idleVersion, version);
        }
    }

    private bool Sounding(IAudioDspFilter nested)
    {
        if (nested is IAudioTailFilter tail)
            return tail.HasTail;
        return _quietSamples < QuietSeconds * _sampleRate;
    }

    private static float Glide(float weight, float target, float k, int samples)
    {
        for (int i = 0; i < samples && weight != target; i++)
            weight = Approach(weight, target, k);
        return weight;
    }

    // Lands exactly: near the target a one-pole glide stops moving the value long before the gap closes, so
    // a move that changes nothing takes the target itself. That is what makes weight 1 exactly the nested
    // filter and weight 0 exactly dry.
    private static float Approach(float value, float target, float k)
    {
        float next = value + (target - value) * k;
        return next == value || MathF.Abs(target - next) < SnapDistance ? target : next;
    }

    private static bool IsEnabled(IAudioDspFilter filter)
    {
        try
        {
            return filter.Enabled;
        }
        catch
        {
            return false;
        }
    }

    private static long Pack(int version, float weight)
        => ((long)version << 32) | (uint)BitConverter.SingleToInt32Bits(weight);

    private static float Unpack(long packed, out int version)
    {
        version = (int)(packed >> 32);
        return BitConverter.Int32BitsToSingle(unchecked((int)packed));
    }
}
