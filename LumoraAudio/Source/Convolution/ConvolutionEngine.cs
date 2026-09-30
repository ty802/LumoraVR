// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumora.Audio;

// Uniformly partitioned overlap-save convolution of a stereo stream with one ImpulseResponse.
//
// Input is taken in blocks of B. Each block and the one before it (2B samples) are transformed once and the
// spectrum goes into a ring of the last P spectra, the frequency-domain delay line. The output block is the
// inverse transform of the sum over p of partition p's spectrum times the input spectrum p blocks old; its
// second half is the linear convolution for the new block, the half before it is wrap-around and is thrown
// away. The work per block is the same every block however long the room: two transforms and P complex
// multiply-adds a channel, never a spike.
//
// Calls that come in whole blocks (the mixer's frame is a multiple of B, which BlockSizeFor arranges) are
// convolved as they arrive and come out with no delay. A call that does not moves the engine to buffered
// framing for good, with a block of latency from then on. Input spectra of silence are marked dead and
// skipped, so a quiet room costs nothing and a room whose input stopped costs less every block until its
// tail is out.
//
// Everything it holds is allocated when it is built, which is a worker's job; Process allocates nothing.
// -xlinka
internal sealed class ConvolutionEngine
{
    private const float QuietFloor = 1e-20f;

    private readonly ImpulseResponse _response;
    private readonly RealFft _fft;
    private readonly int _block;
    private readonly int _stride;
    private readonly int _partitions;
    private readonly float[] _historyRe;
    private readonly float[] _historyIm;
    private readonly bool[] _live;
    private readonly float[] _windowLeft;
    private readonly float[] _windowRight;
    private readonly float[] _spectrumRe;
    private readonly float[] _spectrumIm;
    private readonly float[] _sumRe;
    private readonly float[] _sumIm;
    private readonly float[] _time;
    private readonly float[] _scratch;
    private readonly float[] _queueLeft;
    private readonly float[] _queueRight;
    private int _head;
    private int _fill;
    private int _queueRead;
    private int _queueCount;
    private bool _buffered;
    private int _liveSlots;
    private bool _previousLeftLive;
    private bool _previousRightLive;

    public ConvolutionEngine(ImpulseResponse response)
    {
        _response = response ?? throw new ArgumentNullException(nameof(response));
        _block = response.BlockSize;
        _stride = response.Stride;
        _partitions = response.Partitions;
        _fft = RealFft.Get(2 * _block);
        _historyRe = new float[2 * _partitions * _stride];
        _historyIm = new float[2 * _partitions * _stride];
        _live = new bool[2 * _partitions];
        _windowLeft = new float[2 * _block];
        _windowRight = new float[2 * _block];
        _spectrumRe = new float[_stride];
        _spectrumIm = new float[_stride];
        _sumRe = new float[_stride];
        _sumIm = new float[_stride];
        _time = new float[2 * _block];
        _scratch = new float[_fft.ScratchLength];
        _queueLeft = new float[2 * _block];
        _queueRight = new float[2 * _block];
    }

    public ImpulseResponse Response => _response;

    public int BlockSize => _block;

    public int Latency => _buffered ? _block : 0;

    // Some input still in the delay line, or output not yet handed out.
    public bool HasTail => _liveSlots > 0 || _queueCount > 0 && QueueHasSound();

    public void Reset()
    {
        Array.Clear(_historyRe);
        Array.Clear(_historyIm);
        Array.Clear(_live);
        Array.Clear(_windowLeft);
        Array.Clear(_windowRight);
        Array.Clear(_queueLeft);
        Array.Clear(_queueRight);
        _head = 0;
        _fill = 0;
        _queueRead = 0;
        _queueCount = 0;
        _buffered = false;
        _liveSlots = 0;
        _previousLeftLive = false;
        _previousRightLive = false;
    }

