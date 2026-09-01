// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

public enum RibbonTimeSplitMode
{
    // The clock runs from the first particle of the current ribbon, so ribbons are cut at a fixed
    // cadence however fast particles arrive.
    SinceRibbonStart,

    // The clock resets on every particle, so a ribbon is cut only by a GAP in emission. This is the one
    // that turns a stuttering emitter into separate streaks instead of one long one.
    SinceLastParticle,
}

// Decides where one ribbon ends and the next begins. Splitters are polled for every particle that
// joins a ribbon, whether or not something else already called for a cut, because they carry state -
// skipping the poll would leave a sequence counter or a timestamp stale and shift every later cut.
public interface IRibbonSplitter
{
    bool ShouldSplit(ParticleRibbonsModule ribbons, int particleIndex);
}

// Threads LIVE particles onto ribbons in emission order.
//
// Unlike a trail, a ribbon has no history: every control point IS a particle, right now, and the
// strand is rebuilt from scratch each frame. Move the particles and the whole ribbon moves with them,
// which is what makes ribbons the right tool for a banner, a lightning arc or a stream of energy, and
// the wrong one for a streak that has to stay where it was drawn.
//
// Membership is decided once, at birth: which ribbon a particle belongs to and its place in the
// sequence. Everything after that is grouping and ordering, so a particle can never jump ribbons
// halfway through its life. -xlinka
public sealed class ParticleRibbonsModule : ParticleSimModule
{
    public const int RibbonLimit = 1024;
    public const int RibbonLengthLimit = 512;

    private struct RibbonMembership
    {
        public int RibbonId;
        public int Sequence;
    }

    private struct RibbonEntry
    {
        public int RibbonId;
        public int Sequence;
        public int Particle;
    }

    private readonly ParticleBuffer<RibbonMembership> _membership = new();
    private readonly List<IRibbonSplitter> _splitters = new();

    private RibbonEntry[] _entries = System.Array.Empty<RibbonEntry>();
    private int _currentRibbonId;
    private int _currentLength;
    private int _currentSequence;
    private float _distributionAccumulator;

    // Hard ceiling on published ribbons. Past it the OLDEST ribbons are dropped, because the newest is
    // the one the effect is currently drawing attention to.
    public int MaxRibbons = 64;

    // Particles per ribbon. Reaching it cuts the ribbon exactly as a splitter would.
    public int MaxRibbonLength = 64;

    public float Ratio = 1f;
    public StrandDistribution Distribution = StrandDistribution.Random;

    public bool UseParticleColor = true;
    public bool UseParticleSize = true;
    public ParticleSizeAxis WidthAxis = ParticleSizeAxis.Average;

    // Sampled 0 at the newest particle in the ribbon to 1 at the oldest.
    public FloatCurve? WidthOverRibbon;
    public ColorGradient? ColorOverRibbon;

    public float WidthScale = 1f;
    public colorHDR ColorScale = colorHDR.White;

    public StrandUVMode UVMode = StrandUVMode.StretchAlongStrand;
    public StrandAlignment Alignment = StrandAlignment.CameraFacing;
    public float3 AlignmentUp = float3.Up;
    public float TileLength = 1f;
    public int Smoothing = 4;

    public override ParticleSimPhase Phase => ParticleSimPhase.Output;

    public override bool AllowMultipleInstances => false;

    public override void SimulateChunk(int offset, int count, float deltaTime) { }

    public ParticleStrandOutput Output { get; } = new();

    public int RibbonCount => Output.StrandCount;

    // Id of the ribbon particles emitted right now would join. Increments on every cut.
    public int CurrentRibbonId => _currentRibbonId;

    public override void ModulesUpdated()
    {
        base.ModulesUpdated();
        _splitters.Clear();
        for (int i = 0; i < Simulation.ModuleCount; i++)
        {
            var module = Simulation.GetModule(i);
            if (module.IsActive && module is IRibbonSplitter splitter)
                _splitters.Add(splitter);
        }
    }

