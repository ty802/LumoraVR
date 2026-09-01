// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles;

// Which component of a particle's size feeds a scalar the strand modules need (a trail width, a light
// range). Kept to the six that are actually reached for; the full 3x6 cross product of axis pairs is a
// menu nobody navigates.
public enum ParticleSizeAxis
{
    X,
    Y,
    Z,
    Average,
    Max,
    Min,
}

public static class ParticleSizeAxisHelper
{
    public static float Resolve(in float3 size, ParticleSizeAxis axis) => axis switch
    {
        ParticleSizeAxis.X => size.x,
        ParticleSizeAxis.Y => size.y,
        ParticleSizeAxis.Z => size.z,
        ParticleSizeAxis.Max => MathF.Max(size.x, MathF.Max(size.y, size.z)),
        ParticleSizeAxis.Min => MathF.Min(size.x, MathF.Min(size.y, size.z)),
        _ => (size.x + size.y + size.z) * (1f / 3f),
    };
}

// How the renderer should lay UVs along a strand.
public enum StrandUVMode
{
    // U runs 0 to 1 across the whole strand, so one texture is stretched end to end however long it is.
    StretchAlongStrand,

    // U advances by world length, so the texture repeats at a fixed physical size and a longer strand
    // simply shows more repeats. TileLength on the output is the world size of one repeat.
    TilePerSegment,
}

// How the renderer should orient the ribbon of quads it builds along a strand. The simulation never
// resolves this - it has no camera and no view matrix - it just carries the author's choice through.
public enum StrandAlignment
{
    // Twist each segment so its face points at the viewer. Right for smoke, energy, generic streaks.
    CameraFacing,

    // Keep the strand flat in the plane defined by its own direction and the reference up axis. Right
    // for anything that reads as a physical flat band: a banner, a sword arc, a tyre mark.
    VelocityAligned,
}

// One drawable strand inside the flat point arrays of a ParticleStrandOutput. Points are contiguous:
// [Start, Start + Count). Point Start is the HEAD - the end at the particle for a trail, the newest
// particle for a ribbon - and the run walks backwards in time from there.
//
// Length is the arc length of the raw control polyline in simulation space, precomputed here because
// the renderer needs it for tiled UVs and would otherwise walk every point a second time to get it.
public readonly struct StrandRange
{
    public readonly int Start;
    public readonly int Count;
    public readonly float Length;

    public StrandRange(int start, int count, float length)
    {
        Start = start;
        Count = count;
        Length = length;
    }
}

// The finished frame of strand geometry a renderer consumes, in the owning system's simulation-local
// space. Struct-of-arrays and flat: one allocation-free pass over Strands, and for each of those one
// contiguous run of Positions/Colors/Widths. Arrays rather than spans because the consumer is a render
// hook that holds onto them between calls.
//
// These are CONTROL points, not vertices. Feed them through StrandSmoother to get the smoothed,
// evenly-spaced vertex ring the strand is actually drawn with. -xlinka
public sealed class ParticleStrandOutput
{
    private readonly ParticleBuffer<StrandRange> _strands = new();
    private readonly ParticleBuffer<float3> _positions = new();
    private readonly ParticleBuffer<colorHDR> _colors = new();
    private readonly ParticleBuffer<float> _widths = new();

    // Number of valid entries in Strands.
    public int StrandCount => _strands.Count;

    // Number of valid entries in Positions / Colors / Widths.
    public int PointCount => _positions.Count;

    // Valid entries are [0, StrandCount). The array itself is longer; do not read past the count.
    public StrandRange[] Strands => _strands.Array;

    // Valid entries are [0, PointCount), addressed through a StrandRange.
    public float3[] Positions => _positions.Array;

    public colorHDR[] Colors => _colors.Array;

    // World-space diameter of the strand at that point, already through width-over-strand and every
    // inheritance and initializer the author stacked on. The renderer offsets by half this.
    public float[] Widths => _widths.Array;

    // Bumped once per published frame. A renderer that has already drawn this version can skip.
    public int Version { get; internal set; }

    public StrandUVMode UVMode { get; internal set; }

    public StrandAlignment Alignment { get; internal set; }

    // Reference up axis for VelocityAligned, in simulation space. Ignored when camera facing.
    public float3 AlignmentUp { get; internal set; } = float3.Up;

    // World length of one texture repeat under TilePerSegment. Ignored when stretching.
    public float TileLength { get; internal set; } = 1f;

    // Control points the renderer should expand each raw point into. 1 draws the raw polyline.
    public int Smoothing { get; internal set; } = 1;

    internal void Begin()
    {
        _strands.Clear();
        _positions.Clear();
        _colors.Clear();
        _widths.Clear();
    }

    internal void ReservePoints(int count, out Span<float3> positions, out Span<colorHDR> colors, out Span<float> widths)
    {
        positions = _positions.IncreaseCount(count);
        colors = _colors.IncreaseCount(count);
        widths = _widths.IncreaseCount(count);
    }

    internal void AddStrand(int start, int count, float length)
        => _strands.IncreaseCount(1)[0] = new StrandRange(start, count, length);

    internal void EnsureCapacity(int strands, int points)
    {
        _strands.EnsureCapacity(strands);
        _positions.EnsureCapacity(points);
        _colors.EnsureCapacity(points);
        _widths.EnsureCapacity(points);
    }
}
