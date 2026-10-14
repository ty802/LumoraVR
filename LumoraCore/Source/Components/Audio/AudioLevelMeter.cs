// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Audio;

namespace Lumora.Core.Components.Audio;

// How loud something is right now, as a plain 0 to 1 field anything can copy or read: a speaker cone that
// pulses, a lamp that breathes with the music. AudioAnalyzer is the full picture (bands, loudness, beats);
// this is the one number, at the cost of a sum over each frame's samples.
//
// Source is what is playing. The meter listens to the output on its own slot, or the slot above, that plays
// it, and hears it as the mixer does before the output's volume, so the reading is the sound itself wherever
// the listener stands. Each frame's average swing (or change, see Method) is raised to Power, so under 1
// lifts quiet passages, then eased in: Smoothing is the share of the last reading kept.
//
// Level is this machine's own reading and is never sent: every machine hears the same sound, so every
// machine's meter agrees closely without a number crossing the wire. -xlinka
[ComponentCategory("Audio")]
public sealed class AudioLevelMeter : Component
{
    private const int DrainLength = 2048;
    private const double StaleSeconds = 0.25;

    public readonly SyncRef<Component> Source;
    [Range(0f, 1f, "0.00")]
    public readonly Sync<float> Smoothing;
    public readonly Sync<float> Power;
    public readonly Sync<AudioLevelMethod> Method;
    public readonly Sync<float> Level;

    private readonly FieldDrive<float> _level;
    private readonly StereoSample[] _drain = new StereoSample[DrainLength];
    private OutputMonitorTap? _tap;
    private AudioOutput? _output;
    private float _reading;
    private float _lastSample;
    private double _lastListen = double.NegativeInfinity;

    public AudioLevelMeter()
    {
        Source = new SyncRef<Component>(this);
        Smoothing = new Sync<float>(this, 0f);
        Power = new Sync<float>(this, 1f);
        Method = new Sync<AudioLevelMethod>(this, AudioLevelMethod.Magnitude);
        Level = new Sync<float>(this, 0f);
        _level = new FieldDrive<float>(this) { LocalValueOnly = true };
    }

    public override void OnStart()
    {
        base.OnStart();
        if (!ReferenceEquals(_level.Target, Level))
            _level.DriveTarget(Level);
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        if (!IsDestroyed)
            Listen();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        StopListening();
        Show(0f);
    }

    public override void OnDestroy()
    {
        StopListening();
        base.OnDestroy();
    }

    private void Listen()
    {
        var simulator = Engine.Current?.AudioSystem?.Simulator;
        var voice = simulator != null ? FindOutput()?.Voice : null;
        if (simulator == null || voice == null)
        {
            _tap?.Skip();
            Show(Ease(0f));
            return;
        }
        if (_tap == null || !ReferenceEquals(_tap.Simulator, simulator))
        {
            _tap?.Dispose();
            _tap = new OutputMonitorTap(simulator, DrainLength * 4);
        }

        double now = World?.Time.TotalTime ?? 0.0;
        if (now - _lastListen > StaleSeconds)
            _tap.Skip();
        _lastListen = now;
        _tap.Voice = voice;

        bool change = Method.Value == AudioLevelMethod.Change;
        double sum = 0.0;
        int count = 0;
        int read;
        while ((read = _tap.Read(_drain)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                float mono = (_drain[i].left + _drain[i].right) * 0.5f;
                sum += change ? MathF.Abs(mono - _lastSample) : MathF.Abs(mono);
                _lastSample = mono;
            }
            count += read;
        }
        if (count == 0)
            return;

        float level = (float)(sum / count);
        level = float.IsFinite(level) ? System.Math.Clamp(level, 0f, 1f) : 0f;
        float power = Power.Value;
        if (float.IsFinite(power) && power > 0f && power != 1f)
            level = MathF.Pow(level, power);
        Show(Ease(level));
    }

    private float Ease(float target)
    {
        float keep = System.Math.Clamp(Smoothing.Value, 0f, 1f);
        _reading = target + (_reading - target) * keep;
        return _reading;
    }

    private void Show(float value)
    {
        if (_level.IsLinkValid)
            _level.SetValue(value);
    }

    // The output playing Source, on this slot or the one above. Kept while it still plays it.
    private AudioOutput? FindOutput()
    {
        var source = Source.Target;
        if (source == null || source.IsDestroyed)
            return _output = null;
        if (_output != null && !_output.IsDestroyed && ReferenceEquals(_output.Source.Target, source))
            return _output;
        _output = null;
        for (var slot = Slot; slot != null && _output == null; slot = ReferenceEquals(slot, Slot) ? slot.Parent : null)
        {
            foreach (var component in slot.Components)
            {
                if (component is AudioOutput output && !output.IsDestroyed && ReferenceEquals(output.Source.Target, source))
                {
                    _output = output;
                    break;
                }
            }
        }
        return _output;
    }

    private void StopListening()
    {
        _tap?.Dispose();
        _tap = null;
        _output = null;
    }
}
