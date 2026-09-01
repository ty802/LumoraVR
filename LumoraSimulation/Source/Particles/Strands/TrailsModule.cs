// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

// How much of a particle's own colour or size a trail takes on.
public enum TrailInheritance
{
    // The trail ignores the particle entirely and uses only what the initializers gave it.
    None,

    // Sampled once, at birth. The trail keeps that value even as the particle fades or shrinks.
    Birth,

    // Sampled as each point is laid, so the trail records the particle's history: the tail keeps the
    // colour the particle had when it passed through, which is the one that reads as a real trail.
    Continuous,

    // Sampled every frame and written across every point, so the whole trail changes together. Costs a
    // pass over the trail's points per frame; use it when the trail has to read as one solid object.
    WholeTrail,
}

// How a fractional Ratio picks which particles get a strand.
public enum StrandDistribution
{
    // Independent draw per particle. Correct on average, clumpy in the small numbers.
    Random,

    // Deterministic every-Nth from a carried accumulator. Even, and identical on every peer without
    // touching the random stream at all.
    Regular,
}

// Per-trail values fixed at birth, before any point is laid. Initializer modules multiply into this.
public struct TrailStrandState
{
    public colorHDR BaseColor;
    public float BaseWidth;

    // Multiplier on the module's MaxPointAge for this one trail.
    public float AgeScale;

    // Index of the particle this trail is following, or -1 once that particle has died.
    public int ParticleIndex;

    // Seconds of death fade left. Only meaningful once ParticleIndex is -1.
    public float FadeRemaining;
}

public interface ITrailStrandInitializer
{
    void InitializeTrail(ParticleTrailsModule trails, int particleIndex, ref TrailStrandState state);
}

// Records where particles have been and publishes the history as strands.
//
// A trail is a ring of control points laid down behind its particle. The newest point is the TIP and
// is rewritten to the particle's position every frame, so the trail is glued to the particle instead
// of stepping along behind it; a new point is committed only once the tip has pulled MinVertexDistance
// clear of the last committed one, which is what keeps a slow particle from filling its whole ring
// with points a millimetre apart.
//
// Points expire on their own age, independently of the particle - so a long-lived particle still has a
// short trail if MaxPointAge is short. The oldest point does not pop out when it expires: it is slid
// along the segment toward its neighbour until it sits exactly on the age cutoff, so the tail recedes
// smoothly rather than losing a whole segment at a time.
//
// When the particle dies the trail can outlive it and fade out in place, which is the difference
// between a firework that leaves streaks and one that leaves nothing. -xlinka
public sealed class ParticleTrailsModule : ParticleSimModule
{
    public const int TrailLimit = 4096;
    public const int PointLimit = 512;

    private readonly TrailPointStore _store = new();
    private readonly ParticleBuffer<int> _slotOf = new();
    private readonly List<ITrailStrandInitializer> _initializers = new();

    private TrailStrandState[] _states = System.Array.Empty<TrailStrandState>();
    private int[] _active = System.Array.Empty<int>();
    private int[] _activeIndex = System.Array.Empty<int>();
    private int _activeCount;
    private int _configuredTrails;
    private int _configuredPoints;
    private float _distributionAccumulator;

    // Hard ceiling on live trails. Particles past it simply get none.
    public int MaxTrails = 256;

    // Ring size per trail. The oldest point is overwritten once it is reached.
    public int MaxPointsPerTrail = 48;

    // Metres the tip must pull clear of the last committed point before another is committed.
    public float MinVertexDistance = 0.02f;

    // Seconds a point survives after being laid.
    public float MaxPointAge = 0.6f;

    // Fraction of particles that get a trail at all.
    public float Ratio = 1f;

    public StrandDistribution Distribution = StrandDistribution.Random;

    // True cuts the trail the instant its particle dies. False lets it linger and fade.
    public bool DieWithParticle;

    // Seconds an orphaned trail takes to fade to nothing. 0 skips the fade and lets the points simply
    // age out on MaxPointAge, which is the right look for something that leaves a real mark.
    public float DeathFadeTime = 0.25f;

    public TrailInheritance ColorInheritance = TrailInheritance.Continuous;
    public TrailInheritance WidthInheritance = TrailInheritance.Birth;
    public ParticleSizeAxis WidthAxis = ParticleSizeAxis.Average;

