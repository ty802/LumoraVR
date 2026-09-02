// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Runtime.CompilerServices;
using Logger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

// One per world: how much wall time this world's soft bodies spent last frame, and what that means for
// how hard they are allowed to solve this frame.
//
// WHY CLOTH GETS A BUDGET THAT ACTUALLY CUTS AND HAIR DOES NOT. The dynamic bone manager measures its
// cost and reports it without dropping anything, because there is no way to spend less on a bone chain
// that does not show: half the room's hair going stiff is worse than the frame it saves. Cloth has a
// lever hair does not. The solver corrects stiffness for the iteration count - the same Stiffness
// setting compounds to the same stiffness at eight iterations and at three - so spending fewer
// iterations costs accuracy in the constraint solve, not the drape, the weight or the stiffness of the
// fabric. A garment solved at three passes instead of eight sags a little further under its own weight
// on a fast move and catches up on the next one. That is a real degradation and it is a graceful one,
// which is the only kind worth building.
//
// The ramp is deliberately slow in both directions. A single expensive frame is a hitch, not a load
// problem, and a budget that reacted to one would spend its life oscillating. -xlinka
public sealed class SoftBodyBudget
{
    private static readonly ConditionalWeakTable<World, SoftBodyBudget> Budgets = new();

    // Wall time a world's soft bodies may spend in a frame before the iteration scale starts falling.
    // Two milliseconds is a third of a 60Hz frame handed to secondary motion, which is generous for
    // something nobody is looking at directly.
    private const double BudgetMilliseconds = 2.0;

    // Frames a world gets to settle before the budget applies. The first passes pay for jitting this
    // code and a world that just came up is loading everything it owns.
    private const ulong WarmupFrames = 120;

    // Per-frame multiplier steps. Down fast enough to catch up with a room filling, up slow enough that
    // a body does not flicker between two iteration counts on the boundary.
    private const float ScaleDown = 0.85f;
    private const float ScaleUp = 1.05f;
    private const float MinScale = 0.25f;

    // Headroom before the scale is allowed to climb back, so it settles instead of hunting.
    private const double RecoverFraction = 0.6;

    private ulong _frame = ulong.MaxValue;
    private ulong _framesRun;
    private double _accumulator;
    private bool _reported;

    private SoftBodyBudget() { }

    public static SoftBodyBudget? For(World? world)
        => world == null ? null : Budgets.GetValue(world, static _ => new SoftBodyBudget());

    // Multiplier the bodies apply to their solver iteration count this frame. 1 when the world is
    // inside its budget.
    public float IterationScale { get; private set; } = 1f;

    // Wall time the last completed frame's soft bodies cost, all bodies summed.
    public double LastFrameMilliseconds { get; private set; }

    // Bodies that reported a step last frame.
    public int LastSteppedBodies { get; private set; }

    private int _stepped;

    // Called by every body that actually stepped. The first call of a frame closes the previous one and
    // settles the scale the whole world uses until the next.
    public void Report(ulong frame, double milliseconds)
    {
        if (frame != _frame)
        {
            LastFrameMilliseconds = _accumulator;
            LastSteppedBodies = _stepped;
            _frame = frame;
            _accumulator = 0.0;
            _stepped = 0;
            _framesRun++;
            Settle();
        }

        _accumulator += milliseconds;
        _stepped++;
    }

    private void Settle()
    {
        if (_framesRun < WarmupFrames)
        {
            IterationScale = 1f;
            return;
        }

        if (LastFrameMilliseconds > BudgetMilliseconds)
        {
            IterationScale = System.Math.Max(MinScale, IterationScale * ScaleDown);
            if (!_reported && IterationScale <= MinScale)
            {
                _reported = true;
                Logger.Warn($"SoftBodyBudget: {LastSteppedBodies} soft bodies cost {LastFrameMilliseconds:F2}ms in a " +
                            $"frame (budget {BudgetMilliseconds:F2}ms). Solver iterations are scaled to the floor " +
                            $"({MinScale:F2}); reported once per world.");
            }
        }
        else if (LastFrameMilliseconds < BudgetMilliseconds * RecoverFraction && IterationScale < 1f)
        {
            IterationScale = System.Math.Min(1f, IterationScale * ScaleUp);
        }
    }
}
