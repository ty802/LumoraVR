// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;

namespace Lumora.Audio;

// A stereo room reverb: a feedback delay network of sixteen lines.
//
// Each channel goes through a pre-delay and four allpass diffusers and is then spread across every line
// along its own row of a 16-point Hadamard matrix. Each line's output passes two loss filters worked out
// from that line's own length, so all sixteen lose the same number of decibels per second whatever
// their length:
//   a second-order low shelf gives the low decay time below the crossover and the mid one above it,
//   with the shelf's halfway point on the crossover and a slope that never overshoots either;
//   a one-pole high shelf takes one more mid-sized loss at the damping frequency, which is exactly
//   what halves the decay time there, and levels off at two of them by Nyquist.
// The filtered outputs are mixed by the same Hadamard matrix scaled to be orthogonal and written back.
// Neither filter ever passes more than the decay asks for and the mix neither adds nor removes energy,
// so the loop is stable for every setting inside the limits, moving or resting. The return is read off
// two more rows of that mix: orthogonal rows over sixteen lines of coprime lengths come back
// uncorrelated, and that is where the width comes from.
//
// Every setting glides. Decay times move as decay rates (1/RT60), so the loss per pass changes by a
// fraction of a decibel at a time instead of jumping; frequencies move in octaves, levels in decibels,
// and the dry and wet gains are smoothed per sample. A pre-delay change crossfades from the old tap to
// the new one instead of sliding the read head, so it neither clicks nor bends pitch.
//
// Settings reach the audio thread as an immutable box swapped by reference: the audio thread never
// waits and never sees half a change. Process allocates nothing unless the sample rate it is handed
// changes. A constant far below hearing rides the input and every line write, so no recursive state can
// decay into the denormal range, and a quiet reverb whose tail has died stops computing. -xlinka
public sealed class ReverbFilter : IAudioDspFilter, IAudioTailFilter
{
    private const int LineCount = 16;
    private const int DiffuserStages = 4;
    private const int ControlBlock = 32;

    // Line lengths are spread evenly in log between these, each moved up to the next prime not taken,
    // so no two lines share a factor and their echoes never pile onto the same instants.
    private const double ShortestLineSeconds = 0.0167;
    private const double LongestLineSeconds = 0.0667;

    // Allpass lengths at 48 kHz, different per channel so the two diffused inputs never match.
    private static readonly int[] DiffuserLengthsLeft = { 139, 211, 293, 401 };
    private static readonly int[] DiffuserLengthsRight = { 149, 199, 317, 389 };
    private const float DiffuserGain = 0.6f;

    private const int LeftInputRow = 1;
    private const int RightInputRow = 2;
    private const int LeftOutputRow = 5;
    private const int RightOutputRow = 10;
    private const float InputGain = 0.25f;
    private const float MixGain = 0.25f;

    // Stereo noise in, reverb return out at the same RMS for a one second room.
    private const float OutputGain = 0.65f;

    // ln(1000): 60 dB in nepers.
    private const double DecayPerRate = 6.907755278982137;
    private const double ParameterSeconds = 0.05;
    private const double GainSeconds = 0.015;
    private const double PreDelayFadeSeconds = 0.02;
    private const double NyquistFraction = 0.45;
    private const double BellQ = 0.7;
    private const float AntiDenormal = 1e-18f;
    private const float SilenceFloor = 1e-6f;
    private const float TailFloor = 1e-5f;
    private const float FlushFloor = 1e-20f;
    private const float GainSnap = 1e-6f;
    private const int QuietForever = int.MaxValue / 2;

    private static readonly float[] InputSignLeft = RowSigns(LeftInputRow);
    private static readonly float[] InputSignRight = RowSigns(RightInputRow);

    private sealed class SettingsBox
    {
        public readonly ReverbSettings Value;

        public SettingsBox(in ReverbSettings value) => Value = value;
    }

    // A peaking band as a trapezoidal state-variable filter, which stays well behaved while its
    // coefficients move.
    private struct Bell
    {
        public float A1;
        public float A2;
        public float A3;
        public float M1;

        public void Set(double frequency, double levelDb, double sampleRate)
        {
            double a = Math.Pow(10.0, levelDb / 40.0);
            double g = Math.Tan(Math.PI * frequency / sampleRate);
            double k = 1.0 / (BellQ * a);
            double a1 = 1.0 / (1.0 + g * (g + k));
            A1 = (float)a1;
            A2 = (float)(g * a1);
            A3 = (float)(g * g * a1);
            M1 = (float)(k * (a * a - 1.0));
        }