    // Sampled 0 at the tip (the particle end) to 1 at the far, oldest end.
    public FloatCurve? WidthOverTrail;
    public ColorGradient? ColorOverTrail;

    public float WidthScale = 1f;
    public colorHDR ColorScale = colorHDR.White;

    public StrandUVMode UVMode = StrandUVMode.StretchAlongStrand;
    public StrandAlignment Alignment = StrandAlignment.CameraFacing;
    public float3 AlignmentUp = float3.Up;
    public float TileLength = 1f;

    // Suggested smoothed vertices per control segment. The renderer is free to ignore it.
    public int Smoothing = 4;

    public override ParticleSimPhase Phase => ParticleSimPhase.Output;

    public override bool AllowMultipleInstances => false;

    // Everything happens in PostprocessUpdate, which is single threaded by contract.
    public override void SimulateChunk(int offset, int count, float deltaTime) { }

    // The published frame. Read Strands/Positions/Colors/Widths; see ParticleStrandOutput.
    public ParticleStrandOutput Output { get; } = new();

    public int TrailCount => _activeCount;

    public int TrailPointCapacity => _store.SlotCapacity * _store.PointsPerSlot;

    public int PointsIn(int activeIndex) => _store.CountOf(_active[activeIndex]);

    public override void ModulesUpdated()
    {
        base.ModulesUpdated();
        _initializers.Clear();
        for (int i = 0; i < Simulation.ModuleCount; i++)
        {
            var module = Simulation.GetModule(i);
            if (module.IsActive && module is ITrailStrandInitializer initializer)
                _initializers.Add(initializer);
        }
    }

    public override void PrepareUpdate(float deltaTime)
    {
        int trails = System.Math.Clamp(MaxTrails, 1, TrailLimit);
        int points = System.Math.Clamp(MaxPointsPerTrail, 2, PointLimit);
        if (trails == _configuredTrails && points == _configuredPoints)
            return;

        // Reshaping the slab throws every live trail away. There is no honest way to rehome rings of
        // different lengths mid-flight, and pretending there is would leave points from one trail
        // showing up in another. -xlinka
        _configuredTrails = trails;
        _configuredPoints = points;
        _store.Configure(trails, points);
        _states = new TrailStrandState[trails];
        _active = new int[trails];
        _activeIndex = new int[trails];
        _activeIndex.AsSpan().Fill(-1);
        _activeCount = 0;
        _slotOf.AsSpan().Fill(-1);
    }

    public override void InitializeNewParticles(int index, int count)
        => _slotOf.IncreaseCount(count).Fill(-1);

    public override void NewParticlesInitialized(int index, int count)
    {
        if (_configuredTrails == 0)
            return;

        var slots = _slotOf.Array;
        for (int i = 0; i < count; i++)
        {
            if (_store.FreeSlots == 0)
                return;
            if (!PassesDistribution())
                continue;
            if (!_store.TryAllocate(out int slot))
                return;

            int particle = index + i;
            ref var state = ref _states[slot];
            state.BaseColor = ColorInheritance == TrailInheritance.Birth
                ? Simulation.StartingColors[particle]
                : colorHDR.White;
            state.BaseWidth = WidthInheritance == TrailInheritance.Birth
                ? ParticleSizeAxisHelper.Resolve(Simulation.StartingSizes[particle], WidthAxis)
                : 1f;
            state.AgeScale = 1f;
            state.FadeRemaining = 0f;
            state.ParticleIndex = particle;

            for (int k = 0; k < _initializers.Count; k++)
                _initializers[k].InitializeTrail(this, particle, ref state);

            slots[particle] = slot;
            AddActive(slot);

            ResolveAppearance(in state, particle, out var color, out float width);
            _store.Push(slot, Simulation.RenderPositions[particle], color, width, Simulation.Time);
        }
    }

    public override void ParticlesDying(ReadOnlySpan<int> dyingIndexes)
    {
        var slots = _slotOf.Array;
        for (int i = 0; i < dyingIndexes.Length; i++)
        {
            int particle = dyingIndexes[i];
            int slot = slots[particle];
            if (slot < 0)
                continue;
            slots[particle] = -1;

            ref var state = ref _states[slot];
            state.ParticleIndex = -1;
            if (DieWithParticle || _store.CountOf(slot) < 2)
            {
                ReleaseTrail(slot);
                continue;
            }
            state.FadeRemaining = MathF.Max(DeathFadeTime, 0f);
        }
    }

