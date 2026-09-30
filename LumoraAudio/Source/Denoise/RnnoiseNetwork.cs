// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

// Based on RNNoise (https://gitlab.xiph.org/xiph/rnnoise), BSD-3-Clause.
// rnn.c: Copyright (c) 2008-2011 Octasic Inc., Copyright (c) 2012-2017 Jean-Marc Valin.
// nnet.c: Copyright (c) 2018 Mozilla, Copyright (c) 2008-2011 Octasic Inc.,
// Copyright (c) 2012-2017 Jean-Marc Valin.
// See LICENSE-RNNoise.txt.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Lumora.Audio;

// The RNNoise graph (compute_rnn) with its recurrent state and scratch: two conv1d layers of
// kernel 3, three GRUs, then the 32 band gains and the voice probability from all four outputs.
internal sealed class RnnoiseNetwork
{
    internal const int FeatureCount = 65;
    internal const int GainCount = 32;

    private const int Conv1Out = 128;
    private const int Conv2Out = 384;
    private const int GruSize = 384;

    private readonly RnnoiseModel _model;

    private readonly float[] _conv1State = new float[FeatureCount * 2];
    private readonly float[] _conv2State = new float[Conv1Out * 2];
    private readonly float[] _gru1State = new float[GruSize];
    private readonly float[] _gru2State = new float[GruSize];
    private readonly float[] _gru3State = new float[GruSize];

    private readonly float[] _convInput = new float[Conv2Out];
    private readonly float[] _conv1Output = new float[Conv1Out];
    private readonly float[] _cat = new float[Conv2Out + 3 * GruSize];
    private readonly float[] _zrh = new float[3 * GruSize];
    private readonly float[] _recur = new float[3 * GruSize];
    private readonly float[] _vad = new float[1];
    private readonly short[] _signedInput = new short[GruSize];
    private readonly byte[] _unsignedInput = new byte[GruSize];

    internal RnnoiseNetwork(RnnoiseModel model)
    {
        _model = model;
    }

    internal void Reset()
    {
        Array.Clear(_conv1State);
        Array.Clear(_conv2State);
        Array.Clear(_gru1State);
        Array.Clear(_gru2State);
        Array.Clear(_gru3State);
    }

    // Writes GainCount band gains and returns the voice probability.
    internal float Compute(ReadOnlySpan<float> features, Span<float> gains)
    {
        RnnoiseModel m = _model;
        Span<float> cat = _cat;
        Conv1d(m.Conv1, _conv1Output, _conv1State, features.Slice(0, FeatureCount));
        Conv1d(m.Conv2, cat.Slice(0, Conv2Out), _conv2State, _conv1Output);
        Gru(m.Gru1Input, m.Gru1Recurrent, _gru1State, cat.Slice(0, Conv2Out));
        Gru(m.Gru2Input, m.Gru2Recurrent, _gru2State, _gru1State);
        Gru(m.Gru3Input, m.Gru3Recurrent, _gru3State, _gru2State);
        _gru1State.CopyTo(cat.Slice(Conv2Out));
        _gru2State.CopyTo(cat.Slice(Conv2Out + GruSize));
        _gru3State.CopyTo(cat.Slice(Conv2Out + 2 * GruSize));
        m.DenseOut.Compute(cat, gains);
        RnnoiseActivations.Sigmoid(gains.Slice(0, GainCount));
        m.VadDense.Compute(cat, _vad);
        RnnoiseActivations.Sigmoid(_vad);
        return _vad[0];
    }

    // compute_generic_conv1d: the layer sees the two previous inputs followed by this one, the
    // state keeps the last two, tanh on the way out.
    private void Conv1d(FloatLinearLayer layer, Span<float> output, float[] state, ReadOnlySpan<float> input)
    {
        Span<float> tmp = ConvWindow(state, input);
        layer.Compute(tmp, output);
        RnnoiseActivations.Tanh(output);
        tmp.Slice(input.Length).CopyTo(state);
    }

    private void Conv1d(Int8LinearLayer layer, Span<float> output, float[] state, ReadOnlySpan<float> input)
    {
        Span<float> tmp = ConvWindow(state, input);
        layer.Compute(tmp, output, _signedInput, _unsignedInput);
        RnnoiseActivations.Tanh(output);
        tmp.Slice(input.Length).CopyTo(state);
    }

    private Span<float> ConvWindow(float[] state, ReadOnlySpan<float> input)
    {
        Span<float> tmp = _convInput.AsSpan(0, state.Length + input.Length);
        state.CopyTo(tmp);
        input.CopyTo(tmp.Slice(state.Length));
        return tmp;
    }

    // compute_generic_gru, reset gate applied after the recurrent matrix.
    private void Gru(Int8LinearLayer inputWeights, Int8LinearLayer recurrentWeights, float[] state, ReadOnlySpan<float> input)
    {
        int n = state.Length;
        Span<float> zrh = _zrh;
        Span<float> recur = _recur;
        inputWeights.Compute(input, zrh, _signedInput, _unsignedInput);
        recurrentWeights.Compute(state, recur, _signedInput, _unsignedInput);

        ref float zr = ref MemoryMarshal.GetArrayDataReference(_zrh);
        ref float rc = ref MemoryMarshal.GetArrayDataReference(_recur);
        ref float s = ref MemoryMarshal.GetArrayDataReference(state);
        int i = 0;
        if (RnnoiseCpu.Vectors)
        {
            for (; i + 4 <= 2 * n; i += 4)
                (Vector128.LoadUnsafe(ref zr, (nuint)i) + Vector128.LoadUnsafe(ref rc, (nuint)i)).StoreUnsafe(ref zr, (nuint)i);
        }
        for (; i < 2 * n; i++)
            zrh[i] += recur[i];
        RnnoiseActivations.Sigmoid(zrh.Slice(0, 2 * n));

        // h += recur_h * r
        i = 0;
        if (RnnoiseCpu.Vectors)
        {
            for (; i + 4 <= n; i += 4)
            {
                nuint h = (nuint)(2 * n + i);
                (Vector128.LoadUnsafe(ref zr, h) + Vector128.LoadUnsafe(ref rc, h) * Vector128.LoadUnsafe(ref zr, (nuint)(n + i))).StoreUnsafe(ref zr, h);
            }
        }
        for (; i < n; i++)
            zrh[2 * n + i] += recur[2 * n + i] * zrh[n + i];
        RnnoiseActivations.Tanh(zrh.Slice(2 * n, n));

        // state = z * state + (1 - z) * h
        i = 0;
        if (RnnoiseCpu.Vectors)
        {
            for (; i + 4 <= n; i += 4)
            {
                Vector128<float> z = Vector128.LoadUnsafe(ref zr, (nuint)i);
                Vector128<float> h = Vector128.LoadUnsafe(ref zr, (nuint)(2 * n + i));
                (z * Vector128.LoadUnsafe(ref s, (nuint)i) + (Vector128<float>.One - z) * h).StoreUnsafe(ref s, (nuint)i);
            }
        }
        for (; i < n; i++)
            state[i] = zrh[i] * state[i] + (1 - zrh[i]) * zrh[2 * n + i];
    }
}
