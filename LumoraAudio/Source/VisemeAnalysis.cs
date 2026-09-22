// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// Turns a voice into mouth shapes.
//
// A mouth is a tube whose shape decides which frequencies come out loudest. Those peaks are formants,
// and the first two of them are very nearly the whole story of which vowel somebody is making: "ee"
// puts the first low and the second high, "oo" puts both low, "ah" puts the first high and the two
// close together. Consonants do not have formants in the same way, but they have unmistakable shapes
// of their own - a hiss is energy above about four kilohertz and nowhere else, a plosive is a gap
// followed by a spike across everything at once.
//
// So: transform, find where the energy actually sits, and score each viseme by how well the spectrum
// fits its template. This is not the same machinery a dedicated speech library uses and it does not
// claim to be - what it is, is the difference between a face that moves when its owner talks and a
// face that does not, which is the entire point.
//
// Runs wherever it is fed. The design intent is that it runs ONCE, on the machine that owns the
// voice, and the fifteen numbers travel; every peer re-deriving them from the decoded audio would be
// the same work done N times to reach the same answer, and would desynchronise the moment one peer
// dropped a packet. -xlinka
public sealed class VisemeAnalysis
{
    public const int WindowSize = 1024;
    public const int Count = 15;

    // Index order matches the engine's Viseme enum: Silence, PP, FF, TH, DD, KK, CH, SS, NN, RR, AA,
    // E, IH, OH, OU.
    public const int Silence = 0;
    public const int PP = 1;
    public const int FF = 2;
    public const int TH = 3;
    public const int DD = 4;
    public const int KK = 5;
    public const int CH = 6;
    public const int SS = 7;
    public const int NN = 8;
    public const int RR = 9;
    public const int AA = 10;
    public const int E = 11;
    public const int IH = 12;
    public const int OH = 13;
    public const int OU = 14;

    // Below this the mouth is shut. Set against the gate's own threshold so a frame the voice stream
    // will not send is not a frame the mouth moves for.
    private const float SilenceFloor = 0.006f;

    // How fast a shape may change. A mouth has mass; stepping straight to a new vowel every frame
    // reads as a chattering puppet, and this is the single biggest difference between lip sync that
    // looks alive and lip sync that looks wrong.
    private const float AttackSeconds = 0.045f;
    private const float ReleaseSeconds = 0.11f;

    private readonly FourierTransform _fft = new(WindowSize);
    private readonly float[] _real = new float[WindowSize];
    private readonly float[] _imag = new float[WindowSize];
    private readonly float[] _ring = new float[WindowSize];
    private readonly float[] _weights = new float[Count];
    private readonly float[] _target = new float[Count];
    private int _filled;
    private int _sampleRate = 48000;

    public VisemeAnalysis()
    {
        _weights[Silence] = 1f;
        _target[Silence] = 1f;
    }

    public float this[int viseme] => viseme >= 0 && viseme < Count ? _weights[viseme] : 0f;

    public void CopyTo(Span<float> destination)
    {
        int n = System.Math.Min(destination.Length, Count);
        for (int i = 0; i < n; i++)
            destination[i] = _weights[i];
    }

    public void Reset()
    {
        Array.Clear(_ring);
        _filled = 0;
        for (int i = 0; i < Count; i++)
            _weights[i] = _target[i] = i == Silence ? 1f : 0f;
    }

    // Push captured audio in. Mono or stereo; stereo is mixed down, because a mouth is one mouth.
    public void Write(ReadOnlySpan<StereoSample> samples, int sampleRate)
    {
        if (sampleRate > 0)
            _sampleRate = sampleRate;

        for (int i = 0; i < samples.Length; i++)
        {
            _ring[_filled++] = (samples[i].left + samples[i].right) * 0.5f;
            if (_filled < WindowSize)
                continue;

            AnalyzeWindow();

            // Half-window hop: often enough that a short consonant is not missed, cheap enough that
            // the transform is a rounding error next to the codec on the same thread.
            Array.Copy(_ring, WindowSize / 2, _ring, 0, WindowSize / 2);
            _filled = WindowSize / 2;
        }
    }

    // Ease the live weights toward the last analysis. Called once a frame by the owner, with that
    // frame's delta, so the smoothing is in seconds rather than in frames.
    public void Advance(float deltaSeconds)
    {
        for (int i = 0; i < Count; i++)
        {
            float target = _target[i];
            float seconds = target > _weights[i] ? AttackSeconds : ReleaseSeconds;
            float blend = seconds <= 0f ? 1f : deltaSeconds / seconds;
            if (blend > 1f) blend = 1f;
            _weights[i] += (target - _weights[i]) * blend;
        }
    }

    // Accept weights computed elsewhere (a remote peer's, off the stream) and ease toward them the
    // same way, so a remote mouth moves with the same weight and the same inertia as a local one and
    // a dropped packet reads as a held shape rather than a snap.
    public void Accept(ReadOnlySpan<float> incoming)
    {
        int n = System.Math.Min(incoming.Length, Count);
        for (int i = 0; i < n; i++)
            _target[i] = System.Math.Clamp(incoming[i], 0f, 1f);
    }

