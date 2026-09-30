// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumora.Audio;

// The FFT of real samples: Size values in, the Size / 2 + 1 bins that carry them out.
//
// An even length packs even samples into the real part and odd samples into the imaginary part of a
// complex transform half as long, then pulls the two spectra apart with one twiddle pass, so a real
// transform costs a little over half a complex one. Bins 0 and Size / 2 are real; the imaginary parts
// of those two come out zero and are ignored going back. An odd length has no halves to pair and runs as
// a full complex transform.
//
// Shared the same way ComplexFft is: planned once per length, immutable, scratch from the caller. -xlinka
public sealed class RealFft
{
    private static readonly ConcurrentDictionary<int, RealFft> Plans = new();

    private readonly ComplexFft _complex;
    private readonly bool _paired;

    // W(N)^k = (cos, -sin)(2 pi k / N) for k up to a quarter of the length.
    private readonly float[] _twiddleRe;
    private readonly float[] _twiddleIm;

    private RealFft(int size)
    {
        Size = size;
        _paired = size % 2 == 0;
        if (_paired)
        {
            int half = size / 2;
            _complex = ComplexFft.Get(half);
            _twiddleRe = new float[half / 2 + 1];
            _twiddleIm = new float[half / 2 + 1];
            for (int k = 0; k < _twiddleRe.Length; k++)
            {
                double angle = 2.0 * Math.PI * k / size;
                _twiddleRe[k] = (float)Math.Cos(angle);
                _twiddleIm[k] = (float)-Math.Sin(angle);
            }
            ScratchLength = size + _complex.ScratchLength;
        }
        else
        {
            _complex = ComplexFft.Get(size);
            _twiddleRe = Array.Empty<float>();
            _twiddleIm = Array.Empty<float>();
            ScratchLength = 2 * size + _complex.ScratchLength;
        }
    }

    public int Size { get; }

    public int BinCount => Size / 2 + 1;

    public int ScratchLength { get; }

    public static RealFft Get(int size)
    {
        if (size < 1)
            throw new ArgumentOutOfRangeException(nameof(size), "an FFT needs at least one point");
        return Plans.GetOrAdd(size, static n => new RealFft(n));
    }

    public void Forward(ReadOnlySpan<float> input, Span<float> real, Span<float> imaginary)
        => Forward(input, real, imaginary, ComplexFft.ThreadScratch(ScratchLength));

    public void Inverse(ReadOnlySpan<float> real, ReadOnlySpan<float> imaginary, Span<float> output, bool scale = true)
        => Inverse(real, imaginary, output, ComplexFft.ThreadScratch(ScratchLength), scale);

    // Unscaled: bin k is sum x[n] exp(-2 pi i n k / Size).
    public void Forward(ReadOnlySpan<float> input, Span<float> real, Span<float> imaginary, Span<float> scratch)
    {
        int size = Size;
        if (input.Length < size)
            throw new ArgumentException($"a {size}-point transform needs {size} samples");
        CheckBins(real.Length, imaginary.Length, scratch.Length);

        if (!_paired)
        {
            Span<float> re = scratch.Slice(0, size);
            Span<float> im = scratch.Slice(size, size);
            input.Slice(0, size).CopyTo(re);
            im.Clear();
            _complex.Run(re, im, scratch.Slice(2 * size));
            int bins = BinCount;
            re.Slice(0, bins).CopyTo(real);
            im.Slice(0, bins).CopyTo(imaginary);
            imaginary[0] = 0f;
            return;
        }

        int half = size / 2;
        ref float x = ref MemoryMarshal.GetReference(input);
        ref float zr = ref MemoryMarshal.GetReference(scratch);
        ref float zi = ref Unsafe.Add(ref zr, half);
        for (int n = 0; n < half; n++)
        {
            Unsafe.Add(ref zr, n) = Unsafe.Add(ref x, 2 * n);
            Unsafe.Add(ref zi, n) = Unsafe.Add(ref x, 2 * n + 1);
        }
        _complex.Run(scratch.Slice(0, half), scratch.Slice(half, half), scratch.Slice(size));

        ref float xr = ref MemoryMarshal.GetReference(real);
        ref float xi = ref MemoryMarshal.GetReference(imaginary);
        ref float twr = ref MemoryMarshal.GetArrayDataReference(_twiddleRe);
        ref float twi = ref MemoryMarshal.GetArrayDataReference(_twiddleIm);
        float z0r = zr;
        float z0i = zi;
        xr = z0r + z0i;
        xi = 0f;
        Unsafe.Add(ref xr, half) = z0r - z0i;
        Unsafe.Add(ref xi, half) = 0f;

        // Z[k] = E[k] + i O[k], with E and O the transforms of the even and odd samples; X[k] = E + W O and
        // X[half - k] = conj(E - W O), so both ends are made from one pair.
        for (int k = 1; k <= half / 2; k++)
        {
            int j = half - k;
            float ar = Unsafe.Add(ref zr, k);
            float ai = Unsafe.Add(ref zi, k);
            float cr = Unsafe.Add(ref zr, j);
            float ci = Unsafe.Add(ref zi, j);
            float er = 0.5f * (ar + cr);
            float ei = 0.5f * (ai - ci);
            float qr = 0.5f * (ai + ci);
            float qi = 0.5f * (cr - ar);
            float wr = Unsafe.Add(ref twr, k);
            float wi = Unsafe.Add(ref twi, k);
            float tr = wr * qr - wi * qi;
            float ti = wr * qi + wi * qr;
            Unsafe.Add(ref xr, k) = er + tr;
            Unsafe.Add(ref xi, k) = ei + ti;
            Unsafe.Add(ref xr, j) = er - tr;
            Unsafe.Add(ref xi, j) = ti - ei;
        }
    }

