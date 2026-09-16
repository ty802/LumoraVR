// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumora.Audio;

// A sample is one frame across channels, not one channel's value. Everything in the mixer is typed
// on this so a mono voice stream and a stereo video never disagree about what a "count" means, and
// a buffer of them reinterprets as a flat float span for anything that wants interleaved PCM. -xlinka
public interface IAudioSample<S> where S : unmanaged, IAudioSample<S>
{
    int ChannelCount { get; }
    float this[int channel] { get; }
    float AbsoluteAmplitude { get; }
    S Add(S sample);
    S Multiply(float value);
    S LerpTo(S next, float lerp);
    MonoSample ToMono();
    StereoSample ToStereo();
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct MonoSample : IAudioSample<MonoSample>
{
    public readonly float value;

    public MonoSample(float value) => this.value = value;

    public int ChannelCount => 1;
    public float this[int channel] => channel == 0 ? value : 0f;
    public float AbsoluteAmplitude => MathF.Abs(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MonoSample Add(MonoSample sample) => new(value + sample.value);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MonoSample Multiply(float v) => new(value * v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MonoSample LerpTo(MonoSample next, float lerp) => new(value + (next.value - value) * lerp);
    public MonoSample ToMono() => this;
    public StereoSample ToStereo() => new(value, value);
}

// Layout matches a float2 / Godot Vector2, so a StereoSample span casts straight onto whatever a
// device wants without a copy.
[StructLayout(LayoutKind.Sequential)]
public readonly struct StereoSample : IAudioSample<StereoSample>
{
    public readonly float left;
    public readonly float right;

    public StereoSample(float left, float right)
    {
        this.left = left;
        this.right = right;
    }

    public StereoSample(float amplitude)
    {
        left = amplitude;
        right = amplitude;
    }

    public int ChannelCount => 2;
    public float this[int channel] => channel switch { 0 => left, 1 => right, _ => 0f };
    public float AbsoluteAmplitude => MathF.Max(MathF.Abs(left), MathF.Abs(right));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StereoSample Add(StereoSample sample) => new(left + sample.left, right + sample.right);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StereoSample Multiply(float v) => new(left * v, right * v);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StereoSample Scale(float leftGain, float rightGain) => new(left * leftGain, right * rightGain);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StereoSample LerpTo(StereoSample next, float lerp) => new(left + (next.left - left) * lerp, right + (next.right - right) * lerp);
    public MonoSample ToMono() => new((left + right) * 0.5f);
    public StereoSample ToStereo() => this;
}

public static class SampleBuffers
{
    // Interleaved float PCM <-> typed sample views. No copies; the caller owns the memory either way.
    public static Span<StereoSample> AsStereo(this Span<float> interleaved) => MemoryMarshal.Cast<float, StereoSample>(interleaved);
    public static ReadOnlySpan<StereoSample> AsStereo(this ReadOnlySpan<float> interleaved) => MemoryMarshal.Cast<float, StereoSample>(interleaved);
    public static Span<float> AsFloats(this Span<StereoSample> samples) => MemoryMarshal.Cast<StereoSample, float>(samples);
    public static ReadOnlySpan<float> AsFloats(this ReadOnlySpan<StereoSample> samples) => MemoryMarshal.Cast<StereoSample, float>(samples);

    public static void Mix(Span<StereoSample> target, ReadOnlySpan<StereoSample> source, float gain)
    {
        int n = Math.Min(target.Length, source.Length);
        for (int i = 0; i < n; i++)
            target[i] = target[i].Add(source[i].Multiply(gain));
    }

    public static void Mix(Span<StereoSample> target, ReadOnlySpan<StereoSample> source, float leftGain, float rightGain)
    {
        int n = Math.Min(target.Length, source.Length);
        for (int i = 0; i < n; i++)
            target[i] = target[i].Add(source[i].Scale(leftGain, rightGain));
    }

    // Gains ramp across the buffer so a per-frame parameter change never lands as a click.
    public static void MixRamped(Span<StereoSample> target, ReadOnlySpan<StereoSample> source, float leftFrom, float leftTo, float rightFrom, float rightTo)
    {
        int n = Math.Min(target.Length, source.Length);
        if (n == 0) return;
        float inv = 1f / n;
        for (int i = 0; i < n; i++)
        {
            float t = i * inv;
            target[i] = target[i].Add(source[i].Scale(leftFrom + (leftTo - leftFrom) * t, rightFrom + (rightTo - rightFrom) * t));
        }
    }

    // Replaces every NaN or infinite sample with silence and returns how many there were. A single
    // NaN survives every add and multiply after it and turns a whole device frame into garbage.
    public static int Sanitize(Span<StereoSample> buffer)
    {
        int count = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            var s = buffer[i];
            bool badLeft = !float.IsFinite(s.left);
            bool badRight = !float.IsFinite(s.right);
            if (badLeft || badRight)
            {
                buffer[i] = new StereoSample(badLeft ? 0f : s.left, badRight ? 0f : s.right);
                count++;
            }
        }
        return count;
    }

    public static void Clip(Span<StereoSample> buffer, float maxAmplitude)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            var s = buffer[i];
            buffer[i] = new StereoSample(Math.Clamp(s.left, -maxAmplitude, maxAmplitude), Math.Clamp(s.right, -maxAmplitude, maxAmplitude));
        }
    }
}
