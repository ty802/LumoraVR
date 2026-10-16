// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Diagnostics;

namespace Lumora.Source.Godot.Bootstrap;

// How many physics steps one frame may run.
//
// A frame owes the physics engine a step for every tick that passed while it ran. When a step costs more
// than the tick it simulates, paying that back never ends: a slow frame owes more steps, the steps make the
// next frame slower still, and every frame ends up running the most steps it is allowed. Ten thousand awake
// bodies cost 16 to 22 ms a step at 60 ticks a second, so they ran eight steps a frame at four frames a
// second, and the simulation still fell behind.
//
// The cap follows what a step costs instead: a frame spends about one 60 Hz frame on steps at most, so an
// overloaded simulation runs slower than real time while the frame rate holds. A light scene keeps the full
// catch-up it always had. A capped frame also stops the platform's own delta at the steps that ran, and the
// engine is handed the time that really passed, so only the bodies slow down and nothing else does. -xlinka
internal sealed class PhysicsStepPacer
{
    private const int MostSteps = 8;
    private const double StepBudgetMs = 1000.0 / 60.0;

    private long _firstTick;
    private long _lastTick;
    private ulong _lastFrames;
    private double _stepMs = -1;

    public int Cap { get; private set; } = MostSteps;

    // A step as last measured on the main thread, its callbacks included; negative before the first.
    public double StepMs => _stepMs;

    public void OnTick()
    {
        long now = Stopwatch.GetTimestamp();
        if (_firstTick == 0)
            _firstTick = now;
        _lastTick = now;
    }

    // At the start of the frame's process pass, after its steps. Returns what the engine should advance by.
    public double BeginFrame(double delta, double wallSeconds)
    {
        ulong frames = global::Godot.Engine.GetPhysicsFrames();
        int steps = _lastFrames == 0 ? 0 : (int)Math.Min(frames - _lastFrames, int.MaxValue);
        _lastFrames = frames;

        if (steps > 0 && _firstTick != 0)
        {
            // Two or more: tick to tick is one whole step. One: up to here, which is near enough.
            long span = steps > 1 ? _lastTick - _firstTick : Stopwatch.GetTimestamp() - _firstTick;
            double ms = span * 1000.0 / Stopwatch.Frequency / Math.Max(steps - 1, 1);
            _stepMs = _stepMs < 0 ? ms : _stepMs * 0.8 + ms * 0.2;
        }
        _firstTick = 0;

        bool capped = steps >= Cap;
        int cap = _stepMs <= 0 ? MostSteps : Math.Clamp((int)(StepBudgetMs / _stepMs), 1, MostSteps);
        if (cap != Cap)
        {
            Cap = cap;
            global::Godot.Engine.MaxPhysicsStepsPerFrame = cap;
        }

        if (!capped || wallSeconds <= delta)
            return delta;
        int rate = Math.Max(global::Godot.Engine.PhysicsTicksPerSecond, 1);
        return Math.Min(wallSeconds, (double)MostSteps / rate);
    }
}
