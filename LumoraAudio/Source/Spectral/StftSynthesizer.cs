// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// One spectrum in, Hop samples out: inverse transform, synthesis window, overlap-add.
//
// Each output sample is divided by what the analysis and synthesis windows overlap-add to at its place in
// the hop. For a COLA pair that sum is one flat number and this is plain overlap-add scaled by it (IsFlat
// says so); for any other pair it still gives the input back, as long as the sum never reaches zero. A
// rectangular synthesis window is plain overlap-add, the analysis shape again is weighted overlap-add. Fed
// by an StftAnalyzer of the same frame, hop and analysis window, the output is the input FrameSize - Hop
// samples late. -xlinka
public sealed class StftSynthesizer
{
    private readonly RealFft _fft;
    private readonly float[] _synthesis;
    private readonly float[] _inverseNorm;
    private readonly float[] _accumulator;
    private readonly float[] _frame;
    private readonly float[] _scratch;

    public StftSynthesizer(int frameSize, int hop, WindowShape analysis = WindowShape.Hann, WindowShape synthesis = WindowShape.Rectangular,
        float kaiserBeta = SpectralWindow.DefaultKaiserBeta)
        : this(frameSize, hop, SpectralWindow.Create(Math.Max(1, frameSize), analysis, true, kaiserBeta),
            SpectralWindow.Create(Math.Max(1, frameSize), synthesis, true, kaiserBeta))
    {
    }

    public StftSynthesizer(int frameSize, int hop, float[] analysis, float[] synthesis)
    {
        if (frameSize < 2)
            throw new ArgumentOutOfRangeException(nameof(frameSize), "a frame needs at least two samples");
        if (hop < 1 || hop > frameSize)
            throw new ArgumentOutOfRangeException(nameof(hop), "the hop runs from one sample to a whole frame");
        if (analysis == null || analysis.Length != frameSize || synthesis == null || synthesis.Length != frameSize)
            throw new ArgumentException("both windows are one value per frame sample");
        FrameSize = frameSize;
        Hop = hop;
        _fft = RealFft.Get(frameSize);
        _synthesis = (float[])synthesis.Clone();
        _accumulator = new float[frameSize];
        _frame = new float[frameSize];
        _scratch = new float[_fft.ScratchLength];

        var sum = new float[hop];
        SpectralWindow.OverlapSum(analysis, synthesis, hop, sum);
        _inverseNorm = new float[hop];
        float low = float.MaxValue;
        float high = 0f;
        for (int i = 0; i < hop; i++)
        {
            float magnitude = Math.Abs(sum[i]);
            low = Math.Min(low, magnitude);
            high = Math.Max(high, magnitude);
            _inverseNorm[i] = magnitude > 1e-9f ? 1f / sum[i] : 0f;
        }
        Gain = high;
        IsFlat = high > 0f && (high - low) <= 1e-5f * high;
        Invertible = low > 1e-9f;
    }

    public int FrameSize { get; }

    public int Hop { get; }

    public int Latency => FrameSize - Hop;

    // The overlap sum's peak; the gain plain overlap-add would leave on the signal.
    public float Gain { get; }

    // The window pair is COLA at this hop.
    public bool IsFlat { get; }

    // The overlap sum never reaches zero, so every sample comes back.
    public bool Invertible { get; }

    public void Reset() => Array.Clear(_accumulator);

    // One frame's bins in, the next Hop finished samples written to output.
    public void Write(ReadOnlySpan<float> real, ReadOnlySpan<float> imaginary, Span<float> output)
    {
        if (output.Length < Hop)
            throw new ArgumentException($"each frame gives {Hop} samples");
        _fft.Inverse(real, imaginary, _frame, _scratch);
        WriteSamples(_frame, output);
    }

    // The same from a frame already back in time, before its synthesis window.
    public void WriteSamples(ReadOnlySpan<float> frame, Span<float> output)
    {
        if (output.Length < Hop)
            throw new ArgumentException($"each frame gives {Hop} samples");
        int size = FrameSize;
        for (int i = 0; i < size; i++)
            _accumulator[i] += frame[i] * _synthesis[i];
        for (int i = 0; i < Hop; i++)
            output[i] = _accumulator[i] * _inverseNorm[i];
        Array.Copy(_accumulator, Hop, _accumulator, 0, size - Hop);
        Array.Clear(_accumulator, size - Hop, Hop);
    }
}
