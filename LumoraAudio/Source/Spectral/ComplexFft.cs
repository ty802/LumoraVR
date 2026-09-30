// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumora.Audio;

// A complex FFT of any length, planned once per length and shared.
//
// Mixed radix in Stockham order: every stage reads one buffer and writes the other, so the result comes
// out in natural order with no bit-reversal pass and every inner loop walks contiguous memory. Radix 4,
// 2, 3 and 5 have dedicated butterflies; any other prime goes through a general one that pairs j with
// r - j to halve its multiplies. That one costs r multiplies a point, so a prime too big for it is done as
// Bluestein's chirp convolution over a smooth length at least twice as long, which keeps a prime length at
// n log n instead of n squared. Where the line sits depends on whether the prime's stage runs on vectors:
// measured, a scalar general stage loses to the chirp from about 40 up, a vector one only past a few
// hundred.
//
// Radix 4 stages run earliest and odd primes latest. The stride a stage walks is the product of the radices
// before it, and once that stride is a whole number of SIMD lanes the stage runs on Vector<float>, so only
// the opening stage or two of any plan run scalar. The inverse is the forward transform with real and
// imaginary swapped on the way in and out, which leaves one set of butterflies to get right.
//
// A plan is immutable once built. Scratch comes from the caller (ScratchLength floats), which is what
// makes one plan safe on any number of threads at once; the overloads without it use scratch kept per
// thread. Nothing allocates while a transform runs. -xlinka
public sealed class ComplexFft
{
    // The largest prime the general butterfly takes on a stage that runs scalar, and on one whose stride
    // fills whole vectors.
    public const int MaxScalarRadix = 40;
    public const int MaxVectorRadix = 256;
    private const int VectorStride = 8;

    private const float Sin60 = 0.866025403784438646763723170752936183f;
    private const float Cos72 = 0.309016994374947424102293417182819059f;
    private const float Cos144 = -0.809016994374947424102293417182819059f;
    private const float Sin72 = 0.951056516295153572116439333379382143f;
    private const float Sin144 = 0.587785252292473129168705954639072769f;

    private static readonly ConcurrentDictionary<int, ComplexFft> Plans = new();

    [ThreadStatic]
    private static float[]? _threadScratch;

    private readonly Stage[] _stages;
    private readonly Chirp? _chirp;

    private sealed class Stage
    {
        public readonly int Radix;
        public readonly int Stride;
        public readonly int Span;

        // W(n)^(p k) for p below Span and k from 1 to Radix - 1, at [p (Radix - 1) + k - 1].
        public readonly float[] TwiddleRe;
        public readonly float[] TwiddleIm;

        // cos and sin of 2 pi j / Radix, for the general butterfly.
        public readonly float[] RootCos;
        public readonly float[] RootSin;

        public Stage(int radix, int stride, int span)
        {
            Radix = radix;
            Stride = stride;
            Span = span;
            int n = radix * span;
            TwiddleRe = new float[span * (radix - 1)];
            TwiddleIm = new float[span * (radix - 1)];
            for (int p = 0; p < span; p++)
            {
                for (int k = 1; k < radix; k++)
                {
                    long turn = (long)p * k % n;
                    double angle = -2.0 * Math.PI * turn / n;
                    TwiddleRe[p * (radix - 1) + k - 1] = (float)Math.Cos(angle);
                    TwiddleIm[p * (radix - 1) + k - 1] = (float)Math.Sin(angle);
                }
            }
            if (radix > 5)
            {
                RootCos = new float[radix];
                RootSin = new float[radix];
                for (int j = 0; j < radix; j++)
                {
                    RootCos[j] = (float)Math.Cos(2.0 * Math.PI * j / radix);
                    RootSin[j] = (float)Math.Sin(2.0 * Math.PI * j / radix);
                }
            }
            else
            {
                RootCos = Array.Empty<float>();
                RootSin = Array.Empty<float>();
            }
        }
    }

    // X[k] = c[k] sum a[n] b[k - n] with a[n] = x[n] c[n], c[n] = exp(-i pi n^2 / N) and b = conj(c), the
    // convolution done circularly over Length. B holds the transform of b with the 1/Length of the inverse
    // folded in.
    private sealed class Chirp
    {
        public readonly int Length;
        public readonly ComplexFft Inner;
        public readonly float[] ChirpRe;
        public readonly float[] ChirpIm;
        public readonly float[] KernelRe;
        public readonly float[] KernelIm;

        public Chirp(int size)
        {
            Length = FftSizes.NextFast(2 * size - 1);
            Inner = Get(Length);
            ChirpRe = new float[size];
            ChirpIm = new float[size];
            var br = new float[Length];
            var bi = new float[Length];
            long twice = 2L * size;
            for (int n = 0; n < size; n++)
            {
                long square = (long)n * n % twice;
                double angle = Math.PI * square / size;
                double c = Math.Cos(angle);
                double s = Math.Sin(angle);
                ChirpRe[n] = (float)c;
                ChirpIm[n] = (float)-s;
                br[n] = (float)c;
                bi[n] = (float)s;
                if (n > 0)
                {
                    br[Length - n] = (float)c;
                    bi[Length - n] = (float)s;
                }
            }
            var scratch = new float[Inner.ScratchLength];
            Inner.Run(br, bi, scratch);
            float scale = 1f / Length;
            for (int i = 0; i < Length; i++)
            {
                br[i] *= scale;
                bi[i] *= scale;
            }
            KernelRe = br;
            KernelIm = bi;
        }

