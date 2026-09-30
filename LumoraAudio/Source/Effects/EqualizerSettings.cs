// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Text;

namespace Lumora.Audio;

// A parametric equalizer as one value: up to eight bands in order, then a gain on the whole output in decibels.
// Bands past BandCount are ignored and compare as nothing.
public struct EqualizerSettings : IEquatable<EqualizerSettings>
{
    public const int MaxBands = 8;
    public const float MinOutputGain = -24f;
    public const float MaxOutputGain = 24f;
    public const float DefaultOutputGain = 0f;

    public EqualizerBands Bands;
    public int BandCount;
    public float OutputGain;

    public static EqualizerSettings Default => new() { OutputGain = DefaultOutputGain };

    public void Add(in EqualizerBandSettings band)
    {
        if (BandCount >= MaxBands)
            return;
        Bands[BandCount++] = band;
    }

    public readonly EqualizerSettings Clamped()
    {
        var clamped = new EqualizerSettings
        {
            BandCount = Math.Clamp(BandCount, 0, MaxBands),
            OutputGain = DspMath.Limit(OutputGain, MinOutputGain, MaxOutputGain, DefaultOutputGain),
        };
        for (int i = 0; i < clamped.BandCount; i++)
            clamped.Bands[i] = Bands[i].Clamped();
        return clamped;
    }

    public readonly bool IsFinite
    {
        get
        {
            if (!float.IsFinite(OutputGain))
                return false;
            int count = Math.Clamp(BandCount, 0, MaxBands);
            for (int i = 0; i < count; i++)
            {
                if (!Bands[i].IsFinite)
                    return false;
            }
            return true;
        }
    }

    public readonly bool Equals(EqualizerSettings other)
    {
        if (BandCount != other.BandCount || !OutputGain.Equals(other.OutputGain))
            return false;
        int count = Math.Clamp(BandCount, 0, MaxBands);
        for (int i = 0; i < count; i++)
        {
            if (!Bands[i].Equals(other.Bands[i]))
                return false;
        }
        return true;
    }

    public override readonly bool Equals(object? obj) => obj is EqualizerSettings other && Equals(other);

    public override readonly int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BandCount);
        hash.Add(OutputGain);
        int count = Math.Clamp(BandCount, 0, MaxBands);
        for (int i = 0; i < count; i++)
            hash.Add(Bands[i]);
        return hash.ToHashCode();
    }

    public static bool operator ==(EqualizerSettings left, EqualizerSettings right) => left.Equals(right);

    public static bool operator !=(EqualizerSettings left, EqualizerSettings right) => !left.Equals(right);

    public override readonly string ToString()
    {
        var text = new StringBuilder();
        int count = Math.Clamp(BandCount, 0, MaxBands);
        for (int i = 0; i < count; i++)
            text.Append('[').Append(Bands[i].ToString()).Append("] ");
        return text.Append("out ").Append(OutputGain).Append(" dB").ToString();
    }
}