        public readonly float Run(float input, ref float s1, ref float s2)
        {
            float v3 = input - s2;
            float v1 = A1 * s1 + A2 * v3;
            float v2 = s2 + A2 * s1 + A3 * v3;
            s1 = 2f * v1 - s1;
            s2 = 2f * v2 - s2;
            return input + M1 * v1;
        }
    }

    private SettingsBox _staged;
    private SettingsBox _applied;
    private volatile bool _enabled = true;
    private volatile bool _resetRequested;

    private int _sampleRate;

    private float[] _lines = Array.Empty<float>();
    private readonly int[] _lineOffset = new int[LineCount];
    private readonly int[] _lineLength = new int[LineCount];
    private readonly int[] _linePosition = new int[LineCount];
    private readonly float[] _lowB0 = new float[LineCount];
    private readonly float[] _lowB1 = new float[LineCount];
    private readonly float[] _lowB2 = new float[LineCount];
    private readonly float[] _lowA1 = new float[LineCount];
    private readonly float[] _lowA2 = new float[LineCount];
    private readonly float[] _lowZ1 = new float[LineCount];
    private readonly float[] _lowZ2 = new float[LineCount];
    private readonly float[] _dampB0 = new float[LineCount];
    private readonly float[] _dampB1 = new float[LineCount];
    private readonly float[] _dampA1 = new float[LineCount];
    private readonly float[] _dampZ = new float[LineCount];
    private readonly float[] _mixed = new float[LineCount];
    private int _longestLine;

    // Left stages, then right ones, packed end to end like the lines.
    private float[] _diffusers = Array.Empty<float>();
    private readonly int[] _diffuserOffset = new int[DiffuserStages * 2];
    private readonly int[] _diffuserLength = new int[DiffuserStages * 2];
    private readonly int[] _diffuserPosition = new int[DiffuserStages * 2];
    private int _diffuserSpan;

    private float[] _preLeft = Array.Empty<float>();
    private float[] _preRight = Array.Empty<float>();
    private int _preWrite;
    private int _tap;
    private int _nextTap;
    private int _targetTap;
    private float _fade;
    private float _fadeIncrement;
    private bool _fading;

    private Bell _bell1;
    private Bell _bell2;
    private readonly float[] _bellState = new float[8];

    private double _rateLow;
    private double _rateLowTarget;
    private double _rateMid;
    private double _rateMidTarget;
    private double _crossoverOctave;
    private double _crossoverOctaveTarget;
    private double _dampingOctave;
    private double _dampingOctaveTarget;
    private double _eq1Octave;
    private double _eq1OctaveTarget;
    private double _eq1Level;
    private double _eq1LevelTarget;
    private double _eq2Octave;
    private double _eq2OctaveTarget;
    private double _eq2Level;
    private double _eq2LevelTarget;
    private float _dry = 1f;
    private float _dryTarget = 1f;
    private float _wet;
    private float _wetTarget;
    private double _parameterCoefficient;
    private float _gainCoefficient;
    private bool _parametersSettled;
    private int _controlCountdown;
    private bool _primed;
    private bool _networkStale;

    private int _quietSamples = QuietForever;
    private float _wetPeak;

    public ReverbFilter()
        : this(AudioSimulator.DefaultSampleRate, ReverbSettings.Default)
    {
    }

    public ReverbFilter(int sampleRate, in ReverbSettings settings)
    {
        _staged = new SettingsBox(settings.Clamped());
        _applied = _staged;
        Configure(sampleRate > 0 ? sampleRate : AudioSimulator.DefaultSampleRate);
    }

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    // Written from any thread, picked up at the top of the next Process. A new value glides from wherever
    // the filter is; one set before the filter has heard anything is simply where it starts.
    public ReverbSettings Settings
    {
        get => Volatile.Read(ref _staged).Value;
        set
        {
            var clamped = value.Clamped();
            if (Volatile.Read(ref _staged).Value.Equals(clamped))
                return;
            Volatile.Write(ref _staged, new SettingsBox(clamped));
        }
    }

    public int SampleRate => _sampleRate;

    // Audio thread only: whether the return still carries sound from input that has stopped.
    public bool HasTail => (_wet > 0f || _wetTarget > 0f) && Ringing;

    // Clears every line and snaps every glide to its target. Taken up by the audio thread at the top of
    // the next Process, so it is safe to call from anywhere.
    public void Reset() => _resetRequested = true;

