// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Godot.Hooks.Particles;

// One real light's worth of state. Intensity is what the candidate asked for; Ramp is how much of it
// is currently being paid out, so the renderer multiplies the two.
public struct ArbitratedLight
{
    public bool InUse;
    public uint Key;
    public float3 Position;
    public colorHDR Color;
    public float Intensity;
    public float Range;
    public float Rank;

    // 0 to 1, crossing over RampSeconds.
    public float Ramp;

    // 0 or 1. A slot on its way out sits at 0 with Ramp still falling.
    public float Target;

    // The candidate that won this slot off its incumbent and is waiting for the fade to finish.
    public bool HasPending;
    public ParticleLightCandidate Pending;

    // Seconds this slot's key has been absent from the candidate list.
    public float Missing;
}

// Decides which nominated particles are actually lit, and does not flicker doing it.
//
// The simulation ranks candidates and stops, because it cannot see the renderer's budget or what was
// lit last frame - and without both of those every rank crossing is a blink. So: an incumbent is
// recognised by Key and KEPT until a challenger beats it by a clear margin, a slot handed over fades
// out and back in rather than cutting, and a light that loses its particle outright fades instead of
// vanishing. The margin is what stops two particles of near-equal rank trading the same slot every
// single frame, which is the failure this whole class exists for. -xlinka
//
// The handover is deliberately sequential inside one slot: the outgoing light rides its ramp to zero,
// THEN the challenger takes the slot and rides back up. Cross-fading the two would need a second real
// light for the duration and the budget is a count of real lights. -xlinka
public sealed class ParticleLightArbiter
{
    // A challenger has to be this much better than the incumbent to take its slot.
    public const float ChallengeMargin = 1.25f;

    // Seconds a promotion or a demotion takes.
    public const float RampSeconds = 0.1f;

    // How long an incumbent survives not being nominated before it starts fading.
    //
    // Not zero, and this matters: the EveryNth selection walks the particle buffer on a stride, so a
    // particle that IS still alive drops out of the list the moment compaction shifts its index off a
    // multiple of the stride, and comes straight back the frame after. Fading on the first miss turns
    // that into a per-frame shimmer on a light nothing is actually wrong with. -xlinka
    public const float GraceSeconds = 0.06f;

    private ArbitratedLight[] _lights = System.Array.Empty<ArbitratedLight>();
    private bool[] _claimed = System.Array.Empty<bool>();
    private int _capacity;

    // Slots that exist. Entries past this are not touched and must not be read.
    public int Capacity => _capacity;

    public ArbitratedLight[] Lights => _lights;

    public float EffectiveIntensity(int index) => _lights[index].Intensity * _lights[index].Ramp;

    public bool IsLit(int index) => _lights[index].InUse && _lights[index].Ramp > 1e-4f;