    // Takes over another engine's input history, framing and pending output so the swap to this room loses
    // nothing: the new room answers as if it had been listening all along. Only between engines of one block
    // size; the most recent spectra that fit are kept.
    public void CopyStateFrom(ConvolutionEngine other)
    {
        if (other._block != _block)
            throw new InvalidOperationException("engines of different block sizes share no history");
        Reset();
        int keep = Math.Min(_partitions, other._partitions);
        for (int age = 0; age < keep; age++)
        {
            int from = (other._head - age + other._partitions) % other._partitions;
            int to = (_partitions - age) % _partitions;
            for (int channel = 0; channel < 2; channel++)
            {
                int source = (channel * other._partitions + from) * _stride;
                int target = (channel * _partitions + to) * _stride;
                Array.Copy(other._historyRe, source, _historyRe, target, _stride);
                Array.Copy(other._historyIm, source, _historyIm, target, _stride);
                bool live = other._live[channel * other._partitions + from];
                _live[channel * _partitions + to] = live;
                if (live)
                    _liveSlots++;
            }
        }
        _head = 0;
        Array.Copy(other._windowLeft, _windowLeft, _windowLeft.Length);
        Array.Copy(other._windowRight, _windowRight, _windowRight.Length);
        Array.Copy(other._queueLeft, _queueLeft, _queueLeft.Length);
        Array.Copy(other._queueRight, _queueRight, _queueRight.Length);
        _fill = other._fill;
        _queueRead = other._queueRead;
        _queueCount = other._queueCount;
        _buffered = other._buffered;
        _previousLeftLive = other._previousLeftLive;
        _previousRightLive = other._previousRightLive;
    }

    // Wet output for the input, one sample out for every sample in.
    public void Process(ReadOnlySpan<float> inLeft, ReadOnlySpan<float> inRight, Span<float> outLeft, Span<float> outRight)
    {
        int count = inLeft.Length;
        if (!_buffered && (count % _block != 0 || _fill != 0))
        {
            // From here on every sample waits for its block: a block of silence goes out ahead of them.
            _buffered = true;
            for (int i = 0; i < _block; i++)
                Enqueue(0f, 0f);
        }

        int offset = 0;
        while (offset < count)
        {
            int take = Math.Min(_block - _fill, count - offset);
            inLeft.Slice(offset, take).CopyTo(_windowLeft.AsSpan(_block + _fill));
            inRight.Slice(offset, take).CopyTo(_windowRight.AsSpan(_block + _fill));
            _fill += take;
            if (_fill == _block)
            {
                RunBlock();
                _fill = 0;
            }
            for (int i = 0; i < take; i++)
            {
                outLeft[offset + i] = _queueLeft[_queueRead];
                outRight[offset + i] = _queueRight[_queueRead];
                _queueRead = _queueRead + 1 == _queueLeft.Length ? 0 : _queueRead + 1;
            }
            _queueCount -= take;
            offset += take;
        }
    }

    private void RunBlock()
    {
        _head = _head + 1 == _partitions ? 0 : _head + 1;
        bool leftLive = Transform(_windowLeft, 0, ref _previousLeftLive);
        bool rightLive = Transform(_windowRight, 1, ref _previousRightLive);

        int write = (_queueRead + _queueCount) % _queueLeft.Length;
        if (_liveSlots == 0 && !leftLive && !rightLive)
        {
            for (int i = 0; i < _block; i++)
                Enqueue(0f, 0f);
            return;
        }
        Convolve(0, _queueLeft, write);
        Convolve(1, _queueRight, write);
        _queueCount += _block;
    }