    public void Process(Span<StereoSample> buffer, int sampleRate)
    {
        if (buffer.IsEmpty)
            return;
        if (sampleRate > 0 && sampleRate != _sampleRate)
            Configure(sampleRate);
        if (_resetRequested)
        {
            _resetRequested = false;
            ClearState();
        }
        PullSettings();

        // A NaN let into the lines would ring forever, so the input is cleaned before anything reads it.
        float peak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            var sample = buffer[i];
            if (!float.IsFinite(sample.left) || !float.IsFinite(sample.right))
            {
                sample = new StereoSample(float.IsFinite(sample.left) ? sample.left : 0f, float.IsFinite(sample.right) ? sample.right : 0f);
                buffer[i] = sample;
            }
            float amplitude = sample.AbsoluteAmplitude;
            if (amplitude > peak)
                peak = amplitude;
        }

        bool quiet = !(peak > SilenceFloor);
        _quietSamples = quiet ? Math.Min(QuietForever, _quietSamples + buffer.Length) : 0;
        _primed = true;

        if (_wetTarget == 0f && _wet == 0f && Settled)
        {
            // The return is off: nothing to compute, and what the lines held is stale by the time it is back.
            _networkStale = true;
            _wetPeak = 0f;
            ApplyDry(buffer);
            return;
        }
        if (_networkStale)
        {
            _networkStale = false;
            ClearNetwork();
        }
        if (quiet && Settled && !Ringing)
        {
            _wetPeak = 0f;
            ApplyDry(buffer);
            return;
        }