    public int LitCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _capacity; i++)
            {
                if (IsLit(i))
                    n++;
            }
            return n;
        }
    }

    // Every slot goes dark and stays there. Used when the system leaves its view distance: the
    // simulation stops stepping out there, so a light left on would sit frozen in the dark.
    public void Extinguish()
    {
        for (int i = 0; i < _capacity; i++)
        {
            _lights[i].Target = 0f;
            _lights[i].HasPending = false;
        }
    }

    public void Clear()
    {
        for (int i = 0; i < _capacity; i++)
            _lights[i] = default;
    }

    public void Update(ReadOnlySpan<ParticleLightCandidate> candidates, int candidateCount, int budget, float deltaTime, bool visible)
    {
        budget = System.Math.Clamp(budget, 0, ParticleLightsModule.CandidateLimit);
        if (!visible)
            budget = 0;

        int count = System.Math.Clamp(candidateCount, 0, candidates.Length);
        EnsureCapacity(budget);
        EnsureClaims(count);
        for (int i = 0; i < count; i++)
            _claimed[i] = false;

        // Anything past the budget is on its way out no matter how good it is. A shrinking budget is
        // the component's MaxLights coming down or the candidate list drying up.
        for (int i = budget; i < _capacity; i++)
        {
            _lights[i].Target = 0f;
            _lights[i].HasPending = false;
        }

        RefreshIncumbents(candidates, count, budget, deltaTime);
        FillFreeSlots(candidates, count, budget);
        Challenge(candidates, count, budget);
        Advance(deltaTime);
    }

    // An incumbent keeps its slot for free: it is only asked to survive a challenge, never a re-sort.
    // Losing its candidate drops its rank to zero, which is what makes a dead particle's light the
    // first thing given up when something better turns up, and after the grace period it fades out on
    // its own - the particle is gone and nothing is going to update that light again.
    private void RefreshIncumbents(ReadOnlySpan<ParticleLightCandidate> candidates, int count, int budget, float deltaTime)
    {
        for (int i = 0; i < budget; i++)
        {
            ref var light = ref _lights[i];
            if (!light.InUse || light.HasPending)
                continue;

            int match = -1;
            for (int c = 0; c < count; c++)
            {
                if (_claimed[c] || candidates[c].Key != light.Key)
                    continue;
                match = c;
                break;
            }

            if (match < 0)
            {
                light.Rank = 0f;
                light.Missing += deltaTime;
                if (light.Missing >= GraceSeconds)
                    light.Target = 0f;
                continue;
            }

            _claimed[match] = true;
            Adopt(ref light, candidates[match]);
            light.Missing = 0f;
            light.Target = 1f;
        }
    }

    private void FillFreeSlots(ReadOnlySpan<ParticleLightCandidate> candidates, int count, int budget)
    {
        int cursor = 0;
        for (int i = 0; i < budget; i++)
        {
            ref var light = ref _lights[i];
            if (light.InUse)
                continue;

            while (cursor < count && _claimed[cursor])
                cursor++;
            if (cursor >= count)
                return;

            _claimed[cursor] = true;
            light.InUse = true;
            light.Ramp = 0f;
            light.HasPending = false;
            light.Missing = 0f;
            Adopt(ref light, candidates[cursor]);
            light.Target = 1f;
        }
    }

    // Candidates arrive sorted by rank descending, so the best unclaimed one is the first unclaimed
    // one. It only gets a slot by beating the weakest thing currently lit by the margin.
    private void Challenge(ReadOnlySpan<ParticleLightCandidate> candidates, int count, int budget)
    {
        for (int pass = 0; pass < budget; pass++)
        {
            int challenger = -1;
            for (int c = 0; c < count; c++)
            {
                if (_claimed[c])
                    continue;
                challenger = c;
                break;
            }
            if (challenger < 0)
                return;

            int weakest = -1;
            float weakestRank = float.MaxValue;
            for (int i = 0; i < budget; i++)
            {
                ref var light = ref _lights[i];
                if (!light.InUse || light.Target <= 0f || light.HasPending)
                    continue;
                if (light.Rank < weakestRank)
                {
                    weakestRank = light.Rank;
                    weakest = i;
                }
            }
            if (weakest < 0)
                return;

            if (!(candidates[challenger].Rank > weakestRank * ChallengeMargin))
                return;

            _claimed[challenger] = true;
            ref var loser = ref _lights[weakest];
            loser.Target = 0f;
            loser.HasPending = true;
            loser.Pending = candidates[challenger];
        }
    }

    private void Advance(float deltaTime)
    {
        float step = deltaTime > 0f ? deltaTime / RampSeconds : 0f;

        for (int i = 0; i < _capacity; i++)
        {
            ref var light = ref _lights[i];
            if (!light.InUse)
                continue;

            if (light.Ramp < light.Target)
                light.Ramp = System.Math.Min(light.Target, light.Ramp + step);
            else if (light.Ramp > light.Target)
                light.Ramp = System.Math.Max(light.Target, light.Ramp - step);

            if (light.Target > 0f || light.Ramp > 0f)
                continue;

            if (light.HasPending)
            {
                Adopt(ref light, light.Pending);
                light.HasPending = false;
                light.Missing = 0f;
                light.Target = 1f;
            }
            else
            {
                light.InUse = false;
                light.Key = 0;
                light.Rank = 0f;
            }
        }
    }

    private static void Adopt(ref ArbitratedLight light, in ParticleLightCandidate candidate)
    {
        light.Key = candidate.Key;
        light.Position = candidate.Position;
        light.Color = candidate.Color;
        light.Intensity = candidate.Intensity;
        light.Range = candidate.Range;
        light.Rank = candidate.Rank;
    }

    // Grows and never shrinks: a slot below the current budget can still be fading out, and dropping
    // the array would take its node with it mid-fade.
    private void EnsureCapacity(int budget)
    {
        if (budget > _capacity)
        {
            if (_lights.Length < budget)
                System.Array.Resize(ref _lights, budget);
            _capacity = budget;
        }
    }

    private void EnsureClaims(int count)
    {
        if (_claimed.Length < count)
            _claimed = new bool[System.Math.Max(count, _claimed.Length * 2)];
    }
}