    // Slides the window on by a block, transforms it into the delay line's newest slot and says whether that
    // slot holds anything.
    private bool Transform(float[] window, int channel, ref bool previousLive)
    {
        bool live = false;
        for (int i = _block; i < 2 * _block; i++)
        {
            float v = window[i];
            if (MathF.Abs(v) < QuietFloor)
                window[i] = 0f;
            else
                live = true;
        }
        int slot = channel * _partitions + _head;
        bool any = live || previousLive;
        previousLive = live;
        bool was = _live[slot];
        if (any)
        {
            _fft.Forward(window, _spectrumRe, _spectrumIm, _scratch);
            int at = slot * _stride;
            _spectrumRe.AsSpan(0, _block + 1).CopyTo(_historyRe.AsSpan(at));
            _spectrumIm.AsSpan(0, _block + 1).CopyTo(_historyIm.AsSpan(at));
        }
        _live[slot] = any;
        _liveSlots += (any ? 1 : 0) - (was ? 1 : 0);
        Array.Copy(window, _block, window, 0, _block);
        return any;
    }

    private void Convolve(int channel, float[] queue, int write)
    {
        Array.Clear(_sumRe);
        Array.Clear(_sumIm);
        int responseChannel = _response.Channels == 1 ? 0 : channel;
        float[] hr = _response.Real;
        float[] hi = _response.Imaginary;
        int responseBase = responseChannel * _partitions * _stride;
        int historyBase = channel * _partitions;
        bool any = false;
        for (int p = 0; p < _partitions; p++)
        {
            int slot = _head - p;
            if (slot < 0)
                slot += _partitions;
            if (!_live[historyBase + slot])
                continue;
            any = true;
            MultiplyAdd(hr, hi, responseBase + p * _stride, _historyRe, _historyIm, (historyBase + slot) * _stride, _sumRe, _sumIm, _stride);
        }
        int length = queue.Length;
        if (!any)
        {
            for (int i = 0; i < _block; i++)
                queue[(write + i) % length] = 0f;
            return;
        }
        _fft.Inverse(_sumRe, _sumIm, _time, _scratch, scale: false);
        for (int i = 0; i < _block; i++)
            queue[(write + i) % length] = _time[_block + i];
    }

    // sum += h * x over count bins, as complex numbers.
    private static void MultiplyAdd(float[] hr, float[] hi, int hAt, float[] xr, float[] xi, int xAt, float[] sr, float[] si, int count)
    {
        ref float h0 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(hr), hAt);
        ref float h1 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(hi), hAt);
        ref float x0 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(xr), xAt);
        ref float x1 = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(xi), xAt);
        ref float s0 = ref MemoryMarshal.GetArrayDataReference(sr);
        ref float s1 = ref MemoryMarshal.GetArrayDataReference(si);
        int i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            int lanes = Vector<float>.Count;
            for (; i + lanes <= count; i += lanes)
            {
                var a = ComplexFft.Load(ref h0, i);
                var b = ComplexFft.Load(ref h1, i);
                var c = ComplexFft.Load(ref x0, i);
                var d = ComplexFft.Load(ref x1, i);
                ComplexFft.Store(ref s0, i, ComplexFft.Load(ref s0, i) + (a * c - b * d));
                ComplexFft.Store(ref s1, i, ComplexFft.Load(ref s1, i) + (a * d + b * c));
            }
        }
        for (; i < count; i++)
        {
            float a = Unsafe.Add(ref h0, i);
            float b = Unsafe.Add(ref h1, i);
            float c = Unsafe.Add(ref x0, i);
            float d = Unsafe.Add(ref x1, i);
            Unsafe.Add(ref s0, i) += a * c - b * d;
            Unsafe.Add(ref s1, i) += a * d + b * c;
        }
    }

    private void Enqueue(float left, float right)
    {
        int at = (_queueRead + _queueCount) % _queueLeft.Length;
        _queueLeft[at] = left;
        _queueRight[at] = right;
        _queueCount++;
    }

    private bool QueueHasSound()
    {
        for (int i = 0; i < _queueCount; i++)
        {
            int at = (_queueRead + i) % _queueLeft.Length;
            if (_queueLeft[at] != 0f || _queueRight[at] != 0f)
                return true;
        }
        return false;
    }
}