        public int ScratchLength => 2 * Length + Inner.ScratchLength;

        public void Run(Span<float> re, Span<float> im, Span<float> scratch)
        {
            int size = ChirpRe.Length;
            Span<float> ar = scratch.Slice(0, Length);
            Span<float> ai = scratch.Slice(Length, Length);
            Span<float> inner = scratch.Slice(2 * Length);
            for (int n = 0; n < size; n++)
            {
                float xr = re[n];
                float xi = im[n];
                float cr = ChirpRe[n];
                float ci = ChirpIm[n];
                ar[n] = xr * cr - xi * ci;
                ai[n] = xr * ci + xi * cr;
            }
            ar.Slice(size).Clear();
            ai.Slice(size).Clear();

            Inner.Run(ar, ai, inner);
            MultiplyInPlace(ar, ai, KernelRe, KernelIm);
            Inner.Run(ai, ar, inner);

            for (int k = 0; k < size; k++)
            {
                float yr = ar[k];
                float yi = ai[k];
                float cr = ChirpRe[k];
                float ci = ChirpIm[k];
                re[k] = yr * cr - yi * ci;
                im[k] = yr * ci + yi * cr;
            }
        }
    }

    private ComplexFft(int size)
    {
        Size = size;
        var factors = Factor(size);
        bool direct = true;
        int walk = 1;
        foreach (int f in factors)
        {
            if (f > (walk >= VectorStride ? MaxVectorRadix : MaxScalarRadix))
                direct = false;
            walk *= f;
        }

        if (!direct)
        {
            _stages = Array.Empty<Stage>();
            _chirp = new Chirp(size);
            ScratchLength = _chirp.ScratchLength;
            return;
        }

        _stages = new Stage[factors.Count];
        int stride = 1;
        for (int i = 0; i < factors.Count; i++)
        {
            int radix = factors[i];
            int n = size / stride;
            _stages[i] = new Stage(radix, stride, n / radix);
            stride *= radix;
        }
        ScratchLength = 2 * size;
    }

    public int Size { get; }

    // Floats of scratch a transform needs beside its data.
    public int ScratchLength { get; }

    // True when the length has a prime factor too big for the general butterfly and runs as a chirp
    // convolution.
    public bool UsesChirp => _chirp != null;

    // The radices in the order the stages run, empty for a chirp plan.
    public int[] Radices
    {
        get
        {
            var radices = new int[_stages.Length];
            for (int i = 0; i < radices.Length; i++)
                radices[i] = _stages[i].Radix;
            return radices;
        }
    }

    public static ComplexFft Get(int size)
    {
        if (size < 1)
            throw new ArgumentOutOfRangeException(nameof(size), "an FFT needs at least one point");
        return Plans.GetOrAdd(size, static n => new ComplexFft(n));
    }

    // In place, unscaled: X[k] = sum x[n] exp(-2 pi i n k / N).
    public void Forward(Span<float> real, Span<float> imaginary, Span<float> scratch)
    {
        Check(real, imaginary, scratch);
        Run(real, imaginary, scratch);
    }

    // In place. Scaled by 1/N unless told otherwise, so Forward then Inverse gives back the input.
    public void Inverse(Span<float> real, Span<float> imaginary, Span<float> scratch, bool scale = true)
    {
        Check(real, imaginary, scratch);
        Run(imaginary, real, scratch);
        if (scale)
            Scale(real.Slice(0, Size), imaginary.Slice(0, Size), 1f / Size);
    }

    public void Forward(Span<float> real, Span<float> imaginary) => Forward(real, imaginary, ThreadScratch(ScratchLength));

    public void Inverse(Span<float> real, Span<float> imaginary) => Inverse(real, imaginary, ThreadScratch(ScratchLength));

    internal static Span<float> ThreadScratch(int length)
    {
        var buffer = _threadScratch;
        if (buffer == null || buffer.Length < length)
            _threadScratch = buffer = new float[Math.Max(length, 256)];
        return buffer.AsSpan(0, length);
    }

    internal static void Scale(Span<float> real, Span<float> imaginary, float factor)
    {
        ScaleInPlace(real, factor);
        ScaleInPlace(imaginary, factor);
    }

    internal static void ScaleInPlace(Span<float> values, float factor)
    {
        int i = 0;
        if (Vector.IsHardwareAccelerated && values.Length >= Vector<float>.Count)
        {
            var f = new Vector<float>(factor);
            ref float v = ref MemoryMarshal.GetReference(values);
            int end = values.Length - values.Length % Vector<float>.Count;
            for (; i < end; i += Vector<float>.Count)
                Store(ref v, i, Load(ref v, i) * f);
        }
        for (; i < values.Length; i++)
            values[i] *= factor;
    }