    public override void InitializeNewParticles(int index, int count)
        => _membership.IncreaseCount(count).Fill(new RibbonMembership { RibbonId = -1 });

    public override void NewParticlesInitialized(int index, int count)
    {
        var membership = _membership.Array;
        int maxLength = System.Math.Clamp(MaxRibbonLength, 2, RibbonLengthLimit);

        for (int i = 0; i < count; i++)
        {
            int particle = index + i;
            if (!PassesDistribution())
            {
                membership[particle].RibbonId = -1;
                continue;
            }

            bool split = _currentLength >= maxLength;
            for (int s = 0; s < _splitters.Count; s++)
            {
                if (_splitters[s].ShouldSplit(this, particle))
                    split = true;
            }

            // A cut before the first particle of a ribbon would leave an empty one behind and shift
            // every id by one for no visible reason.
            if (split && _currentLength > 0)
            {
                _currentRibbonId++;
                _currentLength = 0;
                _currentSequence = 0;
            }

            membership[particle].RibbonId = _currentRibbonId;
            membership[particle].Sequence = _currentSequence++;
            _currentLength++;
        }
    }

    public override void ParticlesRemoved(ReadOnlySpan<ParticleMove> moves, int newCount)
        => _membership.ApplyCompaction(moves, newCount);

    public override void TrimParticles(int newCount) => _membership.TrimParticles(newCount);

    public override void PostprocessUpdate(float deltaTime)
    {
        int live = Simulation.ParticleCount;
        if (_entries.Length < live)
            _entries = new RibbonEntry[System.Math.Max(live, _entries.Length * 2)];

        var membership = _membership.Array;
        int count = 0;
        for (int i = 0; i < live; i++)
        {
            if (membership[i].RibbonId < 0)
                continue;
            _entries[count].RibbonId = membership[i].RibbonId;
            _entries[count].Sequence = membership[i].Sequence;
            _entries[count].Particle = i;
            count++;
        }

        Sort(_entries, count);
        Publish(count);
    }

    private void Publish(int count)
    {
        var output = Output;
        output.Begin();
        output.UVMode = UVMode;
        output.Alignment = Alignment;
        output.AlignmentUp = AlignmentUp;
        output.TileLength = MathF.Max(TileLength, 1e-4f);
        output.Smoothing = System.Math.Clamp(Smoothing, 1, 32);
        output.Version++;

        if (count == 0)
            return;

        int maxRibbons = System.Math.Clamp(MaxRibbons, 1, RibbonLimit);
        int maxLength = System.Math.Clamp(MaxRibbonLength, 2, RibbonLengthLimit);

        // Runs are already in ascending ribbon id, so the newest ribbons are at the end. Count the
        // drawable ones first and skip from the front, which drops the oldest.
        int drawable = 0;
        for (int i = 0; i < count;)
        {
            int end = RunEnd(i, count);
            if (end - i >= 2)
                drawable++;
            i = end;
        }
        int skip = System.Math.Max(0, drawable - maxRibbons);

        var widthCurve = WidthOverRibbon;
        var colorGradient = ColorOverRibbon;
        bool hasWidthCurve = widthCurve != null && widthCurve.KeyCount > 0;
        bool hasColorGradient = colorGradient != null && colorGradient.KeyCount > 0;

        var renderPositions = Simulation.RenderPositions;
        var renderColors = Simulation.RenderColors;
        var renderSizes = Simulation.RenderSizes;

        for (int i = 0; i < count;)
        {
            int end = RunEnd(i, count);
            int length = end - i;
            if (length < 2)
            {
                i = end;
                continue;
            }
            if (skip > 0)
            {
                skip--;
                i = end;
                continue;
            }

            int points = System.Math.Min(length, maxLength);
            int start = output.PointCount;
            output.ReservePoints(points, out var positions, out var colors, out var widths);

            float inverse = 1f / (points - 1);
            float arc = 0f;
            for (int j = 0; j < points; j++)
            {
                int particle = _entries[i + j].Particle;
                float t = j * inverse;

                var position = renderPositions[particle];
                positions[j] = position;

                var color = ColorScale;
                if (UseParticleColor)
                    color *= renderColors[particle];
                if (hasColorGradient)
                    color *= colorGradient!.Sample(t);
                colors[j] = color;

                float width = WidthScale;
                if (UseParticleSize)
                    width *= ParticleSizeAxisHelper.Resolve(renderSizes[particle], WidthAxis);
                if (hasWidthCurve)
                    width *= widthCurve!.Sample(t);
                widths[j] = width;

                if (j > 0)
                    arc += (position - positions[j - 1]).Length;
            }

            output.AddStrand(start, points, arc);
            i = end;
        }
    }