    public override void ParticlesRemoved(ReadOnlySpan<ParticleMove> moves, int newCount)
    {
        _slotOf.ApplyCompaction(moves, newCount);
        var slots = _slotOf.Array;
        for (int i = 0; i < moves.Length; i++)
        {
            int slot = slots[moves[i].Dst];
            if (slot >= 0)
                _states[slot].ParticleIndex = moves[i].Dst;
        }
    }

    public override void TrimParticles(int newCount)
    {
        for (int a = _activeCount - 1; a >= 0; a--)
        {
            int slot = _active[a];
            ref var state = ref _states[slot];
            if (state.ParticleIndex < newCount)
                continue;
            state.ParticleIndex = -1;
            if (DieWithParticle || _store.CountOf(slot) < 2)
                ReleaseTrail(slot);
            else
                state.FadeRemaining = MathF.Max(DeathFadeTime, 0f);
        }
        _slotOf.TrimParticles(newCount);
        if (newCount < _slotOf.Capacity)
            _slotOf.AsSpan().Slice(newCount).Fill(-1);
    }

    public override void PostprocessUpdate(float deltaTime)
    {
        float now = Simulation.Time;
        float minDistanceSqr = MinVertexDistance * MinVertexDistance;
        float fadeTime = MathF.Max(DeathFadeTime, 0f);
        bool wholeTrail = ColorInheritance == TrailInheritance.WholeTrail
            || WidthInheritance == TrailInheritance.WholeTrail;

        for (int a = _activeCount - 1; a >= 0; a--)
        {
            int slot = _active[a];
            ref var state = ref _states[slot];
            int particle = state.ParticleIndex;

            if (particle >= 0)
            {
                AdvanceTip(slot, in state, particle, now, minDistanceSqr, wholeTrail);
            }
            else if (fadeTime > 0f)
            {
                state.FadeRemaining -= deltaTime;
                if (state.FadeRemaining <= 0f)
                {
                    ReleaseTrail(slot);
                    continue;
                }
            }

            ExpireTail(slot, now, MathF.Max(MaxPointAge * state.AgeScale, 1e-4f));

            if (particle < 0 && _store.CountOf(slot) < 2)
                ReleaseTrail(slot);
        }

        Publish(fadeTime);
    }

    private void AdvanceTip(int slot, in TrailStrandState state, int particle, float now, float minDistanceSqr, bool wholeTrail)
    {
        var position = Simulation.RenderPositions[particle];
        ResolveAppearance(in state, particle, out var color, out float width);

        int count = _store.CountOf(slot);
        bool commit;
        if (count == 0)
        {
            commit = true;
        }
        else if (count == 1)
        {
            // One point cannot be drawn. Commit the second as soon as the particle has moved at all,
            // so a trail appears the instant there is something to draw rather than one MinVertexDistance
            // later.
            commit = (position - _store.Position(_store.RawIndex(slot, 0))).LengthSquared > 0f;
        }
        else
        {
            // Measured from where the TIP currently sits, not from where the particle has just moved
            // to, because committing freezes the tip where it is and pushes a fresh one at the
            // particle. Testing the particle's new position instead commits a point that is a whole
            // simulation step short of MinVertexDistance, so a fast particle silently lays its trail
            // denser than it was told to. -xlinka
            commit = (_store.Position(_store.RawIndex(slot, 0)) - _store.Position(_store.RawIndex(slot, 1))).LengthSquared > minDistanceSqr;
        }

        if (commit)
        {
            _store.Push(slot, position, color, width, now);
        }
        else
        {
            int raw = _store.RawIndex(slot, 0);
            _store.Position(raw) = position;
            _store.Color(raw) = color;
            _store.Width(raw) = width;
            _store.Time(raw) = now;
        }

        if (!wholeTrail)
            return;

        int points = _store.CountOf(slot);
        for (int i = 1; i < points; i++)
        {
            int raw = _store.RawIndex(slot, i);
            if (ColorInheritance == TrailInheritance.WholeTrail)
                _store.Color(raw) = color;
            if (WidthInheritance == TrailInheritance.WholeTrail)
                _store.Width(raw) = width;
        }
    }