    // Scaled by 1/Size unless told otherwise; unscaled gives Size times the samples, which is what a caller
    // wants that has already folded the scale into its own numbers.
    public void Inverse(ReadOnlySpan<float> real, ReadOnlySpan<float> imaginary, Span<float> output, Span<float> scratch, bool scale = true)
    {
        int size = Size;
        if (output.Length < size)
            throw new ArgumentException($"a {size}-point transform writes {size} samples");
        CheckBins(real.Length, imaginary.Length, scratch.Length);

        if (!_paired)
        {
            Span<float> re = scratch.Slice(0, size);
            Span<float> im = scratch.Slice(size, size);
            int bins = BinCount;
            re[0] = real[0];
            im[0] = 0f;
            for (int k = 1; k < bins; k++)
            {
                re[k] = real[k];
                im[k] = imaginary[k];
                re[size - k] = real[k];
                im[size - k] = -imaginary[k];
            }
            _complex.Run(im, re, scratch.Slice(2 * size));
            float factor = scale ? 1f / size : 1f;
            for (int n = 0; n < size; n++)
                output[n] = re[n] * factor;
            return;
        }

        int half = size / 2;
        ref float zr = ref MemoryMarshal.GetReference(scratch);
        ref float zi = ref Unsafe.Add(ref zr, half);
        ref float xr = ref MemoryMarshal.GetReference(real);
        ref float xi = ref MemoryMarshal.GetReference(imaginary);
        ref float twr = ref MemoryMarshal.GetArrayDataReference(_twiddleRe);
        ref float twi = ref MemoryMarshal.GetArrayDataReference(_twiddleIm);

        // Z' = g Z with g = 1/half for samples out, 2 for Size times them, and the 1/2 of E and O folded in.
        float g = scale ? 0.5f / half : 1f;
        float x0 = xr;
        float xh = Unsafe.Add(ref xr, half);
        zr = g * (x0 + xh);
        zi = g * (x0 - xh);
        for (int k = 1; k <= half / 2; k++)
        {
            int j = half - k;
            float xkr = Unsafe.Add(ref xr, k);
            float xki = Unsafe.Add(ref xi, k);
            float xjr = Unsafe.Add(ref xr, j);
            float xji = Unsafe.Add(ref xi, j);
            float er = xkr + xjr;
            float ei = xki - xji;
            float fr = xkr - xjr;
            float fi = xki + xji;
            float wr = Unsafe.Add(ref twr, k);
            float wi = Unsafe.Add(ref twi, k);
            float qr = wr * fr + wi * fi;
            float qi = wr * fi - wi * fr;
            Unsafe.Add(ref zr, k) = g * (er - qi);
            Unsafe.Add(ref zi, k) = g * (ei + qr);
            Unsafe.Add(ref zr, j) = g * (er + qi);
            Unsafe.Add(ref zi, j) = g * (qr - ei);
        }
        _complex.Run(scratch.Slice(half, half), scratch.Slice(0, half), scratch.Slice(size));
        ref float y = ref MemoryMarshal.GetReference(output);
        for (int n = 0; n < half; n++)
        {
            Unsafe.Add(ref y, 2 * n) = Unsafe.Add(ref zr, n);
            Unsafe.Add(ref y, 2 * n + 1) = Unsafe.Add(ref zi, n);
        }
    }

    private void CheckBins(int realLength, int imaginaryLength, int scratchLength)
    {
        int bins = BinCount;
        if (realLength < bins || imaginaryLength < bins)
            throw new ArgumentException($"a {Size}-point real transform has {bins} bins");
        if (scratchLength < ScratchLength)
            throw new ArgumentException($"a {Size}-point real transform needs {ScratchLength} floats of scratch");
    }
}