    private int RunEnd(int start, int count)
    {
        int id = _entries[start].RibbonId;
        int i = start + 1;
        while (i < count && _entries[i].RibbonId == id)
            i++;
        return i;
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

    // Heapsort rather than Array.Sort: in place, no recursion, no comparer object, and therefore no
    // allocation on a path that runs every frame for the life of the effect. -xlinka
    private static void Sort(RibbonEntry[] entries, int count)
    {
        for (int i = count / 2 - 1; i >= 0; i--)
            SiftDown(entries, i, count);
        for (int end = count - 1; end > 0; end--)
        {
            (entries[0], entries[end]) = (entries[end], entries[0]);
            SiftDown(entries, 0, end);
        }
    }

    private static void SiftDown(RibbonEntry[] entries, int root, int count)
    {
        while (true)
        {
            int child = root * 2 + 1;
            if (child >= count)
                return;
            if (child + 1 < count && Precedes(entries[child], entries[child + 1]))
                child++;
            if (!Precedes(entries[root], entries[child]))
                return;
            (entries[root], entries[child]) = (entries[child], entries[root]);
            root = child;
        }
    }

    // Ribbon id ascending, then sequence DESCENDING, so each run comes out newest particle first and
    // point 0 of a published ribbon is its freshest end - the same convention trails use.
    private static bool Precedes(in RibbonEntry a, in RibbonEntry b)
    {
        if (a.RibbonId != b.RibbonId)
            return a.RibbonId < b.RibbonId;
        return a.Sequence > b.Sequence;
    }
}

public abstract class RibbonSplitterModule : ParticleSimModule, IRibbonSplitter
{
    public override ParticleSimPhase Phase => ParticleSimPhase.Initializer;

    public override void SimulateChunk(int offset, int count, float deltaTime) { }

    public abstract bool ShouldSplit(ParticleRibbonsModule ribbons, int particleIndex);
}

// Cuts after a run of particles, its length drawn per ribbon from a range. Set both bounds the same
// for exact fixed-length ribbons.
public sealed class SequenceRibbonSplitter : RibbonSplitterModule
{
    public int MinCount = 5;
    public int MaxCount = 10;

    private int _remaining;

    public override bool ShouldSplit(ParticleRibbonsModule ribbons, int particleIndex)
    {
        if (_remaining > 0)
        {
            _remaining--;
            return false;
        }
        int min = System.Math.Max(1, MinCount);
        int max = System.Math.Max(min, MaxCount);
        int length = min == max ? min : Simulation.Random.Range(min, max + 1);
        _remaining = length - 1;
        return true;
    }
}

// Cuts on a time boundary. Which boundary depends on the mode: a fixed cadence, or a gap in emission.
public sealed class TimeProximityRibbonSplitter : RibbonSplitterModule
{
    public float Interval = 0.25f;
    public RibbonTimeSplitMode Mode = RibbonTimeSplitMode.SinceRibbonStart;

    private float _lastTime = float.NegativeInfinity;

    public override bool ShouldSplit(ParticleRibbonsModule ribbons, int particleIndex)
    {
        float now = Simulation.Time;
        if (now - _lastTime >= Interval)
        {
            _lastTime = now;
            return true;
        }
        if (Mode == RibbonTimeSplitMode.SinceLastParticle)
            _lastTime = now;
        return false;
    }
}
