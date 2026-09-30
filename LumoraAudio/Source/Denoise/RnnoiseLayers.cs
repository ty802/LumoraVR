// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

// Based on RNNoise (https://gitlab.xiph.org/xiph/rnnoise), BSD-3-Clause.
// nnet_arch.h: Copyright (c) 2018-2019 Mozilla, Copyright (c) 2023 Amazon.
// vec.h, vec_avx.h: Copyright (c) 2018 Mozilla, Copyright (c) 2008-2011 Octasic Inc.,
// Copyright (c) 2012-2017 Jean-Marc Valin.
// See LICENSE-RNNoise.txt.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Lumora.Audio;

// A dense float layer: conv1, dense_out and vad_dense. Each output is summed over the inputs in
// order with a multiply then an add, the order RNNoise's sgemv keeps in every lane, so running
// four outputs per vector changes nothing in the result.
internal sealed class FloatLinearLayer
{
    internal readonly int Inputs;
    internal readonly int Outputs;

    // Input-major: weight of input j for output i at [j * Outputs + i].
    private readonly float[] _weights;
    private readonly float[] _bias;

    internal FloatLinearLayer(float[] weights, float[] bias, int inputs, int outputs)
    {
        _weights = weights;
        _bias = bias;
        Inputs = inputs;
        Outputs = outputs;
    }

    internal void Compute(ReadOnlySpan<float> input, Span<float> output)
    {
        input = input.Slice(0, Inputs);
        output = output.Slice(0, Outputs);
        if (RnnoiseCpu.Vectors && Outputs % 4 == 0)
            MultiplyVector(input, output);
        else
            MultiplyScalar(input, output);
        for (int i = 0; i < Outputs; i++)
            output[i] += _bias[i];
    }

    private unsafe void MultiplyVector(ReadOnlySpan<float> input, Span<float> output)
    {
        int n = Outputs;
        int m = Inputs;
        fixed (float* w = _weights)
        fixed (float* x = input)
        fixed (float* y = output)
        {
            int i = 0;
            for (; i + 16 <= n; i += 16)
            {
                Vector128<float> a0 = Vector128<float>.Zero;
                Vector128<float> a1 = Vector128<float>.Zero;
                Vector128<float> a2 = Vector128<float>.Zero;
                Vector128<float> a3 = Vector128<float>.Zero;
                float* col = w + i;
                for (int j = 0; j < m; j++, col += n)
                {
                    Vector128<float> xj = Vector128.Create(x[j]);
                    a0 += Vector128.Load(col) * xj;
                    a1 += Vector128.Load(col + 4) * xj;
                    a2 += Vector128.Load(col + 8) * xj;
                    a3 += Vector128.Load(col + 12) * xj;
                }
                a0.Store(y + i);
                a1.Store(y + i + 4);
                a2.Store(y + i + 8);
                a3.Store(y + i + 12);
            }
            for (; i + 4 <= n; i += 4)
            {
                Vector128<float> a0 = Vector128<float>.Zero;
                float* col = w + i;
                for (int j = 0; j < m; j++, col += n)
                    a0 += Vector128.Load(col) * Vector128.Create(x[j]);
                a0.Store(y + i);
            }
        }
    }

    // Four outputs per pass, each still its own chain over the inputs in order.
    private void MultiplyScalar(ReadOnlySpan<float> input, Span<float> output)
    {
        int n = Outputs;
        int m = Inputs;
        float[] w = _weights;
        int i = 0;
        for (; i + 4 <= n; i += 4)
        {
            float s0 = 0, s1 = 0, s2 = 0, s3 = 0;
            for (int j = 0; j < m; j++)
            {
                float xj = input[j];
                int at = j * n + i;
                s0 += w[at] * xj;
                s1 += w[at + 1] * xj;
                s2 += w[at + 2] * xj;
                s3 += w[at + 3] * xj;
            }
            output[i] = s0;
            output[i + 1] = s1;
            output[i + 2] = s2;
            output[i + 3] = s3;
        }
        for (; i < n; i++)
        {
            float sum = 0;
            for (int j = 0; j < m; j++)
                sum += w[j * n + i] * input[j];
            output[i] = sum;
        }
    }
}

