// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// How one running script is doing on this machine: what it cost to start, running totals, and the last
// full second, which is what a diagnostics panel shows. The scheduler records every call it makes and
// closes each frame, so a script that went quiet reads zero a second later. -xlinka
public sealed class ScriptRuntimeStats
{
    public const double WindowSeconds = 1.0;

    // Fixed when it starts. CompileMs is this graph's compile on this machine, whenever that happened;
    // CompileCached says this start found it compiled already.
    public int Nodes { get; internal set; }
    public int ModuleBytes { get; internal set; }
    public double CompileMs { get; internal set; }
    public bool CompileCached { get; internal set; }
    public double LoadMs { get; internal set; }
    public double StartedAt { get; internal set; }

    public long Calls { get; private set; }
    public double TotalMs { get; private set; }
    public long TotalFuel { get; private set; }
    public double PeakCallMs { get; private set; }
    public long PeakCallFuel { get; private set; }
    public int Traps { get; private set; }
    public int OutOfFuel { get; private set; }
    public int OutOfTime { get; private set; }

    // The last full second. Deferred frames ended with events still waiting for time.
    public bool HasWindow { get; private set; }
    public double MsPerFrame { get; private set; }
    public double PeakFrameMs { get; private set; }
    public double CallsPerSecond { get; private set; }
    public double FuelPerSecond { get; private set; }

    private double _frameMs;
    private double _windowStart = double.NaN;
    private double _windowMs;
    private double _windowPeak;
    private int _windowFrames;
    private long _windowCalls;
    private long _windowFuel;

    // A Display refresh takes time from the frame but is not a call the script made.
    internal void Record(double ms, long fuel, ScriptCallStatus status, bool call = true)
    {
        _frameMs += ms;
        TotalMs += ms;
        if (!call)
            return;
        Calls++;
        _windowCalls++;
        TotalFuel += fuel;
        _windowFuel += fuel;
        if (ms > PeakCallMs)
            PeakCallMs = ms;
        if (fuel > PeakCallFuel)
            PeakCallFuel = fuel;
        if (status == ScriptCallStatus.OutOfFuel)
            OutOfFuel++;
        else if (status == ScriptCallStatus.OutOfTime)
            OutOfTime++;
        else if (status == ScriptCallStatus.Trap)
            Traps++;
    }

    internal void EndFrame(double now)
    {
        if (double.IsNaN(_windowStart))
            _windowStart = now;
        _windowMs += _frameMs;
        if (_frameMs > _windowPeak)
            _windowPeak = _frameMs;
        _frameMs = 0;
        _windowFrames++;

        double elapsed = now - _windowStart;
        if (elapsed < WindowSeconds)
            return;
        MsPerFrame = _windowMs / _windowFrames;
        PeakFrameMs = _windowPeak;
        CallsPerSecond = _windowCalls / elapsed;
        FuelPerSecond = _windowFuel / elapsed;
        HasWindow = true;

        _windowStart = now;
        _windowMs = 0;
        _windowPeak = 0;
        _windowFrames = 0;
        _windowCalls = 0;
        _windowFuel = 0;
    }
}
