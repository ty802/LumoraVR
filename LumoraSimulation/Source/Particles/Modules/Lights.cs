// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

public enum ParticleLightSelection
{
    // Thins the pool by taking every Nth particle. Deterministic, cheap, and spreads the lights over
    // the buffer instead of clustering them at whichever end the emitter happened to fill.
    EveryNth,

    // Considers every particle and keeps the ones that matter most.
    Importance,
}

// One particle nominated as a light source. The simulation produces a ranked, capped list of these
// every frame; deciding which ones actually become real lights is the renderer's budget problem, and
// Rank plus Key is everything it needs to make that decision without flickering.
public readonly struct ParticleLightCandidate
{
    public readonly float3 Position;

    // Tint only. Intensity is carried separately so a renderer can scale one without the other.
    public readonly colorHDR Color;

    public readonly float Intensity;

    public readonly float Range;

    // How much this light is worth this frame. Higher wins. See ParticleLightsModule for the formula.
    public readonly float Rank;

    // Stable for the whole life of the particle and unchanged by buffer compaction, so a renderer can
    // recognise a light it lit last frame. Derived from the particle's birth seed: distinct in
    // practice, not guaranteed unique, so treat a match as a strong hint and never as an assertion.
    public readonly uint Key;

    public ParticleLightCandidate(in float3 position, in colorHDR color, float intensity, float range, float rank, uint key)
    {
        Position = position;
        Color = color;
        Intensity = intensity;
        Range = range;
        Rank = rank;
        Key = key;
    }
}

// Nominates particles as light sources.
//
// This module lights nothing. It produces a short, ranked, hard-capped candidate list and stops,
// because the simulation has no idea what the renderer's light budget is, what is on screen, or what
// it already had lit last frame - and every one of those has to be in the decision or lights pop.
//
// RANK is intensity weighted by where the particle is in its life: a light that has just been born or
// is about to die scores lower, so when the budget forces a drop it drops the one whose disappearance
// is least visible. The list comes out sorted by rank descending.
//
// HYSTERESIS is the renderer's job and it must do it. Take the top N by rank, but keep a candidate
// that was lit last frame (match on Key) until a challenger beats it by a margin - a factor of about
// 1.25 is enough to stop two particles of near-equal rank trading the same slot every frame - and ramp
// a promoted or demoted light's intensity over roughly 0.1 s instead of switching it. Without that,
// every rank crossing is a visible blink. -xlinka
public sealed class ParticleLightsModule : ParticleSimModule
{
    public const int CandidateLimit = 256;

    private ParticleLightCandidate[] _candidates = System.Array.Empty<ParticleLightCandidate>();
    private int _count;

    // Hard cap on the published list.
    public int MaxLights = 16;

    // EveryNth stride. 1 considers every particle.
    public int Stride = 8;

    public ParticleLightSelection Selection = ParticleLightSelection.EveryNth;

    public colorHDR BaseColor = colorHDR.White;
    public float BaseIntensity = 1f;
    public float BaseRange = 4f;

    public bool ColorFromParticle = true;

    // Folds the particle's alpha into the intensity, so a light fades out with the particle it is on
    // instead of staying at full brightness until the particle vanishes.
    public bool IntensityFromAlpha = true;

    public bool RangeFromSize = true;
    public ParticleSizeAxis RangeAxis = ParticleSizeAxis.Average;

    // Fraction of the lifetime over which a light's rank rises at birth and falls before death.
    public float RiseFraction = 0.1f;
    public float FallFraction = 0.25f;

    public override ParticleSimPhase Phase => ParticleSimPhase.Output;

    public override bool AllowMultipleInstances => false;

    public override void SimulateChunk(int offset, int count, float deltaTime) { }

    // Valid entries are [0, CandidateCount), sorted by Rank descending.
    public ParticleLightCandidate[] Candidates => _candidates;

    public int CandidateCount => _count;

    public override void PostprocessUpdate(float deltaTime)
    {
        int cap = System.Math.Clamp(MaxLights, 0, CandidateLimit);
        if (_candidates.Length < cap)
            _candidates = new ParticleLightCandidate[cap];
        _count = 0;
        if (cap == 0)
            return;

        int live = Simulation.ParticleCount;
        if (live == 0)
            return;

        int stride = Selection == ParticleLightSelection.EveryNth
            ? System.Math.Max(1, Stride)
            : 1;

        var positions = Simulation.RenderPositions;
        var colors = Simulation.RenderColors;
        var sizes = Simulation.RenderSizes;
        var progression = Simulation.NormalizedProgressions;
        var seeds = Simulation.StartingSeeds;

        float rise = System.Math.Clamp(RiseFraction, 0f, 1f);
        float fall = System.Math.Clamp(FallFraction, 0f, 1f);

        for (int i = 0; i < live; i += stride)
        {
            var particleColor = colors[i];

            float intensity = BaseIntensity;
            if (IntensityFromAlpha)
                intensity *= particleColor.a;
            if (!(intensity > 0f))
                continue;

            float range = BaseRange;
            if (RangeFromSize)
                range *= ParticleSizeAxisHelper.Resolve(sizes[i], RangeAxis);
            if (!(range > 0f))
                continue;

            var tint = BaseColor;
            if (ColorFromParticle)
            {
                tint.r *= particleColor.r;
                tint.g *= particleColor.g;
                tint.b *= particleColor.b;
            }

            float rank = intensity * AgeWeight(progression[i], rise, fall);
            if (!(rank > 0f))
                continue;

            Insert(new ParticleLightCandidate(positions[i], tint, intensity, range, rank, seeds[i]), cap);
        }
    }

    // 0 at the instant of birth and of death, 1 through the middle. Smoothstepped at both ends so the
    // rank does not jump the frame a particle crosses the threshold.
    private static float AgeWeight(float progression, float rise, float fall)
    {
        float t = System.Math.Clamp(progression, 0f, 1f);
        float weight = 1f;
        if (rise > 0f)
            weight *= ParticleMath.SmoothStep(0f, rise, t);
        if (fall > 0f)
            weight *= 1f - ParticleMath.SmoothStep(1f - fall, 1f, t);
        return weight;
    }

    // Keeps the array sorted by rank descending. The common case for a full list is one comparison
    // against the weakest entry and no shifting at all.
    private void Insert(in ParticleLightCandidate candidate, int cap)
    {
        if (_count == cap)
        {
            if (candidate.Rank <= _candidates[_count - 1].Rank)
                return;
        }
        else
        {
            _count++;
        }

        int i = _count - 1;
        while (i > 0 && _candidates[i - 1].Rank < candidate.Rank)
        {
            _candidates[i] = _candidates[i - 1];
            i--;
        }
        _candidates[i] = candidate;
    }

    public override void TrimParticles(int newCount)
    {
        if (newCount == 0)
            _count = 0;
    }
}