    private void ExpireTail(int slot, float now, float maxAge)
    {
        float cutoff = now - maxAge;
        while (_store.CountOf(slot) >= 2)
        {
            int count = _store.CountOf(slot);
            int oldest = _store.RawIndex(slot, count - 1);
            if (_store.Time(oldest) >= cutoff)
                return;

            int next = _store.RawIndex(slot, count - 2);
            float t1 = _store.Time(next);
            if (t1 <= cutoff)
            {
                _store.DropOldest(slot);
                continue;
            }

            // The tail point has expired but its neighbour has not, so instead of dropping a whole
            // segment slide the point along it until it sits exactly on the cutoff. That is the
            // difference between a tail that recedes and one that flickers.
            float t0 = _store.Time(oldest);
            float span = t1 - t0;
            float u = span > 1e-6f ? System.Math.Clamp((cutoff - t0) / span, 0f, 1f) : 1f;
            _store.Position(oldest) = float3.Lerp(_store.Position(oldest), _store.Position(next), u);
            _store.Color(oldest) = colorHDR.Lerp(_store.Color(oldest), _store.Color(next), u);
            _store.Width(oldest) = _store.Width(oldest) + (_store.Width(next) - _store.Width(oldest)) * u;
            _store.Time(oldest) = cutoff;
            return;
        }

        if (_store.CountOf(slot) == 1 && _store.Time(_store.RawIndex(slot, 0)) < cutoff)
            _store.DropOldest(slot);
    }

    private void Publish(float fadeTime)
    {
        var output = Output;
        output.Begin();
        output.UVMode = UVMode;
        output.Alignment = Alignment;
        output.AlignmentUp = AlignmentUp;
        output.TileLength = MathF.Max(TileLength, 1e-4f);
        output.Smoothing = System.Math.Clamp(Smoothing, 1, 32);

        var widthCurve = WidthOverTrail;
        var colorGradient = ColorOverTrail;
        bool hasWidthCurve = widthCurve != null && widthCurve.KeyCount > 0;
        bool hasColorGradient = colorGradient != null && colorGradient.KeyCount > 0;

        for (int a = 0; a < _activeCount; a++)
        {
            int slot = _active[a];
            int count = _store.CountOf(slot);
            if (count < 2)
                continue;

            ref var state = ref _states[slot];
            float fade = state.ParticleIndex < 0 && fadeTime > 0f
                ? System.Math.Clamp(state.FadeRemaining / fadeTime, 0f, 1f)
                : 1f;

            int start = output.PointCount;
            output.ReservePoints(count, out var positions, out var colors, out var widths);

            float inverse = 1f / (count - 1);
            float length = 0f;
            for (int i = 0; i < count; i++)
            {
                int raw = _store.RawIndex(slot, i);
                float t = i * inverse;
                var position = _store.Position(raw);
                positions[i] = position;

                var color = _store.Color(raw) * ColorScale;
                if (hasColorGradient)
                    color *= colorGradient!.Sample(t);
                color.a *= fade;
                colors[i] = color;

                float width = _store.Width(raw) * WidthScale;
                if (hasWidthCurve)
                    width *= widthCurve!.Sample(t);
                widths[i] = width;

                if (i > 0)
                    length += (position - positions[i - 1]).Length;
            }

            output.AddStrand(start, count, length);
        }

        output.Version++;
    }

    private void ResolveAppearance(in TrailStrandState state, int particle, out colorHDR color, out float width)
    {
        color = state.BaseColor;
        width = state.BaseWidth;
        if (ColorInheritance is TrailInheritance.Continuous or TrailInheritance.WholeTrail)
            color *= Simulation.RenderColors[particle];
        if (WidthInheritance is TrailInheritance.Continuous or TrailInheritance.WholeTrail)
            width *= ParticleSizeAxisHelper.Resolve(Simulation.RenderSizes[particle], WidthAxis);
    }

