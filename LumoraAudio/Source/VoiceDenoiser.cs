// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Takes the noise out of a voice with RNNoise, in whatever lengths the platform hands audio over.
//
// The network wants whole 480-sample mono frames at 48 kHz in the 16-bit range. Stereo is folded to
// mono on the way in, because a voice comes out of one mouth and the stream that carries it is mono,
// and the cleaned mono goes back out on both channels. Cleaned audio waits in a queue until a call
// has room for it, so a call returns how much it wrote, which is not how much went in: output runs
// about a frame behind input (10 ms) on top of the network's own frame of delay. Any other rate passes
// straight through, since the model only knows 48 kHz. -xlinka
public sealed class VoiceDenoiser
{
    private const float ToPcm = 32768f;
    private const float FromPcm = 1f / 32768f;

    private readonly RnnoiseDenoiser _network = new();
    private readonly float[] _frameIn = new float[RnnoiseDenoiser.FrameSize];
    private readonly float[] _frameOut = new float[RnnoiseDenoiser.FrameSize];
    private int _filled;

    private float[] _ready = new float[RnnoiseDenoiser.FrameSize * 8];
    private int _readyCount;

    // The VAD of every frame waiting in _ready, oldest first, and how much of the oldest has already
    // gone out, so a sample handed out can be traced back to the frame it came from.
    private float[] _readyVoice = new float[8];
    private int _readyFrames;
    private int _headSent;

    private int[] _runLength = new int[4];
    private float[] _runVoice = new float[4];
    private int _runCount;

    // The network's own read of the last frame: 0 for noise, 1 for a voice.
    public float VoiceProbability { get; private set; }

    // What the last Process call handed out, as runs of samples that each came out of one frame, oldest
    // first, with that frame's VAD. A run is a whole frame unless a call boundary split it. None at a
    // rate the network does not run at.
    public int OutputRunCount => _runCount;

    public int OutputRunLength(int run) => _runLength[run];

    public float OutputRunVoiceProbability(int run) => _runVoice[run];

    public int Process(Span<StereoSample> buffer, int sampleRate)
    {
        _runCount = 0;
        if (sampleRate != RnnoiseDenoiser.SampleRate)
            return buffer.Length;

        for (int i = 0; i < buffer.Length; i++)
        {
            _frameIn[_filled++] = (buffer[i].left + buffer[i].right) * 0.5f * ToPcm;
            if (_filled < RnnoiseDenoiser.FrameSize)
                continue;
            _filled = 0;

            VoiceProbability = _network.ProcessFrame(_frameIn, _frameOut);
            if (_ready.Length < _readyCount + RnnoiseDenoiser.FrameSize)
                Array.Resize(ref _ready, _ready.Length * 2);
            for (int k = 0; k < RnnoiseDenoiser.FrameSize; k++)
                _ready[_readyCount++] = _frameOut[k] * FromPcm;
            if (_readyFrames == _readyVoice.Length)
                Array.Resize(ref _readyVoice, _readyVoice.Length * 2);
            _readyVoice[_readyFrames++] = VoiceProbability;
        }

        int written = System.Math.Min(buffer.Length, _readyCount);
        for (int i = 0; i < written; i++)
        {
            float sample = _ready[i];
            buffer[i] = new StereoSample(sample, sample);
        }
        if (written > 0)
        {
            _readyCount -= written;
            Array.Copy(_ready, written, _ready, 0, _readyCount);
            TakeRuns(written);
        }
        return written;
    }

    private void TakeRuns(int samples)
    {
        while (samples > 0 && _readyFrames > 0)
        {
            int take = System.Math.Min(RnnoiseDenoiser.FrameSize - _headSent, samples);
            if (_runCount == _runLength.Length)
            {
                Array.Resize(ref _runLength, _runCount * 2);
                Array.Resize(ref _runVoice, _runCount * 2);
            }
            _runLength[_runCount] = take;
            _runVoice[_runCount] = _readyVoice[0];
            _runCount++;

            samples -= take;
            _headSent += take;
            if (_headSent == RnnoiseDenoiser.FrameSize)
            {
                _headSent = 0;
                _readyFrames--;
                Array.Copy(_readyVoice, 1, _readyVoice, 0, _readyFrames);
            }
        }
    }

    public void Reset()
    {
        _network.Reset();
        _filled = 0;
        _readyCount = 0;
        _readyFrames = 0;
        _headSent = 0;
        _runCount = 0;
        VoiceProbability = 0f;
    }
}