    private void AnalyzeWindow()
    {
        float energy = 0f;
        for (int i = 0; i < WindowSize; i++)
        {
            float w = _fft.WindowAt(i);
            float v = _ring[i] * w * w;   // squared: this path windows once, see FourierTransform
            _real[i] = v;
            _imag[i] = 0f;
            energy += MathF.Abs(_ring[i]);
        }
        energy /= WindowSize;

        if (energy < SilenceFloor)
        {
            for (int i = 0; i < Count; i++)
                _target[i] = i == Silence ? 1f : 0f;
            return;
        }

        _fft.Forward(_real, _imag);

        // Band energies. The edges are chosen where speech actually divides: the first formant lives
        // under about 900 Hz, the second between there and 2.5 kHz, fricatives above 4 kHz.
        float low = Band(180f, 500f);         // first formant, closed vowels
        float lowMid = Band(500f, 900f);      // first formant, open vowels
        float mid = Band(900f, 1600f);        // second formant, back vowels
        float highMid = Band(1600f, 2200f);   // second formant, mid-front vowels (ih, eh)
        // "ee" and "ih" are two different mouths and both put their second formant up here, a few
        // hundred hertz apart: about 2300 for ee against about 1990 for ih. One band from 1600 to
        // 2600 cannot tell them apart, and the analysis called every ee an ih because of it. Splitting
        // the front in two is the whole fix; the line between them is the line between the two
        // shapes. -xlinka
        float frontHigh = Band(2200f, 3200f); // second formant, closed front vowels (ee)
        float presence = Band(3200f, 4000f);  // consonant body
        float hiss = Band(4000f, 8000f);      // sibilants
        float total = low + lowMid + mid + highMid + frontHigh + presence + hiss;
        if (total < 1e-6f)
        {
            for (int i = 0; i < Count; i++)
                _target[i] = i == Silence ? 1f : 0f;
            return;
        }

        float inverse = 1f / total;
        low *= inverse; lowMid *= inverse; mid *= inverse;
        highMid *= inverse; frontHigh *= inverse; presence *= inverse; hiss *= inverse;

        Array.Clear(_target);

        // SIBILANTS AND FRICATIVES. Nothing else in a voice puts this much of its energy that high,
        // so a strong hiss band is decided before any vowel is considered.
        if (hiss > 0.34f)
        {
            // A sharp, narrow hiss is an s; a broader one with body under it is sh or ch.
            if (presence > hiss * 0.55f)
                _target[CH] = 1f;
            else
                _target[SS] = 1f;
        }
        else if (hiss > 0.18f)
        {
            // Softer and breathier: f, v, th.
            _target[presence > 0.22f ? TH : FF] = 1f;
        }
        else
        {
            // VOWELS, by where the two formants sit relative to each other.
            //
            //   "ee" front and closed: second formant high, first low
            //   "ih" front, more open
            //   "eh" middle
            //   "ah" open: first formant high, the two close together
            //   "oh" back and rounded: both lower
            //   "oo" back and closed: everything low
            float front = highMid + frontHigh + presence * 0.5f;
            float back = low + lowMid * 0.5f;
            float openness = lowMid + mid * 0.5f;

            // Which half of the front the second formant sits in decides ee against ih, and it is a
            // comparison between the two bands rather than a level: a quiet ee is still an ee.
            if (frontHigh > 0.16f && frontHigh >= highMid * 0.9f)
                _target[E] = 1f;                       // ee
            else if (front > 0.24f)
                _target[IH] = 1f;                      // ih / eh
            else if (openness > 0.33f)
                _target[AA] = 1f;                      // ah
            else if (back > 0.55f)
                _target[OU] = 1f;                      // oo
            else
                _target[OH] = 1f;                      // oh

            // Voiced consonants ride on top of a vowel rather than replacing it: a closed mouth with
            // energy only at the bottom is m/b/p, a little higher is n, and a mid-band hump with no
            // hiss at all is the r colouring.
            if (back > 0.72f && hiss < 0.06f)
                _target[PP] = MathF.Min(1f, (back - 0.72f) * 3.5f);
            else if (mid > 0.30f && hiss < 0.10f)
                _target[NN] = MathF.Min(1f, (mid - 0.30f) * 2.5f);

            if (mid > 0.24f && highMid > 0.20f && hiss < 0.08f)
                _target[RR] = MathF.Min(0.6f, (mid + highMid - 0.44f) * 2f);

            // Plosive burst: presence spikes while the bottom stays thin.
            if (presence > 0.30f && back < 0.28f)
                _target[KK] = MathF.Min(1f, (presence - 0.30f) * 3f);
            if (lowMid > 0.34f && presence > 0.16f)
                _target[DD] = MathF.Min(0.8f, (lowMid - 0.34f) * 3f);
        }

        // How wide the mouth opens overall follows loudness, so a whisper moves the face less than a
        // shout does. Everything is scaled by it, and what is left over is silence.
        float loudness = MathF.Min(1f, energy / 0.10f);
        float sum = 0f;
        for (int i = 1; i < Count; i++)
        {
            _target[i] *= loudness;
            sum += _target[i];
        }
        _target[Silence] = MathF.Max(0f, 1f - sum);
    }

    private float Band(float fromHz, float toHz)
    {
        int first = _fft.FrequencyBin(fromHz, _sampleRate);
        int last = _fft.FrequencyBin(toHz, _sampleRate);
        if (last <= first)
            return 0f;

        float sum = 0f;
        for (int b = first; b < last; b++)
            sum += MathF.Sqrt(_real[b] * _real[b] + _imag[b] * _imag[b]);
        return sum;
    }
}