        float wetPeak = 0f;
        int offset = 0;
        while (offset < buffer.Length)
        {
            if (_controlCountdown <= 0)
            {
                ControlUpdate();
                _controlCountdown = ControlBlock;
            }
            int run = Math.Min(_controlCountdown, buffer.Length - offset);
            float runPeak = Render(buffer.Slice(offset, run));
            if (runPeak > wetPeak)
                wetPeak = runPeak;
            _controlCountdown -= run;
            offset += run;
        }
        _wetPeak = wetPeak;
        FlushSmallStates();
    }

    private bool Settled => _parametersSettled && !_fading && _tap == _targetTap && _dry == _dryTarget && _wet == _wetTarget;

    private bool Ringing => _quietSamples < TailHoldSamples || _wetPeak > TailFloor;

    // Long enough after the input stops for the last of it to have come out of the pre-delay, the
    // diffusers and the longest line, which is when the return's own level can be trusted to say.
    private int TailHoldSamples => Math.Max(_tap, Math.Max(_nextTap, _targetTap)) + _longestLine + _diffuserSpan + ControlBlock;

    private void PullSettings()
    {
        var box = Volatile.Read(ref _staged);
        if (ReferenceEquals(box, _applied))
            return;
        _applied = box;
        Retarget(box.Value);
        if (!_primed)
            Snap();
    }

    private void Retarget(in ReverbSettings settings)
    {
        double sampleRate = _sampleRate;
        double ceiling = NyquistFraction * sampleRate;
        _rateLowTarget = 1.0 / settings.LowRT60;
        _rateMidTarget = 1.0 / settings.MidRT60;
        _crossoverOctaveTarget = Math.Log2(Math.Min(settings.Crossover, ceiling));
        _dampingOctaveTarget = Math.Log2(Math.Min(settings.HFDamping, ceiling));
        _eq1OctaveTarget = Math.Log2(Math.Min(settings.EQ1Frequency, ceiling));
        _eq1LevelTarget = settings.EQ1Level;
        _eq2OctaveTarget = Math.Log2(Math.Min(settings.EQ2Frequency, ceiling));
        _eq2LevelTarget = settings.EQ2Level;
        _targetTap = Math.Clamp((int)Math.Round(settings.PreDelay * 0.001 * sampleRate), 0, _preLeft.Length - 1);

        float level = MathF.Pow(10f, settings.Level / 20f);
        _dryTarget = MathF.Min(1f, 2f * (1f - settings.Mix));
        _wetTarget = MathF.Min(1f, 2f * settings.Mix) * level;
        _parametersSettled = false;
    }

    private void Snap()
    {
        _rateLow = _rateLowTarget;
        _rateMid = _rateMidTarget;
        _crossoverOctave = _crossoverOctaveTarget;
        _dampingOctave = _dampingOctaveTarget;
        _eq1Octave = _eq1OctaveTarget;
        _eq1Level = _eq1LevelTarget;
        _eq2Octave = _eq2OctaveTarget;
        _eq2Level = _eq2LevelTarget;
        _tap = _targetTap;
        _nextTap = _targetTap;
        _fading = false;
        _fade = 0f;
        _dry = _dryTarget;
        _wet = _wetTarget;
        UpdateCoefficients();
        _parametersSettled = true;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;

        int total = 0;
        int previous = 1;
        for (int i = 0; i < LineCount; i++)
        {
            double seconds = ShortestLineSeconds * Math.Pow(LongestLineSeconds / ShortestLineSeconds, i / (double)(LineCount - 1));
            int length = NextPrime(Math.Max(previous + 1, (int)Math.Round(seconds * sampleRate)));
            _lineLength[i] = length;
            _lineOffset[i] = total;
            total += length;
            previous = length;
        }
        _longestLine = previous;
        _lines = new float[total];

        total = 0;
        int spanLeft = 0;
        int spanRight = 0;
        for (int s = 0; s < DiffuserStages * 2; s++)
        {
            bool left = s < DiffuserStages;
            int baseLength = left ? DiffuserLengthsLeft[s] : DiffuserLengthsRight[s - DiffuserStages];
            int length = Math.Max(1, (int)Math.Round(baseLength * (sampleRate / 48000.0)));
            _diffuserLength[s] = length;
            _diffuserOffset[s] = total;
            total += length;
            if (left)
                spanLeft += length;
            else
                spanRight += length;
        }
        _diffusers = new float[total];
        _diffuserSpan = Math.Max(spanLeft, spanRight);

        int preSize = (int)Math.Ceiling(ReverbSettings.MaxPreDelay * 0.001 * sampleRate) + 1;
        _preLeft = new float[preSize];
        _preRight = new float[preSize];

        _parameterCoefficient = 1.0 - Math.Exp(-ControlBlock / (ParameterSeconds * sampleRate));
        _gainCoefficient = (float)(1.0 - Math.Exp(-1.0 / (GainSeconds * sampleRate)));
        _fadeIncrement = (float)(1.0 / (PreDelayFadeSeconds * sampleRate));

        ClearState();
    }

    private void ClearState()
    {
        ClearNetwork();
        _networkStale = false;
        _quietSamples = QuietForever;
        _wetPeak = 0f;
        _controlCountdown = 0;
        Retarget(_applied.Value);
        Snap();
    }

    // The pre-delay goes too: after the return has been off, what it holds is from before, and would
    // come out as a late echo of it.
    private void ClearNetwork()
    {
        Array.Clear(_preLeft);
        Array.Clear(_preRight);
        _preWrite = 0;
        Array.Clear(_lines);
        Array.Clear(_linePosition);
        Array.Clear(_lowZ1);
        Array.Clear(_lowZ2);
        Array.Clear(_dampZ);
        Array.Clear(_diffusers);
        Array.Clear(_diffuserPosition);
        Array.Clear(_bellState);
    }

    private void ControlUpdate()
    {
        if (!_fading && _tap != _targetTap)
        {
            _nextTap = _targetTap;
            _fade = 0f;
            _fading = true;
        }
        if (_parametersSettled)
            return;

        double k = _parameterCoefficient;
        bool moving = Glide(ref _rateLow, _rateLowTarget, k, 1e-6);
        moving |= Glide(ref _rateMid, _rateMidTarget, k, 1e-6);
        moving |= Glide(ref _crossoverOctave, _crossoverOctaveTarget, k, 1e-5);
        moving |= Glide(ref _dampingOctave, _dampingOctaveTarget, k, 1e-5);
        moving |= Glide(ref _eq1Octave, _eq1OctaveTarget, k, 1e-5);
        moving |= Glide(ref _eq1Level, _eq1LevelTarget, k, 1e-4);
        moving |= Glide(ref _eq2Octave, _eq2OctaveTarget, k, 1e-5);
        moving |= Glide(ref _eq2Level, _eq2LevelTarget, k, 1e-4);
        UpdateCoefficients();
        _parametersSettled = !moving;
    }

    private static bool Glide(ref double value, double target, double coefficient, double tolerance)
    {
        double next = value + (target - value) * coefficient;
        if (next == value || Math.Abs(target - next) <= tolerance)
        {
            value = target;
            return false;
        }
        value = next;
        return true;
    }

    // A one-pole glide that lands exactly: once a move would not change the value, it is close enough to take
    // the target itself, which is what makes a mix glided to 0 bit-exact dry.
    private static float Approach(float value, float target, float coefficient)
    {
        float next = value + (target - value) * coefficient;
        return next == value || MathF.Abs(target - next) < GainSnap ? target : next;
    }

    private void UpdateCoefficients()
    {
        double sampleRate = _sampleRate;
        double w0 = 2.0 * Math.PI * Math.Pow(2.0, _crossoverOctave) / sampleRate;
        double cosW0 = Math.Cos(w0);
        double alpha = Math.Sin(w0) / Math.Sqrt(2.0);
        double warp = Math.Tan(Math.PI * Math.Pow(2.0, _dampingOctave) / sampleRate);

        for (int i = 0; i < LineCount; i++)
        {
            double seconds = _lineLength[i] / sampleRate;
            double mid = Math.Exp(-DecayPerRate * seconds * _rateMid);
            double low = Math.Exp(-DecayPerRate * seconds * _rateLow);

            // Low shelf of slope 1 with low/mid below the crossover and 1 above it, scaled by mid.
            double a = Math.Sqrt(low / mid);
            double root = 2.0 * Math.Sqrt(a) * alpha;
            double a0 = (a + 1.0) + (a - 1.0) * cosW0 + root;
            double scale = mid / a0;
            _lowB0[i] = (float)(a * ((a + 1.0) - (a - 1.0) * cosW0 + root) * scale);
            _lowB1[i] = (float)(2.0 * a * ((a - 1.0) - (a + 1.0) * cosW0) * scale);
            _lowB2[i] = (float)(a * ((a + 1.0) - (a - 1.0) * cosW0 - root) * scale);
            _lowA1[i] = (float)(-2.0 * ((a - 1.0) + (a + 1.0) * cosW0) / a0);
            _lowA2[i] = (float)(((a + 1.0) + (a - 1.0) * cosW0 - root) / a0);

            // High shelf: 1 at DC, mid at the damping frequency, mid squared at Nyquist.
            double over = mid / warp;
            double under = 1.0 / (mid * warp);
            double norm = 1.0 / (1.0 + under);
            _dampB0[i] = (float)((1.0 + over) * norm);
            _dampB1[i] = (float)((1.0 - over) * norm);
            _dampA1[i] = (float)((1.0 - under) * norm);
        }

        _bell1.Set(Math.Pow(2.0, _eq1Octave), _eq1Level, sampleRate);
        _bell2.Set(Math.Pow(2.0, _eq2Octave), _eq2Level, sampleRate);
    }

    // One control block's worth of samples, in place. Returns the return's peak before its gain.
    private float Render(Span<StereoSample> buffer)
    {
        float[] lines = _lines;
        int[] lineOffset = _lineOffset;
        int[] lineLength = _lineLength;
        int[] linePosition = _linePosition;
        float[] lowB0 = _lowB0;
        float[] lowB1 = _lowB1;
        float[] lowB2 = _lowB2;
        float[] lowA1 = _lowA1;
        float[] lowA2 = _lowA2;
        float[] lowZ1 = _lowZ1;
        float[] lowZ2 = _lowZ2;
        float[] dampB0 = _dampB0;
        float[] dampB1 = _dampB1;
        float[] dampA1 = _dampA1;
        float[] dampZ = _dampZ;
        float[] mixed = _mixed;
        float[] signLeft = InputSignLeft;
        float[] signRight = InputSignRight;
        float[] diffusers = _diffusers;
        int[] diffuserOffset = _diffuserOffset;
        int[] diffuserLength = _diffuserLength;
        int[] diffuserPosition = _diffuserPosition;
        float[] preLeft = _preLeft;
        float[] preRight = _preRight;
        float[] bellState = _bellState;
        Bell bell1 = _bell1;
        Bell bell2 = _bell2;
        int preSize = preLeft.Length;
        float peak = 0f;

        for (int n = 0; n < buffer.Length; n++)
        {
            StereoSample x = buffer[n];

            int write = _preWrite;
            preLeft[write] = x.left + AntiDenormal;
            preRight[write] = x.right + AntiDenormal;
            int read = write - _tap;
            if (read < 0)
                read += preSize;
            float left = preLeft[read];
            float right = preRight[read];
            if (_fading)
            {
                int other = write - _nextTap;
                if (other < 0)
                    other += preSize;
                float t = _fade;
                left += (preLeft[other] - left) * t;
                right += (preRight[other] - right) * t;
                t += _fadeIncrement;
                if (t >= 1f)
                {
                    _tap = _nextTap;
                    _fading = false;
                    t = 0f;
                }
                _fade = t;
            }
            if (++write == preSize)
                write = 0;
            _preWrite = write;

            for (int s = 0; s < DiffuserStages; s++)
            {
                left = Allpass(diffusers, diffuserOffset, diffuserLength, diffuserPosition, s, left);
                right = Allpass(diffusers, diffuserOffset, diffuserLength, diffuserPosition, s + DiffuserStages, right);
            }

            for (int i = 0; i < LineCount; i++)
            {
                float y = lines[lineOffset[i] + linePosition[i]];
                float shaped = lowB0[i] * y + lowZ1[i];
                lowZ1[i] = lowB1[i] * y - lowA1[i] * shaped + lowZ2[i];
                lowZ2[i] = lowB2[i] * y - lowA2[i] * shaped;
                float output = dampB0[i] * shaped + dampZ[i];
                dampZ[i] = dampB1[i] * shaped - dampA1[i] * output;
                mixed[i] = output;
            }

            Hadamard(mixed);

            float wetLeft = mixed[LeftOutputRow] * OutputGain;
            float wetRight = mixed[RightOutputRow] * OutputGain;
            float injectLeft = left * InputGain;
            float injectRight = right * InputGain;
            for (int i = 0; i < LineCount; i++)
            {
                int position = linePosition[i];
                lines[lineOffset[i] + position] = mixed[i] * MixGain + signLeft[i] * injectLeft + signRight[i] * injectRight + AntiDenormal;
                linePosition[i] = ++position == lineLength[i] ? 0 : position;
            }

            float magnitude = MathF.Max(MathF.Abs(wetLeft), MathF.Abs(wetRight));
            if (magnitude > peak)
                peak = magnitude;

            wetLeft = bell1.Run(wetLeft, ref bellState[0], ref bellState[1]);
            wetRight = bell1.Run(wetRight, ref bellState[2], ref bellState[3]);
            wetLeft = bell2.Run(wetLeft, ref bellState[4], ref bellState[5]);
            wetRight = bell2.Run(wetRight, ref bellState[6], ref bellState[7]);

            float dry = _dry;
            if (dry != _dryTarget)
                _dry = dry = Approach(dry, _dryTarget, _gainCoefficient);
            float wet = _wet;
            if (wet != _wetTarget)
                _wet = wet = Approach(wet, _wetTarget, _gainCoefficient);

            buffer[n] = new StereoSample(x.left * dry + wetLeft * wet, x.right * dry + wetRight * wet);
        }

        return peak;
    }

    private static float Allpass(float[] buffer, int[] offset, int[] length, int[] position, int stage, float input)
    {
        int at = position[stage];
        int index = offset[stage] + at;
        float delayed = buffer[index];
        float fed = input + DiffuserGain * delayed;
        buffer[index] = fed;
        position[stage] = ++at == length[stage] ? 0 : at;
        return delayed - DiffuserGain * fed;
    }

    // In place, natural order: entry (row, column) is +1 or -1 by the parity of row AND column.
    private static void Hadamard(float[] values)
    {
        for (int half = 1; half < LineCount; half <<= 1)
        {
            for (int start = 0; start < LineCount; start += half << 1)
            {
                for (int i = start; i < start + half; i++)
                {
                    float a = values[i];
                    float b = values[i + half];
                    values[i] = a + b;
                    values[i + half] = a - b;
                }
            }
        }
    }

    private void ApplyDry(Span<StereoSample> buffer)
    {
        float dry = _dry;
        if (dry == 1f)
            return;
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = buffer[i].Multiply(dry);
    }

    private void FlushSmallStates()
    {
        float[] state = _bellState;
        for (int i = 0; i < state.Length; i++)
        {
            if (MathF.Abs(state[i]) < FlushFloor)
                state[i] = 0f;
        }
    }

    private static float[] RowSigns(int row)
    {
        var signs = new float[LineCount];
        for (int i = 0; i < LineCount; i++)
        {
            int bits = row & i;
            int parity = 0;
            while (bits != 0)
            {
                parity ^= bits & 1;
                bits >>= 1;
            }
            signs[i] = parity == 0 ? 1f : -1f;
        }
        return signs;
    }

    private static int NextPrime(int value)
    {
        for (int n = Math.Max(2, value); ; n++)
        {
            if (IsPrime(n))
                return n;
        }
    }

    private static bool IsPrime(int n)
    {
        if (n < 2)
            return false;
        if (n % 2 == 0)
            return n == 2;
        for (int d = 3; d * d <= n; d += 2)
        {
            if (n % d == 0)
                return false;
        }
        return true;
    }
}
