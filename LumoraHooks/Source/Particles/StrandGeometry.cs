// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;

namespace Lumora.Godot.Hooks.Particles;

// Everything a strand needs to become a ribbon of quads, lifted off whatever published it. The
// particle path fills this from a ParticleStrandOutput; TrailRenderer fills it from its own sync
// fields. Neither one gets to own the geometry code.
public readonly struct StrandGeometryParams
{
    public readonly StrandUVMode UVMode;
    public readonly StrandAlignment Alignment;

    // Face normal of the band under VelocityAligned, in the same space as the points. Named "up" by
    // the simulation because that is what it usually is: a tyre mark with world up here lies flat on
    // the ground. It is not the offset direction - the offset is tangent x this. -xlinka
    public readonly float3 AlignmentUp;

    public readonly float TileLength;
    public readonly int Smoothing;

    public StrandGeometryParams(StrandUVMode uvMode, StrandAlignment alignment, in float3 alignmentUp, float tileLength, int smoothing)
    {
        UVMode = uvMode;
        Alignment = alignment;
        AlignmentUp = alignmentUp;
        TileLength = tileLength;
        Smoothing = smoothing;
    }

    public static StrandGeometryParams From(ParticleStrandOutput output) => new(
        output.UVMode,
        output.Alignment,
        output.AlignmentUp,
        output.TileLength,
        output.Smoothing);
}

// Where the builder puts what it produces. A struct implementation is devirtualised by the JIT, which
// is what lets the geometry be written straight into Godot's typed arrays without a staging copy and
// still be exercised by a test that has never seen a scene.
public interface IStrandVertexSink
{
    void Vertex(int index, in float3 position, in colorHDR color, float u, float v);

    void Triangle(int index, int a, int b, int c);
}

// Turns a strand's CONTROL points into a two-sided ribbon of quads.
//
// One of these per consumer, never shared: it holds the StrandSmoother, which is not thread safe, plus
// the scratch the expansion writes into. Both grow to the high-water mark and stop, so a steady-state
// frame allocates nothing here.
//
// Vertices come out in ring pairs - index 2i is the minus side of ring i, 2i+1 the plus side - so a
// consumer that wants to walk the ribbon's spine reads every other one. -xlinka
public sealed class StrandGeometryBuilder
{
    // Past this the spline is being sampled far finer than any strand is long and the vertex count is
    // the only thing still growing. The simulation suggests 1..32; this is the enforcement.
    public const int MaxSmoothing = 32;

    private const float Epsilon = 1e-6f;

    private readonly StrandSmoother _smoother = new();
    private float3[] _points = System.Array.Empty<float3>();
    private StrandSample[] _samples = System.Array.Empty<StrandSample>();
    private float[] _arc = System.Array.Empty<float>();

    public StrandSmoother Smoother => _smoother;

    // Vertices and indices the strands WILL need, so a consumer can size its buffers before the build.
    // The build can come in under this when a strand collapses; it never comes in over.
    public static void Measure(ReadOnlySpan<StrandRange> strands, int strandCount, int smoothing, out int vertexCount, out int indexCount)
    {
        vertexCount = 0;
        indexCount = 0;

        int steps = System.Math.Clamp(smoothing, 1, MaxSmoothing);
        int count = System.Math.Min(strandCount, strands.Length);
        for (int i = 0; i < count; i++)
        {
            int points = strands[i].Count;
            if (points < 2)
                continue;
            int verts = StrandSmoother.VertexCount(points, steps);
            vertexCount += verts * 2;
            indexCount += (verts - 1) * 6;
        }
    }

