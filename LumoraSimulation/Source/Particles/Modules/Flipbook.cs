// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Simulation.Particles.Modules;

public enum FlipbookLoopMode
{
    // Plays through once and holds the last frame.
    Once,

    // Wraps back to the first frame.
    Loop,

    // Runs to the end and back, so the sequence reads as continuous without the author authoring the
    // return leg.
    PingPong,
}

public enum FlipbookDrive
{
    // The sequence is stretched across the particle's lifetime, so every particle finishes its
    // animation exactly as it dies however long it lives.
    Lifetime,

    // The sequence advances at a fixed frame rate regardless of lifetime, which is what a real film
    // loop does and what a smoke puff sheet is authored against.
    Rate,
}

// Per-particle frame position in a texture sheet, published as a FRACTIONAL frame rather than an
// integer index.
//
// The fraction is the whole point. An integer frame steps, and a sheet stepping at 12 fps in front of
// a 90 fps headset is a strobe; handing the renderer 7.35 lets it draw frame 7 and frame 8 and cross
// fade them by 0.35, which turns the same sheet into continuous motion. The renderer that only wants
// the integer just truncates. -xlinka
public sealed class ParticleFlipbookModule : ParticleSimModule
{
    private readonly ParticleBuffer<float> _frames = new();
    private readonly ParticleBuffer<int> _startFrames = new();

    private int _resolvedFrameCount = 1;
    private float _cycleFrames = 1f;

    public int Columns = 1;
    public int Rows = 1;

    // 0 uses the whole sheet. Set it when the last row is partly empty.
    public int FrameCount;

    // Rate drive only.
    public float FramesPerSecond = 12f;

    // Lifetime drive only: how many times through the sheet across one particle's life.
    public float CycleCount = 1f;

    public int StartFrame;

    // Scatters the starting frame across the sheet so a burst does not animate in lockstep.
    public bool RandomStartFrame;

    public FlipbookLoopMode LoopMode = FlipbookLoopMode.Loop;
    public FlipbookDrive Drive = FlipbookDrive.Lifetime;

    public override ParticleSimPhase Phase => ParticleSimPhase.Appearance;

    public override bool AllowMultipleInstances => false;

    // Runs first in the newborn pass so a start-frame initializer has a column to write into.
    public override int InitPriority => -100;

    // Continuous frame position per particle, always in [0, FrameCount). Floor it for the frame to
    // draw, take the fraction as the blend to the NEXT frame, and wrap that next index modulo
    // FrameCount.
    public Span<float> Frames => _frames.AsSpan();

    // Per-particle offset into the sheet, applied on top of the animation. Initializers write here.
    public Span<int> StartFrames => _startFrames.AsSpan();

    // Raw backing array of Frames, for a renderer that holds onto it between calls. Valid entries are
    // [0, the simulation's ParticleCount).
    public float[] FrameArray => _frames.Array;

    // Frames actually in play after Columns, Rows and FrameCount are reconciled. Never below 1.
    public int ResolvedFrameCount => _resolvedFrameCount;

    public int ResolvedColumns => System.Math.Max(1, Columns);

    public int ResolvedRows => System.Math.Max(1, Rows);

    public override void PrepareUpdate(float deltaTime)
    {
        int total = ResolvedColumns * ResolvedRows;
        _resolvedFrameCount = FrameCount > 0 ? System.Math.Min(FrameCount, total) : total;
        if (_resolvedFrameCount < 1)
            _resolvedFrameCount = 1;
        _cycleFrames = _resolvedFrameCount * MathF.Max(CycleCount, 0f);
    }

    public override void InitializeNewParticles(int index, int count)
    {
        _frames.IncreaseCount(count).Fill(0f);
        var starts = _startFrames.IncreaseCount(count);

        int total = ResolvedColumns * ResolvedRows;
        int frames = FrameCount > 0 ? System.Math.Min(FrameCount, total) : total;
        if (frames < 1)
            frames = 1;

        if (!RandomStartFrame)
        {
            starts.Fill(StartFrame);
            return;
        }
        var random = Simulation.Random;
        for (int i = 0; i < starts.Length; i++)
            starts[i] = StartFrame + random.Range(0, frames);
    }

    public override void SimulateChunk(int offset, int count, float deltaTime)
    {
        var frames = _frames.Slice(offset, count);
        var starts = _startFrames.Slice(offset, count);
        int total = _resolvedFrameCount;
        float totalF = total;

        if (Drive == FlipbookDrive.Lifetime)
        {
            var progression = Simulation.NormalizedProgressions.Slice(offset, count);
            for (int i = 0; i < count; i++)
                frames[i] = Resolve(progression[i] * _cycleFrames, starts[i], total, totalF);
            return;
        }

        var lifetimes = Simulation.CurrentLifetimes.Slice(offset, count);
        var starting = Simulation.StartingLifetimes.Slice(offset, count);
        float rate = MathF.Max(FramesPerSecond, 0f);
        for (int i = 0; i < count; i++)
            frames[i] = Resolve((starting[i] - lifetimes[i]) * rate, starts[i], total, totalF);
    }

    private float Resolve(float raw, int start, int total, float totalF)
    {
        if (raw < 0f)
            raw = 0f;

        float frame;
        switch (LoopMode)
        {
            case FlipbookLoopMode.Once:
                // Stops exactly on the last frame, so the fraction is 0 there and the renderer's blend
                // target - which wraps to frame 0 - contributes nothing. Landing past it would flash
                // the start of the sheet at the end of the animation.
                frame = MathF.Min(raw, totalF - 1f);
                break;
            case FlipbookLoopMode.PingPong:
                if (total <= 1)
                {
                    frame = 0f;
                    break;
                }
                float period = (total - 1) * 2f;
                float wrapped = raw - MathF.Floor(raw / period) * period;
                frame = wrapped <= total - 1 ? wrapped : period - wrapped;
                break;
            default:
                frame = raw - MathF.Floor(raw / totalF) * totalF;
                break;
        }

        if (start != 0)
        {
            frame += start;
            frame -= MathF.Floor(frame / totalF) * totalF;
        }

        // Floating point can land exactly on the count after the subtraction above; clamping here is
        // cheaper than making every consumer guard an out-of-range frame index.
        if (frame >= totalF)
            frame = 0f;
        return frame < 0f ? 0f : frame;
    }

    public override void ParticlesRemoved(ReadOnlySpan<ParticleMove> moves, int newCount)
    {
        _frames.ApplyCompaction(moves, newCount);
        _startFrames.ApplyCompaction(moves, newCount);
    }

    public override void TrimParticles(int newCount)
    {
        _frames.TrimParticles(newCount);
        _startFrames.TrimParticles(newCount);
    }
}

// Scatters the starting frame across a range, for sheets where only part of the sheet is a sensible
// place to begin.
public sealed class FlipbookStartFrameInitializer : ParticleValueInitializer<int>
{
    public int MinFrame;
    public int MaxFrame = 1;

    private ParticleFlipbookModule? _flipbook;

    protected override Span<int> Buffer => _flipbook != null ? _flipbook.StartFrames : default;

    public override void ModulesUpdated()
    {
        base.ModulesUpdated();
        _flipbook = Simulation.TryGetModule<ParticleFlipbookModule>();
    }

    protected override void InitializeValues(Span<int> data)
    {
        var random = Simulation.Random;
        int min = MinFrame;
        int max = System.Math.Max(min, MaxFrame);
        for (int i = 0; i < data.Length; i++)
            data[i] = min == max ? min : random.Range(min, max + 1);
    }
}
