// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Samples in, one windowed spectrum out every Hop samples.
//
// Starts primed with FrameSize - Hop zeros, so a frame is ready after a single hop and every sample
// that is written sits under the full stack of frames that overlap it. That is what lets an StftSynthesizer
// on the other end give back exactly what came in, delayed by FrameSize - Hop. Allocation free once built;
// one instance per stream. -xlinka
public sealed class StftAnalyzer
{
    private readonly RealFft _fft;
    private readonly float[] _window;
    private readonly float[] _buffer;
    private readonly float[] _frame;
    private readonly float[] _scratch;
    private readonly bool _primed;
    private int _filled;

    public StftAnalyzer(int frameSize, int hop, WindowShape window = WindowShape.Hann, float kaiserBeta = SpectralWindow.DefaultKaiserBeta, bool primed = true)
        : this(frameSize, hop, SpectralWindow.Create(Math.Max(1, frameSize), window, true, kaiserBeta), primed)
    {
    }

    public StftAnalyzer(int frameSize, int hop, float[] window, bool primed = true)
    {
        if (frameSize < 2)
            throw new ArgumentOutOfRangeException(nameof(frameSize), "a frame needs at least two samples");
        if (hop < 1 || hop > frameSize)
            throw new ArgumentOutOfRangeException(nameof(hop), "the hop runs from one sample to a whole frame");
        if (window == null || window.Length != frameSize)
            throw new ArgumentException("the window is one value per frame sample", nameof(window));
        FrameSize = frameSize;
        Hop = hop;
        _fft = RealFft.Get(frameSize);
        _window = (float[])window.Clone();
        _buffer = new float[frameSize];
        _frame = new float[frameSize];
        _scratch = new float[_fft.ScratchLength];
        _primed = primed;
        Reset();
    }

    public int FrameSize { get; }

    public int Hop { get; }

    public int BinCount => FrameSize / 2 + 1;

    public ReadOnlySpan<float> Window => _window;

    // Samples still wanted before the next frame is ready.
    public int Needed => FrameSize - _filled;

    public bool IsFrameReady => _filled == FrameSize;

    public long FramesRead { get; private set; }

    public void Reset()
    {
        Array.Clear(_buffer);
        _filled = _primed ? FrameSize - Hop : 0;
        FramesRead = 0;
    }

    // Takes up to Needed samples and returns how many it took; the rest wait for the frame to be read.
    public int Write(ReadOnlySpan<float> samples)
    {
        int take = Math.Min(samples.Length, FrameSize - _filled);
        samples.Slice(0, take).CopyTo(_buffer.AsSpan(_filled));
        _filled += take;
        return take;
    }

    // The ready frame's spectrum, unscaled; then the frame moves on by a hop.
    public bool TryRead(Span<float> real, Span<float> imaginary)
    {
        if (_filled < FrameSize)
            return false;
        CopyWindowed(_frame);
        _fft.Forward(_frame, real, imaginary, _scratch);
        Advance();
        return true;
    }

    // The ready frame as windowed samples instead of a spectrum, for a caller with its own transform.
    public bool TryReadSamples(Span<float> windowed)
    {
        if (_filled < FrameSize)
            return false;
        CopyWindowed(windowed);
        Advance();
        return true;
    }

    private void CopyWindowed(Span<float> destination)
    {
        for (int i = 0; i < FrameSize; i++)
            destination[i] = _buffer[i] * _window[i];
    }

    private void Advance()
    {
        Array.Copy(_buffer, Hop, _buffer, 0, FrameSize - Hop);
        _filled = FrameSize - Hop;
        FramesRead++;
    }
}