// An int8 layer: conv2 and the GRU input and recurrent matrices, dense or block sparse. RNNoise's
// x86 build quantizes the input to unsigned bytes, 127 + floor(0.5 + 127 * x), takes an exact
// integer dot product per output, converts it to float, multiplies by the output's scale and adds
// subias (USE_SU_BIAS). Every path here produces that same integer, so the float that follows is
// bit identical whichever kernel ran. -xlinka
internal sealed class Int8LinearLayer
{
    private const int BlockSize = 32;

    internal readonly int Inputs;
    internal readonly int Outputs;

    private readonly int _groups;
    private readonly int[] _groupEnd;
    private readonly int[] _blockInput;
    private readonly float[] _scale;
    private readonly float[] _subias;
    private readonly float[]? _diag;

    // RNNoise's layout: 8 outputs by 4 inputs per block, output-major, for maddubs.
    private readonly sbyte[] _blocks;

    // The same blocks input-major, 8 outputs per input, for the portable and scalar kernels.
    private readonly sbyte[] _columns;

    // 127 times the sum of each output's weights: the portable kernels multiply by the signed
    // input q, and the unsigned product the C code takes is that plus 127 times the weight sum.
    private readonly int[] _offset;

    // True when no pair of adjacent weights can push maddubs past 16 bits with inputs up to 254.
    private readonly bool _pairSafe;