    // a *= b, element by element as complex numbers.
    internal static void MultiplyInPlace(Span<float> ar, Span<float> ai, ReadOnlySpan<float> br, ReadOnlySpan<float> bi)
    {
        int count = ar.Length;
        ref float xr = ref MemoryMarshal.GetReference(ar);
        ref float xi = ref MemoryMarshal.GetReference(ai);
        ref float yr = ref MemoryMarshal.GetReference(br);
        ref float yi = ref MemoryMarshal.GetReference(bi);
        int i = 0;
        if (Vector.IsHardwareAccelerated && count >= Vector<float>.Count)
        {
            int end = count - count % Vector<float>.Count;
            for (; i < end; i += Vector<float>.Count)
            {
                var pr = Load(ref xr, i);
                var pi = Load(ref xi, i);
                var qr = Load(ref yr, i);
                var qi = Load(ref yi, i);
                Store(ref xr, i, pr * qr - pi * qi);
                Store(ref xi, i, pr * qi + pi * qr);
            }
        }
        for (; i < count; i++)
        {
            float pr = Unsafe.Add(ref xr, i);
            float pi = Unsafe.Add(ref xi, i);
            float qr = Unsafe.Add(ref yr, i);
            float qi = Unsafe.Add(ref yi, i);
            Unsafe.Add(ref xr, i) = pr * qr - pi * qi;
            Unsafe.Add(ref xi, i) = pr * qi + pi * qr;
        }
    }

    private void Check(Span<float> real, Span<float> imaginary, Span<float> scratch)
    {
        if (real.Length < Size || imaginary.Length < Size)
            throw new ArgumentException($"a {Size}-point transform needs {Size} values in each of real and imaginary");
        if (scratch.Length < ScratchLength)
            throw new ArgumentException($"a {Size}-point transform needs {ScratchLength} floats of scratch");
    }

    // Forward, unchecked. Swapping real and imaginary around it gives the unscaled inverse.
    internal void Run(Span<float> re, Span<float> im, Span<float> scratch)
    {
        if (_chirp != null)
        {
            _chirp.Run(re, im, scratch);
            return;
        }
        int count = _stages.Length;
        if (count == 0)
            return;

        ref float dataRe = ref MemoryMarshal.GetReference(re);
        ref float dataIm = ref MemoryMarshal.GetReference(im);
        ref float spareRe = ref MemoryMarshal.GetReference(scratch);
        ref float spareIm = ref Unsafe.Add(ref spareRe, Size);

        // An odd count runs its last stage in place: that stage has a span of one, so every group it reads
        // is the group it writes, and the ping-pong before it has left the data where it started.
        bool inData = true;
        for (int i = 0; i < count; i++)
        {
            var stage = _stages[i];
            if (i == count - 1 && (count & 1) == 1)
            {
                RunStage(stage, ref dataRe, ref dataIm, ref dataRe, ref dataIm);
                break;
            }
            if (inData)
                RunStage(stage, ref dataRe, ref dataIm, ref spareRe, ref spareIm);
            else
                RunStage(stage, ref spareRe, ref spareIm, ref dataRe, ref dataIm);
            inData = !inData;
        }
    }

    private static void RunStage(Stage stage, ref float xr, ref float xi, ref float yr, ref float yi)
    {
        switch (stage.Radix)
        {
            case 2:
                Radix2(stage, ref xr, ref xi, ref yr, ref yi);
                break;
            case 3:
                Radix3(stage, ref xr, ref xi, ref yr, ref yi);
                break;
            case 4:
                Radix4(stage, ref xr, ref xi, ref yr, ref yi);
                break;
            case 5:
                Radix5(stage, ref xr, ref xi, ref yr, ref yi);
                break;
            default:
                RadixOdd(stage, ref xr, ref xi, ref yr, ref yi);
                break;
        }
    }

