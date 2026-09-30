// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Halves the rate of one channel through a half-band filter: each pair of input samples becomes one, the odd-tap
// FIR over the earlier of each pair plus half the later of the pair (M + 1)/2 pairs back. Latency is M samples at
// the input rate.
internal sealed class HalfbandDownsampler
{
    private readonly float[] _coefficients;
    private readonly float[] _history;
    private readonly float[] _centre;
    private readonly int _length;
    private int _write;
    private int _centreWrite;

    public HalfbandDownsampler(float[] coefficients)
    {
        _coefficients = coefficients;
        _length = coefficients.Length;
        _history = new float[2 * _length];
        // Read before it is overwritten, each slot of a ring as long as the delay holds the sample from that far back.
        _centre = new float[_length / 2];
    }

    public float Process(float first, float second)
    {
        int length = _length;
        int write = _write;
        _history[write] = first;
        _history[write + length] = first;
        float fir = Halfband.Dot(_coefficients, _history.AsSpan(write + 1, length));
        _write = write + 1 == length ? 0 : write + 1;

        int centreWrite = _centreWrite;
        float centre = _centre[centreWrite];
        _centre[centreWrite] = second;
        _centreWrite = centreWrite + 1 == _centre.Length ? 0 : centreWrite + 1;
        return fir + 0.5f * centre;
    }

    public void Clear()
    {
        Array.Clear(_history);
        Array.Clear(_centre);
        _write = 0;
        _centreWrite = 0;
    }
}
