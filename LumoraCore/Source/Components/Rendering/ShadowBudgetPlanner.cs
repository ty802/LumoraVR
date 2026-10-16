// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Components;

// One point or spot light asking for a shadow, as the planner sees it.
public struct ShadowCandidate
{
    // How much this light's shadow matters to the viewer. Zero means it may not hold one at all.
    public float Score;

    // In: holds a shadow now. Out: holds one after the plan.
    public bool Granted;

    // Seconds it has held its current grant.
    public float HeldSeconds;
}

// WHICH LIGHTS CAST.
//
// A shadowed point light is six scene renders whenever anything in its range moves, and in a room with
// people in it something always moves: a lobby with six of them spent nine tenths of its draw calls in the
// shadow pass. So only the lights that matter most to the person looking get a shadow, the rest keep
// lighting without one, and a world with no more shadowed lights than the budget looks exactly as built.
//
// The pick is by light, not by frame: a granted light keeps its shadow until a rival scores clearly
// higher and it has held for a while, and at most one swap happens per plan, so walking through a room
// hands shadows over one at a time instead of flickering them. Engine-free on purpose, so the selection
// can be checked without a renderer. -xlinka
public static class ShadowBudgetPlanner
{
    // A rival has to beat a holder by this much before it takes the shadow.
    public const float SwapMargin = 1.25f;

    // Seconds a fresh grant is safe from being taken by a rival.
    public const float MinHoldSeconds = 1f;

    // Metres past a light's own range that its shadow is still worth drawing, before the user's shadow
    // distance scale. Beyond that the nearest thing it lights is far enough that its shadows are specks.
    public const float BaseReach = 25f;

    // What the planner granted last time, for the lighting tools and the profiler.
    public static int LastGranted { get; private set; }
    public static int LastCandidates { get; private set; }
    public static int LastBudget { get; private set; }

    // Inside the range a light scores 1 to 2 by how close to its centre the viewer stands; outside, the
    // range over the distance, which falls like the size of the lit volume on screen. Brightness enters
    // as a square root so a light turned up tenfold does not drown every other one. Zero when the lit
    // volume is out of reach.
    public static float Score(float distance, float range, float brightness, float reach)
    {
        if (!(brightness > 0f) || !(range > 0f))
            return 0f;
        if (distance < 0f)
            distance = 0f;
        if (distance - range > reach)
            return 0f;
        float proximity = distance <= range ? 2f - distance / range : range / distance;
        return MathF.Sqrt(brightness) * proximity;
    }

    // Decides the grants in place. Returns how many candidates changed hands.
    public static int Plan(Span<ShadowCandidate> candidates, int budget)
    {
        if (budget < 0)
            budget = 0;
        int changes = 0;
        int granted = 0;
        int eligible = 0;

        // A holder that is no longer eligible gives its shadow up at once.
        for (int i = 0; i < candidates.Length; i++)
        {
            ref var c = ref candidates[i];
            if (c.Score > 0f)
                eligible++;
            if (!c.Granted)
                continue;
            if (c.Score <= 0f)
            {
                c.Granted = false;
                c.HeldSeconds = 0f;
                changes++;
            }
            else
            {
                granted++;
            }
        }

        // Over budget, the budget was lowered: the weakest holders go first.
        while (granted > budget)
        {
            int weakest = Weakest(candidates, ignoreHold: true);
            if (weakest < 0)
                break;
            candidates[weakest].Granted = false;
            candidates[weakest].HeldSeconds = 0f;
            granted--;
            changes++;
        }

        // Free places go to the strongest waiting lights straight away.
        while (granted < budget)
        {
            int strongest = Strongest(candidates);
            if (strongest < 0)
                break;
            candidates[strongest].Granted = true;
            candidates[strongest].HeldSeconds = 0f;
            granted++;
            changes++;
        }

        // Full: one swap at most, and only for a clear winner over a holder that has had its turn.
        if (granted > 0)
        {
            int challenger = Strongest(candidates);
            int holder = Weakest(candidates, ignoreHold: false);
            if (challenger >= 0 && holder >= 0
                && candidates[challenger].Score > candidates[holder].Score * SwapMargin)
            {
                candidates[holder].Granted = false;
                candidates[holder].HeldSeconds = 0f;
                candidates[challenger].Granted = true;
                candidates[challenger].HeldSeconds = 0f;
                changes += 2;
            }
        }

        LastGranted = granted;
        LastCandidates = eligible;
        LastBudget = budget;
        return changes;
    }

    // The strongest eligible light without a grant, or -1.
    private static int Strongest(Span<ShadowCandidate> candidates)
    {
        int best = -1;
        float bestScore = 0f;
        for (int i = 0; i < candidates.Length; i++)
        {
            ref var c = ref candidates[i];
            if (c.Granted || c.Score <= bestScore)
                continue;
            best = i;
            bestScore = c.Score;
        }
        return best;
    }

    // The weakest holder, or -1. Unless told otherwise, one still inside its hold time is passed over.
    private static int Weakest(Span<ShadowCandidate> candidates, bool ignoreHold)
    {
        int worst = -1;
        float worstScore = float.MaxValue;
        for (int i = 0; i < candidates.Length; i++)
        {
            ref var c = ref candidates[i];
            if (!c.Granted || (!ignoreHold && c.HeldSeconds < MinHoldSeconds))
                continue;
            if (c.Score < worstScore)
            {
                worst = i;
                worstScore = c.Score;
            }
        }
        return worst;
    }
}