    // How many of a stage's stride positions the vector loop covers; the rest run scalar.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int VectorSpan(int stride)
    {
        int lanes = Vector<float>.Count;
        return Vector.IsHardwareAccelerated && stride >= lanes ? stride - stride % lanes : 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Vector<float> Load(ref float origin, int index)
        => Unsafe.ReadUnaligned<Vector<float>>(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref origin, index)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Store(ref float origin, int index, Vector<float> value)
        => Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref Unsafe.Add(ref origin, index)), value);

    // Reads x[q + s (p + j m)], writes y[q + s (r p + k)] times W(n)^(p k).
    private static void Radix2(Stage stage, ref float xr, ref float xi, ref float yr, ref float yi)
    {
        int s = stage.Stride;
        int m = stage.Span;
        int sm = s * m;
        int vector = VectorSpan(s);
        ref float twr = ref MemoryMarshal.GetArrayDataReference(stage.TwiddleRe);
        ref float twi = ref MemoryMarshal.GetArrayDataReference(stage.TwiddleIm);
        for (int p = 0; p < m; p++)
        {
            float wr = Unsafe.Add(ref twr, p);
            float wi = Unsafe.Add(ref twi, p);
            int a = s * p;
            int o = 2 * s * p;
            int q = 0;
            if (vector > 0)
            {
                var vwr = new Vector<float>(wr);
                var vwi = new Vector<float>(wi);
                for (; q < vector; q += Vector<float>.Count)
                {
                    var a0r = Load(ref xr, a + q);
                    var a0i = Load(ref xi, a + q);
                    var a1r = Load(ref xr, a + sm + q);
                    var a1i = Load(ref xi, a + sm + q);
                    var dr = a0r - a1r;
                    var di = a0i - a1i;
                    Store(ref yr, o + q, a0r + a1r);
                    Store(ref yi, o + q, a0i + a1i);
                    Store(ref yr, o + s + q, dr * vwr - di * vwi);
                    Store(ref yi, o + s + q, dr * vwi + di * vwr);
                }
            }
            for (; q < s; q++)
            {
                float a0r = Unsafe.Add(ref xr, a + q);
                float a0i = Unsafe.Add(ref xi, a + q);
                float a1r = Unsafe.Add(ref xr, a + sm + q);
                float a1i = Unsafe.Add(ref xi, a + sm + q);
                float dr = a0r - a1r;
                float di = a0i - a1i;
                Unsafe.Add(ref yr, o + q) = a0r + a1r;
                Unsafe.Add(ref yi, o + q) = a0i + a1i;
                Unsafe.Add(ref yr, o + s + q) = dr * wr - di * wi;
                Unsafe.Add(ref yi, o + s + q) = dr * wi + di * wr;
            }
        }
    }

    private static void Radix3(Stage stage, ref float xr, ref float xi, ref float yr, ref float yi)
    {
        int s = stage.Stride;
        int m = stage.Span;
        int sm = s * m;
        int vector = VectorSpan(s);
        ref float twr = ref MemoryMarshal.GetArrayDataReference(stage.TwiddleRe);
        ref float twi = ref MemoryMarshal.GetArrayDataReference(stage.TwiddleIm);
        var half = new Vector<float>(0.5f);
        var root = new Vector<float>(Sin60);
        for (int p = 0; p < m; p++)
        {
            int t = 2 * p;
            float w1r = Unsafe.Add(ref twr, t), w1i = Unsafe.Add(ref twi, t);
            float w2r = Unsafe.Add(ref twr, t + 1), w2i = Unsafe.Add(ref twi, t + 1);
            int a = s * p;
            int o = 3 * s * p;
            int q = 0;
            if (vector > 0)
            {
                var v1r = new Vector<float>(w1r);
                var v1i = new Vector<float>(w1i);
                var v2r = new Vector<float>(w2r);
                var v2i = new Vector<float>(w2i);
                for (; q < vector; q += Vector<float>.Count)
                {
                    var a0r = Load(ref xr, a + q);
                    var a0i = Load(ref xi, a + q);
                    var a1r = Load(ref xr, a + sm + q);
                    var a1i = Load(ref xi, a + sm + q);
                    var a2r = Load(ref xr, a + 2 * sm + q);
                    var a2i = Load(ref xi, a + 2 * sm + q);
                    var sr = a1r + a2r;
                    var si = a1i + a2i;
                    var mr = a0r - sr * half;
                    var mi = a0i - si * half;
                    var nr = (a1i - a2i) * root;
                    var ni = (a2r - a1r) * root;
                    Store(ref yr, o + q, a0r + sr);
                    Store(ref yi, o + q, a0i + si);
                    var b1r = mr + nr;
                    var b1i = mi + ni;
                    var b2r = mr - nr;
                    var b2i = mi - ni;
                    Store(ref yr, o + s + q, b1r * v1r - b1i * v1i);
                    Store(ref yi, o + s + q, b1r * v1i + b1i * v1r);
                    Store(ref yr, o + 2 * s + q, b2r * v2r - b2i * v2i);
                    Store(ref yi, o + 2 * s + q, b2r * v2i + b2i * v2r);
                }
            }
            for (; q < s; q++)
            {
                float a0r = Unsafe.Add(ref xr, a + q);
                float a0i = Unsafe.Add(ref xi, a + q);
                float a1r = Unsafe.Add(ref xr, a + sm + q);
                float a1i = Unsafe.Add(ref xi, a + sm + q);
                float a2r = Unsafe.Add(ref xr, a + 2 * sm + q);
                float a2i = Unsafe.Add(ref xi, a + 2 * sm + q);
                float sr = a1r + a2r;
                float si = a1i + a2i;
                float mr = a0r - 0.5f * sr;
                float mi = a0i - 0.5f * si;
                float nr = (a1i - a2i) * Sin60;
                float ni = (a2r - a1r) * Sin60;
                Unsafe.Add(ref yr, o + q) = a0r + sr;
                Unsafe.Add(ref yi, o + q) = a0i + si;
                float b1r = mr + nr;
                float b1i = mi + ni;
                float b2r = mr - nr;
                float b2i = mi - ni;
                Unsafe.Add(ref yr, o + s + q) = b1r * w1r - b1i * w1i;
                Unsafe.Add(ref yi, o + s + q) = b1r * w1i + b1i * w1r;
                Unsafe.Add(ref yr, o + 2 * s + q) = b2r * w2r - b2i * w2i;
                Unsafe.Add(ref yi, o + 2 * s + q) = b2r * w2i + b2i * w2r;
            }
        }
    }

    private static void Radix4(Stage stage, ref float xr, ref float xi, ref float yr, ref float yi)
    {
        int s = stage.Stride;
        int m = stage.Span;
        int sm = s * m;
        int vector = VectorSpan(s);
        ref float twr = ref MemoryMarshal.GetArrayDataReference(stage.TwiddleRe);
        ref float twi = ref MemoryMarshal.GetArrayDataReference(stage.TwiddleIm);
        for (int p = 0; p < m; p++)
        {
            int t = 3 * p;
            float w1r = Unsafe.Add(ref twr, t), w1i = Unsafe.Add(ref twi, t);
            float w2r = Unsafe.Add(ref twr, t + 1), w2i = Unsafe.Add(ref twi, t + 1);
            float w3r = Unsafe.Add(ref twr, t + 2), w3i = Unsafe.Add(ref twi, t + 2);
            int a = s * p;
            int o = 4 * s * p;
            int q = 0;
            if (vector > 0)
            {
                var v1r = new Vector<float>(w1r);
                var v1i = new Vector<float>(w1i);
                var v2r = new Vector<float>(w2r);
                var v2i = new Vector<float>(w2i);
                var v3r = new Vector<float>(w3r);
                var v3i = new Vector<float>(w3i);
                for (; q < vector; q += Vector<float>.Count)
                {
                    var a0r = Load(ref xr, a + q);
                    var a0i = Load(ref xi, a + q);
                    var a1r = Load(ref xr, a + sm + q);
                    var a1i = Load(ref xi, a + sm + q);
                    var a2r = Load(ref xr, a + 2 * sm + q);
                    var a2i = Load(ref xi, a + 2 * sm + q);
                    var a3r = Load(ref xr, a + 3 * sm + q);
                    var a3i = Load(ref xi, a + 3 * sm + q);
                    var t0r = a0r + a2r;
                    var t0i = a0i + a2i;
                    var t1r = a0r - a2r;
                    var t1i = a0i - a2i;
                    var t2r = a1r + a3r;
                    var t2i = a1i + a3i;
                    var t3r = a1i - a3i;
                    var t3i = a3r - a1r;
                    Store(ref yr, o + q, t0r + t2r);
                    Store(ref yi, o + q, t0i + t2i);
                    var b1r = t1r + t3r;
                    var b1i = t1i + t3i;
                    var b2r = t0r - t2r;
                    var b2i = t0i - t2i;
                    var b3r = t1r - t3r;
                    var b3i = t1i - t3i;
                    Store(ref yr, o + s + q, b1r * v1r - b1i * v1i);
                    Store(ref yi, o + s + q, b1r * v1i + b1i * v1r);
                    Store(ref yr, o + 2 * s + q, b2r * v2r - b2i * v2i);
                    Store(ref yi, o + 2 * s + q, b2r * v2i + b2i * v2r);
                    Store(ref yr, o + 3 * s + q, b3r * v3r - b3i * v3i);
                    Store(ref yi, o + 3 * s + q, b3r * v3i + b3i * v3r);
                }
            }
            for (; q < s; q++)
            {
                float a0r = Unsafe.Add(ref xr, a + q);
                float a0i = Unsafe.Add(ref xi, a + q);
                float a1r = Unsafe.Add(ref xr, a + sm + q);
                float a1i = Unsafe.Add(ref xi, a + sm + q);
                float a2r = Unsafe.Add(ref xr, a + 2 * sm + q);
                float a2i = Unsafe.Add(ref xi, a + 2 * sm + q);
                float a3r = Unsafe.Add(ref xr, a + 3 * sm + q);
                float a3i = Unsafe.Add(ref xi, a + 3 * sm + q);
                float t0r = a0r + a2r;
                float t0i = a0i + a2i;
                float t1r = a0r - a2r;
                float t1i = a0i - a2i;
                float t2r = a1r + a3r;
                float t2i = a1i + a3i;
                float t3r = a1i - a3i;
                float t3i = a3r - a1r;
                Unsafe.Add(ref yr, o + q) = t0r + t2r;
                Unsafe.Add(ref yi, o + q) = t0i + t2i;
                float b1r = t1r + t3r;
                float b1i = t1i + t3i;
                float b2r = t0r - t2r;
                float b2i = t0i - t2i;
                float b3r = t1r - t3r;
                float b3i = t1i - t3i;
                Unsafe.Add(ref yr, o + s + q) = b1r * w1r - b1i * w1i;
                Unsafe.Add(ref yi, o + s + q) = b1r * w1i + b1i * w1r;
                Unsafe.Add(ref yr, o + 2 * s + q) = b2r * w2r - b2i * w2i;
                Unsafe.Add(ref yi, o + 2 * s + q) = b2r * w2i + b2i * w2r;
                Unsafe.Add(ref yr, o + 3 * s + q) = b3r * w3r - b3i * w3i;
                Unsafe.Add(ref yi, o + 3 * s + q) = b3r * w3i + b3i * w3r;
            }
        }
    }

    private static void Radix5(Stage stage, ref float xr, ref float xi, ref float yr, ref float yi)
    {
        int s = stage.Stride;
        int m = stage.Span;
        int sm = s * m;
        int vector = VectorSpan(s);
        ref float twr = ref MemoryMarshal.GetArrayDataReference(stage.TwiddleRe);
        ref float twi = ref MemoryMarshal.GetArrayDataReference(stage.TwiddleIm);
        var c1 = new Vector<float>(Cos72);
        var c2 = new Vector<float>(Cos144);
        var s1 = new Vector<float>(Sin72);
        var s2 = new Vector<float>(Sin144);
        for (int p = 0; p < m; p++)
        {
            int t = 4 * p;
            float w1r = Unsafe.Add(ref twr, t), w1i = Unsafe.Add(ref twi, t);
            float w2r = Unsafe.Add(ref twr, t + 1), w2i = Unsafe.Add(ref twi, t + 1);
            float w3r = Unsafe.Add(ref twr, t + 2), w3i = Unsafe.Add(ref twi, t + 2);
            float w4r = Unsafe.Add(ref twr, t + 3), w4i = Unsafe.Add(ref twi, t + 3);
            int a = s * p;
            int o = 5 * s * p;
            int q = 0;
            if (vector > 0)
            {
                var v1r = new Vector<float>(w1r);
                var v1i = new Vector<float>(w1i);
                var v2r = new Vector<float>(w2r);
                var v2i = new Vector<float>(w2i);
                var v3r = new Vector<float>(w3r);
                var v3i = new Vector<float>(w3i);
                var v4r = new Vector<float>(w4r);
                var v4i = new Vector<float>(w4i);
                for (; q < vector; q += Vector<float>.Count)
                {
                    var a0r = Load(ref xr, a + q);
                    var a0i = Load(ref xi, a + q);
                    var a1r = Load(ref xr, a + sm + q);
                    var a1i = Load(ref xi, a + sm + q);
                    var a2r = Load(ref xr, a + 2 * sm + q);
                    var a2i = Load(ref xi, a + 2 * sm + q);
                    var a3r = Load(ref xr, a + 3 * sm + q);
                    var a3i = Load(ref xi, a + 3 * sm + q);
                    var a4r = Load(ref xr, a + 4 * sm + q);
                    var a4i = Load(ref xi, a + 4 * sm + q);
                    var t1r = a1r + a4r;
                    var t1i = a1i + a4i;
                    var t2r = a2r + a3r;
                    var t2i = a2i + a3i;
                    var t3r = a1r - a4r;
                    var t3i = a1i - a4i;
                    var t4r = a2r - a3r;
                    var t4i = a2i - a3i;
                    var m1r = a0r + c1 * t1r + c2 * t2r;
                    var m1i = a0i + c1 * t1i + c2 * t2i;
                    var m2r = a0r + c2 * t1r + c1 * t2r;
                    var m2i = a0i + c2 * t1i + c1 * t2i;
                    var ur = s1 * t3r + s2 * t4r;
                    var ui = s1 * t3i + s2 * t4i;
                    var zr = s2 * t3r - s1 * t4r;
                    var zi = s2 * t3i - s1 * t4i;
                    Store(ref yr, o + q, a0r + t1r + t2r);
                    Store(ref yi, o + q, a0i + t1i + t2i);
                    var b1r = m1r + ui;
                    var b1i = m1i - ur;
                    var b4r = m1r - ui;
                    var b4i = m1i + ur;
                    var b2r = m2r + zi;
                    var b2i = m2i - zr;
                    var b3r = m2r - zi;
                    var b3i = m2i + zr;
                    Store(ref yr, o + s + q, b1r * v1r - b1i * v1i);
                    Store(ref yi, o + s + q, b1r * v1i + b1i * v1r);
                    Store(ref yr, o + 2 * s + q, b2r * v2r - b2i * v2i);
                    Store(ref yi, o + 2 * s + q, b2r * v2i + b2i * v2r);
                    Store(ref yr, o + 3 * s + q, b3r * v3r - b3i * v3i);
                    Store(ref yi, o + 3 * s + q, b3r * v3i + b3i * v3r);
                    Store(ref yr, o + 4 * s + q, b4r * v4r - b4i * v4i);
                    Store(ref yi, o + 4 * s + q, b4r * v4i + b4i * v4r);
                }
            }
            for (; q < s; q++)
            {
                float a0r = Unsafe.Add(ref xr, a + q);
                float a0i = Unsafe.Add(ref xi, a + q);
                float a1r = Unsafe.Add(ref xr, a + sm + q);
                float a1i = Unsafe.Add(ref xi, a + sm + q);
                float a2r = Unsafe.Add(ref xr, a + 2 * sm + q);
                float a2i = Unsafe.Add(ref xi, a + 2 * sm + q);
                float a3r = Unsafe.Add(ref xr, a + 3 * sm + q);
                float a3i = Unsafe.Add(ref xi, a + 3 * sm + q);
                float a4r = Unsafe.Add(ref xr, a + 4 * sm + q);
                float a4i = Unsafe.Add(ref xi, a + 4 * sm + q);
                float t1r = a1r + a4r;
                float t1i = a1i + a4i;
                float t2r = a2r + a3r;
                float t2i = a2i + a3i;
                float t3r = a1r - a4r;
                float t3i = a1i - a4i;
                float t4r = a2r - a3r;
                float t4i = a2i - a3i;
                float m1r = a0r + Cos72 * t1r + Cos144 * t2r;
                float m1i = a0i + Cos72 * t1i + Cos144 * t2i;
                float m2r = a0r + Cos144 * t1r + Cos72 * t2r;
                float m2i = a0i + Cos144 * t1i + Cos72 * t2i;
                float ur = Sin72 * t3r + Sin144 * t4r;
                float ui = Sin72 * t3i + Sin144 * t4i;
                float zr = Sin144 * t3r - Sin72 * t4r;
                float zi = Sin144 * t3i - Sin72 * t4i;
                Unsafe.Add(ref yr, o + q) = a0r + t1r + t2r;
                Unsafe.Add(ref yi, o + q) = a0i + t1i + t2i;
                float b1r = m1r + ui;
                float b1i = m1i - ur;
                float b4r = m1r - ui;
                float b4i = m1i + ur;
                float b2r = m2r + zi;
                float b2i = m2i - zr;
                float b3r = m2r - zi;
                float b3i = m2i + zr;
                Unsafe.Add(ref yr, o + s + q) = b1r * w1r - b1i * w1i;
                Unsafe.Add(ref yi, o + s + q) = b1r * w1i + b1i * w1r;
                Unsafe.Add(ref yr, o + 2 * s + q) = b2r * w2r - b2i * w2i;
                Unsafe.Add(ref yi, o + 2 * s + q) = b2r * w2i + b2i * w2r;
                Unsafe.Add(ref yr, o + 3 * s + q) = b3r * w3r - b3i * w3i;
                Unsafe.Add(ref yi, o + 3 * s + q) = b3r * w3i + b3i * w3r;
                Unsafe.Add(ref yr, o + 4 * s + q) = b4r * w4r - b4i * w4i;
                Unsafe.Add(ref yi, o + 4 * s + q) = b4r * w4i + b4i * w4r;
            }
        }
    }

    // Any odd prime r. Inputs j and r - j go in as sum and difference: output k takes the sums
    // through cos(2 pi j k / r) and the differences through sin, and output r - k is the same two sums
    // with the sine half negated.
    private static void RadixOdd(Stage stage, ref float xr, ref float xi, ref float yr, ref float yi)
    {
        int r = stage.Radix;
        int h = (r - 1) / 2;
        int s = stage.Stride;
        int m = stage.Span;
        int sm = s * m;
        int vector = VectorSpan(s);
        float[] cos = stage.RootCos;
        float[] sin = stage.RootSin;
        ref float twr = ref MemoryMarshal.GetArrayDataReference(stage.TwiddleRe);
        ref float twi = ref MemoryMarshal.GetArrayDataReference(stage.TwiddleIm);
        Span<float> sums = stackalloc float[4 * h];
        Span<Vector<float>> lanes = vector > 0 ? stackalloc Vector<float>[4 * h] : default;

        for (int p = 0; p < m; p++)
        {
            int t = (r - 1) * p;
            int a = s * p;
            int o = r * s * p;
            int q = 0;
            for (; q < vector; q += Vector<float>.Count)
            {
                var a0r = Load(ref xr, a + q);
                var a0i = Load(ref xi, a + q);
                var dcr = a0r;
                var dci = a0i;
                for (int j = 1; j <= h; j++)
                {
                    var ljr = Load(ref xr, a + j * sm + q);
                    var lji = Load(ref xi, a + j * sm + q);
                    var hjr = Load(ref xr, a + (r - j) * sm + q);
                    var hji = Load(ref xi, a + (r - j) * sm + q);
                    var pr = ljr + hjr;
                    var pi = lji + hji;
                    lanes[4 * (j - 1)] = pr;
                    lanes[4 * (j - 1) + 1] = pi;
                    lanes[4 * (j - 1) + 2] = ljr - hjr;
                    lanes[4 * (j - 1) + 3] = lji - hji;
                    dcr += pr;
                    dci += pi;
                }
                Store(ref yr, o + q, dcr);
                Store(ref yi, o + q, dci);
                for (int k = 1; k <= h; k++)
                {
                    var ar = a0r;
                    var ai = a0i;
                    var br = Vector<float>.Zero;
                    var bi = Vector<float>.Zero;
                    int turn = 0;
                    for (int j = 1; j <= h; j++)
                    {
                        turn += k;
                        if (turn >= r)
                            turn -= r;
                        var c = new Vector<float>(cos[turn]);
                        var sn = new Vector<float>(sin[turn]);
                        ar += lanes[4 * (j - 1)] * c;
                        ai += lanes[4 * (j - 1) + 1] * c;
                        br += lanes[4 * (j - 1) + 2] * sn;
                        bi += lanes[4 * (j - 1) + 3] * sn;
                    }
                    var ukr = ar + bi;
                    var uki = ai - br;
                    var dkr = ar - bi;
                    var dki = ai + br;
                    var wkr = new Vector<float>(Unsafe.Add(ref twr, t + k - 1));
                    var wki = new Vector<float>(Unsafe.Add(ref twi, t + k - 1));
                    var wlr = new Vector<float>(Unsafe.Add(ref twr, t + r - k - 1));
                    var wli = new Vector<float>(Unsafe.Add(ref twi, t + r - k - 1));
                    Store(ref yr, o + k * s + q, ukr * wkr - uki * wki);
                    Store(ref yi, o + k * s + q, ukr * wki + uki * wkr);
                    Store(ref yr, o + (r - k) * s + q, dkr * wlr - dki * wli);
                    Store(ref yi, o + (r - k) * s + q, dkr * wli + dki * wlr);
                }
            }
            for (; q < s; q++)
            {
                float a0r = Unsafe.Add(ref xr, a + q);
                float a0i = Unsafe.Add(ref xi, a + q);
                float dcr = a0r;
                float dci = a0i;
                for (int j = 1; j <= h; j++)
                {
                    float ljr = Unsafe.Add(ref xr, a + j * sm + q);
                    float lji = Unsafe.Add(ref xi, a + j * sm + q);
                    float hjr = Unsafe.Add(ref xr, a + (r - j) * sm + q);
                    float hji = Unsafe.Add(ref xi, a + (r - j) * sm + q);
                    float pr = ljr + hjr;
                    float pi = lji + hji;
                    sums[4 * (j - 1)] = pr;
                    sums[4 * (j - 1) + 1] = pi;
                    sums[4 * (j - 1) + 2] = ljr - hjr;
                    sums[4 * (j - 1) + 3] = lji - hji;
                    dcr += pr;
                    dci += pi;
                }
                Unsafe.Add(ref yr, o + q) = dcr;
                Unsafe.Add(ref yi, o + q) = dci;
                for (int k = 1; k <= h; k++)
                {
                    float ar = a0r;
                    float ai = a0i;
                    float br = 0f;
                    float bi = 0f;
                    int turn = 0;
                    for (int j = 1; j <= h; j++)
                    {
                        turn += k;
                        if (turn >= r)
                            turn -= r;
                        float c = cos[turn];
                        float sn = sin[turn];
                        ar += sums[4 * (j - 1)] * c;
                        ai += sums[4 * (j - 1) + 1] * c;
                        br += sums[4 * (j - 1) + 2] * sn;
                        bi += sums[4 * (j - 1) + 3] * sn;
                    }
                    float ukr = ar + bi;
                    float uki = ai - br;
                    float dkr = ar - bi;
                    float dki = ai + br;
                    float wkr = Unsafe.Add(ref twr, t + k - 1);
                    float wki = Unsafe.Add(ref twi, t + k - 1);
                    float wlr = Unsafe.Add(ref twr, t + r - k - 1);
                    float wli = Unsafe.Add(ref twi, t + r - k - 1);
                    Unsafe.Add(ref yr, o + k * s + q) = ukr * wkr - uki * wki;
                    Unsafe.Add(ref yi, o + k * s + q) = ukr * wki + uki * wkr;
                    Unsafe.Add(ref yr, o + (r - k) * s + q) = dkr * wlr - dki * wli;
                    Unsafe.Add(ref yi, o + (r - k) * s + q) = dkr * wli + dki * wlr;
                }
            }
        }
    }

    // Stage order: fours, then a two, then odd primes from smallest to largest.
    private static List<int> Factor(int size)
    {
        var factors = new List<int>();
        int n = size;
        while (n % 4 == 0)
        {
            factors.Add(4);
            n /= 4;
        }
        while (n % 2 == 0)
        {
            factors.Add(2);
            n /= 2;
        }
        for (int p = 3; (long)p * p <= n; p += 2)
        {
            while (n % p == 0)
            {
                factors.Add(p);
                n /= p;
            }
        }
        if (n > 1)
            factors.Add(n);
        return factors;
    }
}
