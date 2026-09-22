// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Radix-2 FFT, in place, allocation free once built.
//
// Three things in this engine want to look at what a signal is made of rather than how loud it is:
// noise suppression needs a noise floor per band, viseme analysis needs where the energy sits to tell
// an "ee" from an "oo", and a waveform display wants a spectrum. One transform serves all three, so it
// lives here rather than three times over.
//
// The tables are built once per size and reused; a transform allocates nothing. Not thread safe by
// itself - each user keeps its own instance, which is what the capture thread and the analyzer both
// do. -xlinka
public sealed class FourierTransform
{
    private readonly int _size;
    private readonly int _bits;
    private readonly float[] _cos;
    private readonly float[] _sin;
    private readonly int[] _reverse;
    private readonly float[] _window;

    public FourierTransform(int size)
    {
        if (size < 4 || (size & (size - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(size), "FFT size must be a power of two, 4 or greater.");

        _size = size;
        _bits = 0;
        for (int n = size; n > 1; n >>= 1)
            _bits++;

        _cos = new float[size / 2];
        _sin = new float[size / 2];
        for (int i = 0; i < size / 2; i++)
        {
            double angle = -2.0 * Math.PI * i / size;
            _cos[i] = (float)Math.Cos(angle);
            _sin[i] = (float)Math.Sin(angle);
        }

        _reverse = new int[size];
        for (int i = 0; i < size; i++)
        {
            int r = 0;
            for (int b = 0; b < _bits; b++)
                r |= ((i >> b) & 1) << (_bits - 1 - b);
            _reverse[i] = r;
        }

        // ROOT Hann, because everything that uses this windows TWICE: once going in and once coming
        // back out of the transform. What has to sum to a constant across hops is the product of the
        // two windows, not either one alone. Plain Hann squared sums to 1.5 at 50% overlap, so every
        // reconstructed signal would come back 3.5 dB hot; the root of it squares back to Hann, which
        // sums to exactly one. Anything that windows only once (a spectrum for display) can square
        // this back with WindowAt(i) * WindowAt(i). -xlinka
        _window = new float[size];
        for (int i = 0; i < size; i++)
        {
            float hann = 0.5f * (1f - (float)Math.Cos(2.0 * Math.PI * i / size));
            _window[i] = (float)Math.Sqrt(hann);
        }
    }

    public int Size => _size;

    // Frequency of bin i at a given sample rate, in Hz.
    public float BinFrequency(int bin, int sampleRate) => bin * sampleRate / (float)_size;

    // Bin that carries a frequency, clamped into the half-spectrum.
    public int FrequencyBin(float hz, int sampleRate)
    {
        int bin = (int)MathF.Round(hz * _size / sampleRate);
        if (bin < 0) bin = 0;
        int last = _size / 2;
        return bin > last ? last : bin;
    }

    public void ApplyWindow(Span<float> samples)
    {
        int n = samples.Length < _size ? samples.Length : _size;
        for (int i = 0; i < n; i++)
            samples[i] *= _window[i];
    }

    public float WindowAt(int index) => _window[index];

    // Forward transform of real input. real and imaginary are both Size long; imaginary is zeroed for
    // you, so the caller only fills real.
    public void Forward(Span<float> real, Span<float> imaginary)
    {
        Reorder(real, imaginary);
        Transform(real, imaginary, inverse: false);
    }

    // Inverse transform, scaled so Forward followed by Inverse is the identity.
    public void Inverse(Span<float> real, Span<float> imaginary)
    {
        Reorder(real, imaginary);
        Transform(real, imaginary, inverse: true);

        float scale = 1f / _size;
        for (int i = 0; i < _size; i++)
        {
            real[i] *= scale;
            imaginary[i] *= scale;
        }
    }

    private void Reorder(Span<float> real, Span<float> imaginary)
    {
        for (int i = 0; i < _size; i++)
        {
            int j = _reverse[i];
            if (j <= i)
                continue;
            (real[i], real[j]) = (real[j], real[i]);
            (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
        }
    }

    private void Transform(Span<float> real, Span<float> imaginary, bool inverse)
    {
        for (int span = 1; span < _size; span <<= 1)
        {
            int step = span << 1;
            int stride = _size / step;

            for (int group = 0; group < span; group++)
            {
                int twiddle = group * stride;
                float wr = _cos[twiddle];
                float wi = inverse ? -_sin[twiddle] : _sin[twiddle];

                for (int i = group; i < _size; i += step)
                {
                    int j = i + span;
                    float tr = wr * real[j] - wi * imaginary[j];
                    float ti = wr * imaginary[j] + wi * real[j];

                    real[j] = real[i] - tr;
                    imaginary[j] = imaginary[i] - ti;
                    real[i] += tr;
                    imaginary[i] += ti;
                }
            }
        }
    }
}