    private bool PassesDistribution()
    {
        float ratio = Ratio;
        if (ratio >= 1f)
            return true;
        if (!(ratio > 0f))
            return false;
        if (Distribution == StrandDistribution.Regular)
        {
            _distributionAccumulator += ratio;
            if (_distributionAccumulator < 1f)
                return false;
            _distributionAccumulator -= 1f;
            return true;
        }
        return Simulation.Random.Value < ratio;
    }

    private void ReleaseTrail(int slot)
    {
        RemoveActive(slot);
        _store.Release(slot);
    }

    private void AddActive(int slot)
    {
        _activeIndex[slot] = _activeCount;
        _active[_activeCount++] = slot;
    }

    private void RemoveActive(int slot)
    {
        int position = _activeIndex[slot];
        if (position < 0)
            return;
        int last = --_activeCount;
        int moved = _active[last];
        _active[position] = moved;
        _activeIndex[moved] = position;
        _activeIndex[slot] = -1;
    }
}

// Base for modules that set a trail's birth values. They run when the trail is allocated, not per
// frame, so stacking a handful of them costs nothing once the particle exists.
public abstract class TrailStrandInitializer : ParticleSimModule, ITrailStrandInitializer
{
    public override ParticleSimPhase Phase => ParticleSimPhase.Initializer;

    public override void SimulateChunk(int offset, int count, float deltaTime) { }

    public abstract void InitializeTrail(ParticleTrailsModule trails, int particleIndex, ref TrailStrandState state);
}

public sealed class TrailWidthConstantInitializer : TrailStrandInitializer
{
    public float Width = 1f;

    public override void InitializeTrail(ParticleTrailsModule trails, int particleIndex, ref TrailStrandState state)
        => state.BaseWidth *= Width;
}

public sealed class TrailWidthRangeInitializer : TrailStrandInitializer
{
    public float MinWidth = 0.5f;
    public float MaxWidth = 1f;

    public override void InitializeTrail(ParticleTrailsModule trails, int particleIndex, ref TrailStrandState state)
        => state.BaseWidth *= Simulation.Random.Range(MinWidth, MaxWidth);
}

public sealed class TrailColorConstantInitializer : TrailStrandInitializer
{
    public colorHDR Color = colorHDR.White;

    public override void InitializeTrail(ParticleTrailsModule trails, int particleIndex, ref TrailStrandState state)
        => state.BaseColor *= Color;
}

public sealed class TrailColorRangeInitializer : TrailStrandInitializer
{
    public colorHDR MinColor = colorHDR.White;
    public colorHDR MaxColor = colorHDR.White;

    public override void InitializeTrail(ParticleTrailsModule trails, int particleIndex, ref TrailStrandState state)
        => state.BaseColor *= Simulation.Random.Range(MinColor, MaxColor);
}

// Scales how long this one trail's points survive, against the module's MaxPointAge.
public sealed class TrailLifetimeConstantInitializer : TrailStrandInitializer
{
    public float Lifetime = 1f;

    public override void InitializeTrail(ParticleTrailsModule trails, int particleIndex, ref TrailStrandState state)
        => state.AgeScale *= Lifetime;
}

public sealed class TrailLifetimeRangeInitializer : TrailStrandInitializer
{
    public float MinLifetime = 0.5f;
    public float MaxLifetime = 1f;

    public override void InitializeTrail(ParticleTrailsModule trails, int particleIndex, ref TrailStrandState state)
        => state.AgeScale *= Simulation.Random.Range(MinLifetime, MaxLifetime);
}

// Ties trail length to how big the particle is, so a burst of mixed sizes gets proportionate streaks
// instead of every particle dragging the same tail regardless of scale.
public sealed class TrailLifetimeFromSizeInitializer : TrailStrandInitializer
{
    public float ReferenceSize = 1f;
    public ParticleSizeAxis Axis = ParticleSizeAxis.Average;

    public override void InitializeTrail(ParticleTrailsModule trails, int particleIndex, ref TrailStrandState state)
    {
        float reference = ReferenceSize;
        if (!(MathF.Abs(reference) > 1e-6f))
            return;
        float size = ParticleSizeAxisHelper.Resolve(Simulation.StartingSizes[particleIndex], Axis);
        float scale = size / reference;
        if (float.IsNaN(scale) || float.IsInfinity(scale))
            return;
        state.AgeScale *= MathF.Max(0f, scale);
    }
}