    public void Build<TSink>(
        ReadOnlySpan<float3> positions,
        ReadOnlySpan<colorHDR> colors,
        ReadOnlySpan<float> widths,
        ReadOnlySpan<StrandRange> strands,
        int strandCount,
        in StrandGeometryParams parameters,
        in float3 viewPosition,
        ref TSink sink,
        out int vertexCount,
        out int indexCount)
        where TSink : IStrandVertexSink
    {
        vertexCount = 0;
        indexCount = 0;

        int steps = System.Math.Clamp(parameters.Smoothing, 1, MaxSmoothing);
        float tileLength = parameters.TileLength > Epsilon ? parameters.TileLength : 1f;
        int count = System.Math.Min(strandCount, strands.Length);

        var up = parameters.AlignmentUp;
        if (up.LengthSquared < Epsilon)
            up = float3.Up;

        for (int s = 0; s < count; s++)
        {
            var range = strands[s];
            if (range.Count < 2 || range.Start < 0 || range.Start + range.Count > positions.Length)
                continue;

            int wanted = StrandSmoother.VertexCount(range.Count, steps);
            EnsureScratch(wanted);

            var control = positions.Slice(range.Start, range.Count);
            int written = _smoother.Expand(control, wanted, _points, _samples);
            if (written < 2)
                continue;

            // Arc of the SMOOTHED polyline, not the control polyline the range carries. The two differ
            // wherever the spline bulges away from its control points, and the one the texture has to
            // agree with is the one being drawn. -xlinka
            float total = 0f;
            _arc[0] = 0f;
            for (int i = 1; i < written; i++)
            {
                total += (_points[i] - _points[i - 1]).Length;
                _arc[i] = total;
            }

            float inverseTotal = total > Epsilon ? 1f / total : 0f;
            int baseVertex = vertexCount;
            var side = float3.Zero;
            bool haveSide = false;

            for (int i = 0; i < written; i++)
            {
                var point = _points[i];
                var tangent = i == 0
                    ? _points[1] - _points[0]
                    : (i == written - 1 ? _points[i] - _points[i - 1] : _points[i + 1] - _points[i - 1]);

                var reference = parameters.Alignment == StrandAlignment.CameraFacing
                    ? viewPosition - point
                    : up;

                var offset = float3.Cross(tangent, reference);
                if (offset.LengthSquared > Epsilon)
                {
                    side = offset.Normalized;
                    haveSide = true;
                }
                else if (!haveSide)
                {
                    // First ring, and the strand is pointing straight at the viewer (or straight along
                    // the up axis). Any perpendicular will do; the next ring that is not degenerate
                    // takes over and the seam is a vertex wide.
                    side = Perpendicular(tangent);
                    haveSide = true;
                }

                var sample = _samples[i];
                int segment = System.Math.Clamp(sample.Segment, 0, range.Count - 2);
                float t = System.Math.Clamp(sample.T, 0f, 1f);
                int lo = range.Start + segment;
                int hi = lo + 1;

                float width = widths.Length > hi ? widths[lo] + (widths[hi] - widths[lo]) * t : 0f;
                var color = colors.Length > hi ? colorHDR.Lerp(colors[lo], colors[hi], t) : colorHDR.White;

                float half = width * 0.5f;
                float u = parameters.UVMode == StrandUVMode.StretchAlongStrand
                    ? _arc[i] * inverseTotal
                    : _arc[i] / tileLength;

                sink.Vertex(vertexCount, point - side * half, color, u, 0f);
                sink.Vertex(vertexCount + 1, point + side * half, color, u, 1f);
                vertexCount += 2;
            }

            for (int i = 0; i < written - 1; i++)
            {
                int a = baseVertex + i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                sink.Triangle(indexCount, a, b, c);
                sink.Triangle(indexCount + 3, b, d, c);
                indexCount += 6;
            }
        }
    }

    private void EnsureScratch(int vertices)
    {
        if (_points.Length >= vertices)
            return;
        int size = System.Math.Max(vertices, _points.Length * 2);
        _points = new float3[size];
        _samples = new StrandSample[size];
        _arc = new float[size];
    }

    private static float3 Perpendicular(in float3 direction)
    {
        if (direction.LengthSquared < Epsilon)
            return float3.Right;
        var axis = MathF.Abs(direction.Normalized.y) < 0.9f ? float3.Up : float3.Right;
        var result = float3.Cross(direction, axis);
        return result.LengthSquared > Epsilon ? result.Normalized : float3.Right;
    }
}

// Whether the ribbon has to be rebuilt this frame.
//
// The published Version alone is not enough and treating it as enough is a real bug: a camera-facing
// strand's vertices are a function of where the viewer is standing, so a system that has finished
// emitting - version frozen - would weld its ribbons to the last viewpoint and turn edge-on as you
// walk round it. Velocity-aligned strands genuinely only depend on the version. -xlinka
public struct StrandRebuildGate
{
    // Roughly a millimetre. Below it the twist is under a pixel and the rebuild is pure cost.
    private const float ViewEpsilonSquared = 1e-6f;

    private int _version;
    private float3 _view;
    private bool _primed;

    public bool ShouldRebuild(int version, in float3 viewPosition, bool cameraFacing)
    {
        if (!_primed || version != _version)
        {
            _primed = true;
            _version = version;
            _view = viewPosition;
            return true;
        }

        if (!cameraFacing)
            return false;

        if ((viewPosition - _view).LengthSquared <= ViewEpsilonSquared)
            return false;

        _view = viewPosition;
        return true;
    }

    public void Reset()
    {
        _primed = false;
        _version = 0;
        _view = float3.Zero;
    }
}
