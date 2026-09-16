// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Audio;

// Positioning math for one (output, listener) pair, run on the audio thread for every audible pair
// every frame. Every path has to survive a zero-length direction, a zero-length shape and an
// infinite MaxDistance without producing a NaN: one NaN reaching the mix buffer is not a glitch,
// it is a dead channel for the rest of the session, because NaN + anything stays NaN. The guards
// are all written as !(x > y) so that a NaN input takes the safe branch instead of the fast one.
// -xlinka
internal static class Spatializer
{
    public const float SpeedOfSound = 343f;

    private const float PanEpsilon = 1e-5f;
    private const float BehindAttenuation = 0.85f;

    public static float ShapeGain(in AudioShape shape, float distance)
    {
        if (shape.Kind == AudioShapeKind.Global)
            return 1f;

        float min = MathF.Max(shape.MinDistance, 1e-4f);
        float max = shape.MaxDistance;
        if (!(distance > min))
            return 1f;

        switch (shape.Rolloff)
        {
            case AudioRolloffCurve.Logarithmic:
                return min / distance;

            case AudioRolloffCurve.LogarithmicFadeOff:
            {
                float gain = min / distance;
                if (float.IsPositiveInfinity(max))
                    return gain;
                if (distance >= max)
                    return 0f;
                float span = MathF.Max(max - min, 1e-4f);
                // The 0.5 unit floor keeps tight shapes from snapping off, but it must never push
                // the fade start inside MinDistance or the curve would step down at full volume.
                float fade = MathF.Min(MathF.Max(0.1f * span, 0.5f), span);
                float fadeStart = max - fade;
                if (distance > fadeStart)
                    gain *= MathF.Max(0f, (max - distance) / fade);
                return gain;
            }

            default:
            {
                if (distance >= max)
                    return 0f;
                float span = MathF.Max(max - min, 1e-4f);
                return LuminaMath.Clamp(1f - (distance - min) / span, 0f, 1f);
            }
        }
    }

    public static void ChannelGains(in AudioOutputParams output, in AudioListenerParams listener, float3 delta, float distance, float shapeGain, out float left, out float right)
    {
        float flat = shapeGain * output.Volume;
        if (!float.IsFinite(flat))
            flat = 0f;

        if (!output.Spatialize || !(output.SpatialBlend > 0f) || output.Shape.Kind == AudioShapeKind.Global || !(distance > 0f))
        {
            left = flat;
            right = flat;
            return;
        }

        float3 local = listener.Rotation.Inverse * delta;
        float horizontal = MathF.Sqrt(local.x * local.x + local.z * local.z);
        float pan = LuminaMath.Clamp(local.x / (horizontal + PanEpsilon), -1f, 1f);
        float angle = (pan + 1f) * (LuminaMath.PI * 0.25f);
        float panLeft = MathF.Cos(angle);
        float panRight = MathF.Sin(angle);
        if (local.z < 0f)
        {
            panLeft *= BehindAttenuation;
            panRight *= BehindAttenuation;
        }

        float range = MathF.Max(output.SpatializationTransitionRange, 1e-4f);
        float blend = LuminaMath.Clamp(output.SpatialBlend * SmoothStep((distance - output.SpatializationStartDistance) / range), 0f, 1f);

        left = (1f + (panLeft - 1f) * blend) * flat;
        right = (1f + (panRight - 1f) * blend) * flat;
        if (!float.IsFinite(left))
            left = 0f;
        if (!float.IsFinite(right))
            right = 0f;
    }

    public static float SmoothStep(float t)
    {
        if (!(t > 0f))
            return 0f;
        if (t >= 1f)
            return 1f;
        return t * t * (3f - 2f * t);
    }

    public static float Doppler(in AudioOutputParams output, in AudioListenerParams listener, float3 delta, float distance)
    {
        if (output.DopplerLevel == 0f || !(distance > 1e-4f))
            return 1f;
        float3 relative = output.Velocity - listener.Velocity;
        float closing = -float3.Dot(relative, delta / distance);
        if (!float.IsFinite(closing))
            return 1f;
        return LuminaMath.Clamp(1f + output.DopplerLevel * (closing / SpeedOfSound), 0.5f, 2f);
    }

    // Linear below the knee, asymptotic above it. A hard clamp would square off the waveform and
    // spray harmonics across the whole band the moment two loud sources land on the same frame.
    public static float SoftClip(float sample)
    {
        const float knee = 0.7f;
        float magnitude = MathF.Abs(sample);
        if (!(magnitude > knee))
            return float.IsNaN(sample) ? 0f : sample;
        float shaped = knee + (1f - knee) * MathF.Tanh((magnitude - knee) / (1f - knee));
        return sample < 0f ? -shaped : shaped;
    }

    public static void ApplyMaster(Span<StereoSample> buffer, float volume)
    {
        if (!float.IsFinite(volume))
            volume = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            StereoSample sample = buffer[i];
            buffer[i] = new StereoSample(SoftClip(sample.left * volume), SoftClip(sample.right * volume));
        }
    }
}
