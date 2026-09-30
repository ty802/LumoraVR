// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Doubles the rate of one channel through a half-band filter: each input sample becomes two, the earlier from the
// odd-tap FIR and the later the input M/2 samples back. The history is written twice, half a buffer apart, so the
// last M + 1 inputs always sit in one straight run for the dot product. Latency is M samples at the doubled rate.
internal sealed class HalfbandUpsampler
{
    private readonly float[] _coefficients;
    private readonly float[] _history;
    private readonly int _length;
    private readonly int _delay;
    private int _write;

    public HalfbandUpsampler(float[] coefficients)
    {
        _coefficients = coefficients;
        _length = coefficients.Length;
        _history = new float[2 * _length];
        _delay = (_length - 2) / 2;
    }

    public void Process(float input, out float first, out float second)
    {
        int length = _length;
        int write = _write;
        _history[write] = input;
        _history[write + length] = input;
        first = 2f * Halfband.Dot(_coefficients, _history.AsSpan(write + 1, length));
        second = _history[write + length - _delay];
        _write = write + 1 == length ? 0 : write + 1;
    }

    public void Clear()
    {
        Array.Clear(_history);
        _write = 0;
    }
}