    private Int8LinearLayer(sbyte[] blocks, int[] groupEnd, int[] blockInput, float[] scale, float[] subias, float[]? diag, int inputs, int outputs)
    {
        Inputs = inputs;
        Outputs = outputs;
        _groups = outputs / 8;
        _groupEnd = groupEnd;
        _blockInput = blockInput;
        _scale = scale;
        _subias = subias;
        _diag = diag;
        _blocks = blocks;
        _columns = new sbyte[blocks.Length];
        _offset = new int[outputs];
        _pairSafe = true;

        int b = 0;
        for (int g = 0; g < _groups; g++)
        {
            for (; b < groupEnd[g]; b++)
            {
                int o = b * BlockSize;
                for (int r = 0; r < 8; r++)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        sbyte w = blocks[o + r * 4 + c];
                        _columns[o + c * 8 + r] = w;
                        _offset[g * 8 + r] += 127 * w;
                    }
                    if (PairPeak(blocks[o + r * 4], blocks[o + r * 4 + 1]) > short.MaxValue
                        || PairPeak(blocks[o + r * 4 + 2], blocks[o + r * 4 + 3]) > short.MaxValue)
                        _pairSafe = false;
                }
            }
        }
    }

    private static int PairPeak(int a, int b)
    {
        int pos = Math.Max(a, 0) + Math.Max(b, 0);
        int neg = Math.Max(-a, 0) + Math.Max(-b, 0);
        return 254 * Math.Max(pos, neg);
    }

    // cgemv8x4 weights: groups of 8 outputs, every group holding inputs / 4 blocks in input order.
    internal static Int8LinearLayer Dense(sbyte[] weights, float[] scale, float[] subias, int inputs, int outputs)
    {
        int perGroup = inputs / 4;
        int groups = outputs / 8;
        var groupEnd = new int[groups];
        var blockInput = new int[groups * perGroup];
        for (int g = 0; g < groups; g++)
        {
            groupEnd[g] = (g + 1) * perGroup;
            for (int j = 0; j < perGroup; j++)
                blockInput[g * perGroup + j] = 4 * j;
        }
        if (weights.Length != groups * perGroup * BlockSize)
            throw new InvalidDataException($"dense int8 matrix holds {weights.Length} weights, expected {groups * perGroup * BlockSize}");
        return new Int8LinearLayer(weights, groupEnd, blockInput, scale, subias, null, inputs, outputs);
    }

    // sparse_cgemv8x4 weights: idx lists per group of 8 outputs a block count, then the first input
    // of each block. Validated the way find_idx_check does.
    internal static Int8LinearLayer Sparse(sbyte[] weights, int[] idx, float[] scale, float[] subias, float[]? diag, int inputs, int outputs)
    {
        int groups = outputs / 8;
        var groupEnd = new int[groups];
        int blocks = 0;
        int k = 0;
        for (int g = 0; g < groups; g++)
        {
            if (k >= idx.Length)
                throw new InvalidDataException($"sparse index ends after {g} of {groups} output groups");
            int count = idx[k++];
            if (count < 0 || count > idx.Length - k)
                throw new InvalidDataException($"sparse index group {g} claims {count} blocks");
            k += count;
            blocks += count;
            groupEnd[g] = blocks;
        }
        if (k != idx.Length)
            throw new InvalidDataException($"sparse index has {idx.Length - k} trailing entries");
        if (weights.Length != blocks * BlockSize)
            throw new InvalidDataException($"sparse matrix holds {weights.Length} weights, index describes {blocks * BlockSize}");

        var blockInput = new int[blocks];
        k = 0;
        int b = 0;
        for (int g = 0; g < groups; g++)
        {
            int count = idx[k++];
            for (int j = 0; j < count; j++)
            {
                int pos = idx[k++];
                if (pos < 0 || pos + 3 >= inputs || (pos & 3) != 0)
                    throw new InvalidDataException($"sparse block input {pos} out of range for {inputs} inputs");
                blockInput[b++] = pos;
            }
        }
        return new Int8LinearLayer(weights, groupEnd, blockInput, scale, subias, diag, inputs, outputs);
    }

    // output = scale * (int8 weights . quantized input) + subias, then the GRU diagonal term.
    // signedScratch and unsignedScratch need Inputs entries.
    internal unsafe void Compute(ReadOnlySpan<float> input, Span<float> output, Span<short> signedScratch, Span<byte> unsignedScratch)
    {
        input = input.Slice(0, Inputs);
        output = output.Slice(0, Outputs);
        fixed (float* y = output)
        {
            if (_pairSafe && RnnoiseCpu.Avx2Dot)
            {
                QuantizeUnsigned(input, unsignedScratch);
                fixed (byte* x = unsignedScratch)
                    DotAvx2(x, y);
            }
            else
            {
                QuantizeSigned(input, signedScratch);
                fixed (short* x = signedScratch)
                {
                    if (RnnoiseCpu.Vectors)
                        DotVector(x, y);
                    else
                        DotScalar(x, y);
                }
            }
        }

        int n = Outputs;
        int i = 0;
        if (RnnoiseCpu.Vectors)
        {
            ref float o = ref MemoryMarshal.GetReference(output);
            ref float s = ref MemoryMarshal.GetArrayDataReference(_subias);
            for (; i + 4 <= n; i += 4)
                (Vector128.LoadUnsafe(ref o, (nuint)i) + Vector128.LoadUnsafe(ref s, (nuint)i)).StoreUnsafe(ref o, (nuint)i);
        }
        for (; i < n; i++)
            output[i] += _subias[i];

        if (_diag != null)
            AddDiagonal(input, output);
    }

    // Recurrent matrices keep their diagonal in float: out[i + k * M] += diag[i + k * M] * in[i].
    private void AddDiagonal(ReadOnlySpan<float> input, Span<float> output)
    {
        float[] diag = _diag!;
        int m = Inputs;
        int i = 0;
        if (RnnoiseCpu.Vectors)
        {
            ref float o = ref MemoryMarshal.GetReference(output);
            ref float d = ref MemoryMarshal.GetArrayDataReference(diag);
            ref float x = ref MemoryMarshal.GetReference(input);
            for (; i + 4 <= m; i += 4)
            {
                Vector128<float> xi = Vector128.LoadUnsafe(ref x, (nuint)i);
                for (int k = 0; k < 3; k++)
                {
                    nuint at = (nuint)(i + k * m);
                    (Vector128.LoadUnsafe(ref o, at) + Vector128.LoadUnsafe(ref d, at) * xi).StoreUnsafe(ref o, at);
                }
            }
        }
        for (; i < m; i++)
        {
            output[i] += diag[i] * input[i];
            output[i + m] += diag[i + m] * input[i];
            output[i + 2 * m] += diag[i + 2 * m] * input[i];
        }
    }

    // q = floor(0.5 + t) with t = 127 * x in float, which RNNoise evaluates in double. In float,
    // t - floor(t) is exact except for t in (-0.5, 0), where it rounds but cannot drop below 0.5,
    // so testing it against 0.5 picks the same integer.
    private static Vector128<int> RoundHalfUp(Vector128<float> x)
    {
        Vector128<float> t = Vector128.Create(127f) * x;
        Vector128<float> f = Vector128.Floor(t);
        Vector128<float> up = Vector128.GreaterThanOrEqual(t - f, Vector128.Create(0.5f)) & Vector128<float>.One;
        return Vector128.ConvertToInt32(f + up);
    }

    private static void QuantizeSigned(ReadOnlySpan<float> input, Span<short> q)
    {
        int n = input.Length;
        int i = 0;
        if (RnnoiseCpu.Vectors)
        {
            ref float x = ref MemoryMarshal.GetReference(input);
            ref short d = ref MemoryMarshal.GetReference(q);
            for (; i + 8 <= n; i += 8)
            {
                Vector128<int> lo = RoundHalfUp(Vector128.LoadUnsafe(ref x, (nuint)i));
                Vector128<int> hi = RoundHalfUp(Vector128.LoadUnsafe(ref x, (nuint)(i + 4)));
                Vector128.Narrow(lo, hi).StoreUnsafe(ref d, (nuint)i);
            }
        }
        for (; i < n; i++)
            q[i] = (short)Math.Floor(0.5 + 127f * input[i]);
    }

    private static void QuantizeUnsigned(ReadOnlySpan<float> input, Span<byte> q)
    {
        int n = input.Length;
        int i = 0;
        ref float x = ref MemoryMarshal.GetReference(input);
        ref byte d = ref MemoryMarshal.GetReference(q);
        Vector128<int> bias = Vector128.Create(127);
        for (; i + 16 <= n; i += 16)
        {
            Vector128<short> a = Vector128.Narrow(
                RoundHalfUp(Vector128.LoadUnsafe(ref x, (nuint)i)) + bias,
                RoundHalfUp(Vector128.LoadUnsafe(ref x, (nuint)(i + 4))) + bias);
            Vector128<short> b = Vector128.Narrow(
                RoundHalfUp(Vector128.LoadUnsafe(ref x, (nuint)(i + 8))) + bias,
                RoundHalfUp(Vector128.LoadUnsafe(ref x, (nuint)(i + 12))) + bias);
            Vector128.Narrow(a.AsUInt16(), b.AsUInt16()).StoreUnsafe(ref d, (nuint)i);
        }
        for (; i < n; i++)
            q[i] = (byte)(127 + (int)Math.Floor(0.5 + 127f * input[i]));
    }

    // _mm256_maddubs_epi16 then _mm256_madd_epi16 with ones, as RNNoise's AVX2 build does. Only
    // taken when _pairSafe, so the 16-bit pair sums are exact and match the SSE2 build.
    private unsafe void DotAvx2(byte* x, float* y)
    {
        Vector256<short> ones = Vector256.Create((short)1);
        fixed (sbyte* blocks = _blocks)
        fixed (int* pos = _blockInput)
        fixed (int* end = _groupEnd)
        fixed (float* scale = _scale)
        {
            sbyte* w = blocks;
            int b = 0;
            for (int g = 0; g < _groups; g++)
            {
                Vector256<int> acc0 = Vector256<int>.Zero;
                Vector256<int> acc1 = Vector256<int>.Zero;
                int e = end[g];
                for (; b + 1 < e; b += 2, w += 2 * BlockSize)
                {
                    Vector256<byte> x0 = Vector256.Create(*(int*)(x + pos[b])).AsByte();
                    Vector256<byte> x1 = Vector256.Create(*(int*)(x + pos[b + 1])).AsByte();
                    acc0 = Avx2.Add(acc0, Avx2.MultiplyAddAdjacent(Avx2.MultiplyAddAdjacent(x0, Avx.LoadVector256(w)), ones));
                    acc1 = Avx2.Add(acc1, Avx2.MultiplyAddAdjacent(Avx2.MultiplyAddAdjacent(x1, Avx.LoadVector256(w + BlockSize)), ones));
                }
                if (b < e)
                {
                    Vector256<byte> x0 = Vector256.Create(*(int*)(x + pos[b])).AsByte();
                    acc0 = Avx2.Add(acc0, Avx2.MultiplyAddAdjacent(Avx2.MultiplyAddAdjacent(x0, Avx.LoadVector256(w)), ones));
                    b++;
                    w += BlockSize;
                }
                Vector256<float> sum = Avx.ConvertToVector256Single(Avx2.Add(acc0, acc1));
                Avx.Store(y + g * 8, Avx.Multiply(sum, Avx.LoadVector256(scale + g * 8)));
            }
        }
    }

    // Portable kernel on the input-major copy: 16-bit products of weights and signed inputs, two
    // inputs summed per step (at most 2 * 128 * 127, no overflow), widened into 32-bit sums.
    private unsafe void DotVector(short* x, float* y)
    {
        fixed (sbyte* blocks = _columns)
        fixed (int* pos = _blockInput)
        fixed (int* end = _groupEnd)
        fixed (int* offset = _offset)
        fixed (float* scale = _scale)
        {
            sbyte* w = blocks;
            int b = 0;
            for (int g = 0; g < _groups; g++)
            {
                Vector128<int> lo = Vector128.Load(offset + g * 8);
                Vector128<int> hi = Vector128.Load(offset + g * 8 + 4);
                int e = end[g];
                for (; b < e; b++, w += BlockSize)
                {
                    short* xp = x + pos[b];
                    Vector128<sbyte> w01 = Vector128.Load(w);
                    Vector128<sbyte> w23 = Vector128.Load(w + 16);
                    Vector128<short> p01 = Vector128.WidenLower(w01) * Vector128.Create(xp[0]) + Vector128.WidenUpper(w01) * Vector128.Create(xp[1]);
                    Vector128<short> p23 = Vector128.WidenLower(w23) * Vector128.Create(xp[2]) + Vector128.WidenUpper(w23) * Vector128.Create(xp[3]);
                    lo += Vector128.WidenLower(p01) + Vector128.WidenLower(p23);
                    hi += Vector128.WidenUpper(p01) + Vector128.WidenUpper(p23);
                }
                (Vector128.ConvertToSingle(lo) * Vector128.Load(scale + g * 8)).Store(y + g * 8);
                (Vector128.ConvertToSingle(hi) * Vector128.Load(scale + g * 8 + 4)).Store(y + g * 8 + 4);
            }
        }
    }

    private unsafe void DotScalar(short* x, float* y)
    {
        fixed (sbyte* blocks = _columns)
        fixed (int* pos = _blockInput)
        fixed (int* end = _groupEnd)
        fixed (int* offset = _offset)
        fixed (float* scale = _scale)
        {
            sbyte* w = blocks;
            int b = 0;
            for (int g = 0; g < _groups; g++)
            {
                int* o = offset + g * 8;
                int a0 = o[0], a1 = o[1], a2 = o[2], a3 = o[3], a4 = o[4], a5 = o[5], a6 = o[6], a7 = o[7];
                for (; b < end[g]; b++)
                {
                    short* xp = x + pos[b];
                    for (int c = 0; c < 4; c++, w += 8)
                    {
                        int xc = xp[c];
                        a0 += w[0] * xc;
                        a1 += w[1] * xc;
                        a2 += w[2] * xc;
                        a3 += w[3] * xc;
                        a4 += w[4] * xc;
                        a5 += w[5] * xc;
                        a6 += w[6] * xc;
                        a7 += w[7] * xc;
                    }
                }
                float* sc = scale + g * 8;
                float* d = y + g * 8;
                d[0] = a0 * sc[0];
                d[1] = a1 * sc[1];
                d[2] = a2 * sc[2];
                d[3] = a3 * sc[3];
                d[4] = a4 * sc[4];
                d[5] = a5 * sc[5];
                d[6] = a6 * sc[6];
                d[7] = a7 * sc[7];
            }
        }
    }
}
