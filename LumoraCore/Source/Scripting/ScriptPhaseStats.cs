// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// The script phase of one world on this machine, frame by frame and over the last full second. Shared is
// the host's pass, Local the pass every machine runs for whoever is at it. -xlinka
public sealed class ScriptPhaseStats
{
    public double LastMs { get; private set; }
    public double LastSharedMs { get; private set; }
    public double LastLocalMs { get; private set; }

    public bool HasWindow { get; private set; }
    public double AverageMs { get; private set; }
    public double PeakMs { get; private set; }
    public double AverageSharedMs { get; private set; }
    public double AverageLocalMs { get; private set; }
    public double CallsPerSecond { get; private set; }
    public double FuelPerSecond { get; private set; }
    public int Frames { get; private set; }

    private double _windowStart = double.NaN;
    private double _windowMs;
    private double _windowShared;
    private double _windowLocal;
    private double _windowPeak;
    private int _windowFrames;
    private long _windowCalls;
    private long _windowFuel;

    internal void Call(long fuel)
    {
        _windowCalls++;
        _windowFuel += fuel;
    }

    internal void EndFrame(double now, double totalMs, double sharedMs, double localMs)
    {
        LastMs = totalMs;
        LastSharedMs = sharedMs;
        LastLocalMs = localMs;
        if (double.IsNaN(_windowStart))
            _windowStart = now;
        _windowMs += totalMs;
        _windowShared += sharedMs;
        _windowLocal += localMs;
        if (totalMs > _windowPeak)
            _windowPeak = totalMs;
        _windowFrames++;

        double elapsed = now - _windowStart;
        if (elapsed < ScriptRuntimeStats.WindowSeconds)
            return;
        AverageMs = _windowMs / _windowFrames;
        AverageSharedMs = _windowShared / _windowFrames;
        AverageLocalMs = _windowLocal / _windowFrames;
        PeakMs = _windowPeak;
        CallsPerSecond = _windowCalls / elapsed;
        FuelPerSecond = _windowFuel / elapsed;
        Frames = _windowFrames;
        HasWindow = true;

        _windowStart = now;
        _windowMs = 0;
        _windowShared = 0;
        _windowLocal = 0;
        _windowPeak = 0;
        _windowFrames = 0;
        _windowCalls = 0;
        _windowFuel = 0;
    }
}
