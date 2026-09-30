// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Audio;

// A filter shape followed without a click: up to a handful of sections, a corner that glides in octaves, a Q
// that glides in octaves of Q and a gain that glides in decibels, all at control rate, with every section
// ramping straight to its new design across the block.
//
// A change of shape (the type, or how many sections a slope takes) cannot glide, so it crossfades: for a short
// while every section runs a blend of its design under the old shape and under the new one, and because the
// designs share their integrator states, a low-pass turning into a high-pass is the same two states read out
// through a mix that moves. A section switching on starts from identity, a section switching off ends there.
// One crossfade runs at a time; a shape asked for during one is taken up as soon as it is done. -xlinka
internal sealed class SvfChain
{
    private const double OctaveTolerance = 1e-5;
    private const double GainTolerance = 1e-4;
    private const double NyquistFraction = 0.49;

    private readonly SvfStage[] _stages;

    private double _octave;
    private double _octaveTarget;
    private double _qOctave;
    private double _qOctaveTarget;
    private double _gain;
    private double _gainTarget;

    private BiquadFilterType _type;
    private int _count;
    private BiquadFilterType _nextType;
    private int _nextCount;
    private BiquadFilterType _fromType;
    private int _fromCount;
    private float _morph = 1f;
    private bool _settled;

    public SvfChain(int stages)
    {
        _stages = new SvfStage[Math.Max(1, stages)];
        for (int i = 0; i < _stages.Length; i++)
            _stages[i].Snap(SvfDesign.Identity);
        _octaveTarget = _octave = Math.Log2(1000.0);
        _settled = true;
    }

    public bool IsIdle
    {
        get
        {
            if (!_settled)
                return false;
            for (int i = 0; i < _stages.Length; i++)
            {
                if (!_stages[i].IsIdle)
                    return false;
            }
            return true;
        }
    }

    public void SetTarget(BiquadFilterType type, int count, double frequency, double q, double gainDb)
    {
        _nextType = type;
        _nextCount = Math.Clamp(count, 0, _stages.Length);
        _octaveTarget = Math.Log2(frequency);
        _qOctaveTarget = Math.Log2(q);
        _gainTarget = gainDb;
        _settled = false;
    }

    // Switches every section off (0) or back on without touching the shape it fades out of or into.
    public void SetCount(int count)
    {
        _nextCount = Math.Clamp(count, 0, _stages.Length);
        _settled = false;
    }

    // Straight to the targets from silence.
    public void Snap(double sampleRate)
    {
        _octave = _octaveTarget;
        _qOctave = _qOctaveTarget;
        _gain = _gainTarget;
        _type = _nextType;
        _count = _nextCount;
        _morph = 1f;
        double frequency = Frequency(sampleRate);
        double q = Math.Pow(2.0, _qOctave);
        for (int i = 0; i < _stages.Length; i++)
        {
            _stages[i].Clear();
            _stages[i].Snap(Design(_type, _count, i, frequency, q, _gain, sampleRate));
        }
        _settled = true;
    }

    public void Clear()
    {
        for (int i = 0; i < _stages.Length; i++)
            _stages[i].Clear();
    }

    public void Control(double sampleRate, double glide, float morphStep, int samples)
    {
        if (_settled)
            return;

        bool moving = DspMath.Glide(ref _octave, _octaveTarget, glide, OctaveTolerance);
        moving |= DspMath.Glide(ref _qOctave, _qOctaveTarget, glide, OctaveTolerance);
        moving |= DspMath.Glide(ref _gain, _gainTarget, glide, GainTolerance);

        if (_morph >= 1f && (_nextType != _type || _nextCount != _count))
        {
            if (_count == 0 && _nextCount == 0)
            {
                _type = _nextType;
            }
            else
            {
                _fromType = _type;
                _fromCount = _count;
                _type = _nextType;
                _count = _nextCount;
                _morph = 0f;
            }
        }
        if (_morph < 1f)
        {
            _morph = MathF.Min(1f, _morph + morphStep);
            moving = true;
        }

        double frequency = Frequency(sampleRate);
        double q = Math.Pow(2.0, _qOctave);
        for (int i = 0; i < _stages.Length; i++)
        {
            SvfDesign target = Design(_type, _count, i, frequency, q, _gain, sampleRate);
            if (_morph < 1f)
                target = SvfDesign.Lerp(Design(_fromType, _fromCount, i, frequency, q, _gain, sampleRate), target, _morph);
            _stages[i].RampTo(target, samples);
        }
        _settled = !moving;
    }

    public void Run(Span<StereoSample> buffer)
    {
        for (int i = 0; i < _stages.Length; i++)
        {
            if (!_stages[i].IsIdle)
                _stages[i].Run(buffer);
        }
    }

    private double Frequency(double sampleRate)
        => Math.Clamp(Math.Pow(2.0, _octave), 1.0, NyquistFraction * sampleRate);

    private static SvfDesign Design(BiquadFilterType type, int count, int index, double frequency, double q, double gainDb, double sampleRate)
    {
        if (index >= count)
            return SvfDesign.Identity;
        if (type == BiquadFilterType.LowPass || type == BiquadFilterType.HighPass)
        {
            // Butterworth sections in rising Q, with the asked-for Q folded into the sharpest one: at a Q of
            // 1/sqrt(2) every slope is a true Butterworth, 3 dB down at the corner.
            double sectionQ = 1.0 / (2.0 * Math.Cos((2 * index + 1) * Math.PI / (4.0 * count)));
            if (index == count - 1)
                sectionQ *= q * Math.Sqrt(2.0);
            return SvfDesign.Make(type, frequency, sectionQ, gainDb, sampleRate);
        }
        return SvfDesign.Make(type, frequency, q, gainDb, sampleRate);
    }
}
