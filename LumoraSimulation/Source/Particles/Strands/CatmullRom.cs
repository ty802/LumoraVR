// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles;

// Where along a strand's CONTROL polyline a smoothed sample landed. Segment is the index of the
// control point the segment starts at, T is 0 to 1 inside it. Everything that is not geometry - the
// colour, the width - is interpolated linearly at exactly this location, so a smoothed strand's
// shading lines up with its shape instead of sliding along it. -xlinka
public readonly struct StrandSample
{
    public readonly int Segment;
    public readonly float T;

    public StrandSample(int segment, float t)
    {
        Segment = segment;
        T = t;
    }
}

// Centripetal Catmull-Rom through a strand's control points.
//
// Centripetal (the knot spacing is the square root of the chord length) rather than uniform, because
// uniform Catmull-Rom self-intersects and loops back on itself wherever two control points sit close
// together next to a distant third - which is exactly what a trail does every time a particle slows
// down or turns sharply, so the uniform variant fails on the one case trails are made of.
//
// The curve passes exactly through every control point, and the two end segments use mirrored phantom
// points so the ends stay put instead of being pulled inward. -xlinka
public static class CatmullRom
{
    // Knot exponent. 0.5 is centripetal, 1 is chordal, 0 degenerates to uniform.
    private const float Alpha = 0.5f;

    private const float Epsilon = 1e-7f;

    // Position on the segment from p1 to p2, with p0 and p3 as the neighbouring control points.
    // t is 0 at p1 and 1 at p2.
    public static float3 Evaluate(in float3 p0, in float3 p1, in float3 p2, in float3 p3, float t)
    {
        float t0 = 0f;
        float t1 = t0 + Knot(p0, p1);
        float t2 = t1 + Knot(p1, p2);
        float t3 = t2 + Knot(p2, p3);

        float x = t1 + (t2 - t1) * System.Math.Clamp(t, 0f, 1f);

        var a1 = Blend(p0, p1, t0, t1, x);
        var a2 = Blend(p1, p2, t1, t2, x);
        var a3 = Blend(p2, p3, t2, t3, x);
        var b1 = Blend(a1, a2, t0, t2, x);
        var b2 = Blend(a2, a3, t1, t3, x);
        return Blend(b1, b2, t1, t2, x);
    }

    // Position on segment `segment` of a control polyline, t from 0 to 1. Handles the ends by
    // mirroring, so segment 0 and the last segment behave like any other.
    public static float3 Evaluate(ReadOnlySpan<float3> control, int segment, float t)
    {
        int last = control.Length - 1;
        if (control.Length == 0)
            return float3.Zero;
        if (control.Length == 1)
            return control[0];

        segment = System.Math.Clamp(segment, 0, last - 1);
        var p1 = control[segment];
        var p2 = control[segment + 1];

        // Two points is a straight line and nothing else. Running the spline over two mirrored phantoms
        // gives the same answer but through six divisions that can all go degenerate.
        if (control.Length == 2)
            return float3.Lerp(p1, p2, System.Math.Clamp(t, 0f, 1f));

        var p0 = segment > 0 ? control[segment - 1] : p1 + (p1 - p2);
        var p3 = segment + 2 <= last ? control[segment + 2] : p2 + (p2 - p1);
        return Evaluate(p0, p1, p2, p3, t);
    }

    private static float Knot(in float3 a, in float3 b)
    {
        float distance = (b - a).Length;
        // A zero-length knot span collapses the whole Barry-Goldman ladder into 0/0. Coincident control
        // points are normal (a stalled particle re-lays its tip in place), so clamp rather than reject.
        return MathF.Max(MathF.Pow(distance, Alpha), Epsilon);
    }

    private static float3 Blend(in float3 a, in float3 b, float ta, float tb, float x)
    {
        float span = tb - ta;
        if (span < Epsilon)
            return b;
        float u = (x - ta) / span;
        return a * (1f - u) + b * u;
    }
}

// Expands a strand's control points into a fixed number of smoothed vertices spaced EVENLY BY ARC
// LENGTH, which is what makes a trail's texture stop sliding and bunching wherever the particle sped
// up or slowed down. Subdividing in the parameter instead is far cheaper and looks fine on a straight
// trail, which is why ExpandByParameter is still here for the renderer to fall back on.
//
// Owns its scratch and grows it to the high-water mark, so a renderer holds ONE of these and expands
// every strand through it without allocating after the first few frames. Not thread safe: one per
// consumer, or one per worker. -xlinka
public sealed class StrandSmoother
{
    private float[] _cumulative = System.Array.Empty<float>();
    private int _tableCount;

    // Flattening steps per control segment used to measure arc length. Higher is more exact spacing on
    // tightly curved strands and costs one spline evaluation each.
    public int FlattenSteps { get; set; } = 8;

    // Arc length of the smoothed curve measured by the last Expand call.
    public float LastLength { get; private set; }

    // Fills positions and samples with outputCount vertices evenly spaced along the smoothed curve
    // through control. Returns how many were written, which is 0 for an empty control set.
    //
    // The first and last outputs are pinned exactly on the first and last control points - an
    // interpolated endpoint that drifts by a rounding error is a trail whose tip detaches from its
    // particle, and that reads as a bug from across the room.
    public int Expand(ReadOnlySpan<float3> control, int outputCount, Span<float3> positions, Span<StrandSample> samples)
    {
        if (outputCount <= 0 || control.Length == 0)
        {
            LastLength = 0f;
            return 0;
        }

        int write = System.Math.Min(outputCount, System.Math.Min(positions.Length, samples.Length));
        if (write <= 0)
        {
            LastLength = 0f;
            return 0;
        }

        if (control.Length == 1)
        {
            LastLength = 0f;
            positions.Slice(0, write).Fill(control[0]);
            samples.Slice(0, write).Fill(new StrandSample(0, 0f));
            return write;
        }

        if (write == 1)
        {
            LastLength = 0f;
            positions[0] = control[0];
            samples[0] = new StrandSample(0, 0f);
            return 1;
        }

        int lastControl = control.Length - 1;

        if (control.Length == 2)
        {
            LastLength = (control[1] - control[0]).Length;
            float step = 1f / (write - 1);
            for (int i = 0; i < write; i++)
            {
                float t = i * step;
                positions[i] = float3.Lerp(control[0], control[1], t);
                samples[i] = new StrandSample(0, t);
            }
            positions[write - 1] = control[1];
            samples[write - 1] = new StrandSample(0, 1f);
            return write;
        }

        BuildArcTable(control);
        float total = LastLength;

        if (!(total > 1e-6f))
        {
            // Every control point sits on top of every other one. Spread the samples across the
            // parameter so the caller still gets a well-formed run rather than a pile at index 0.
            float step = lastControl / (float)(write - 1);
            for (int i = 0; i < write; i++)
            {
                float u = i * step;
                int segment = System.Math.Min((int)u, lastControl - 1);
                positions[i] = control[segment];
                samples[i] = new StrandSample(segment, u - segment);
            }
            positions[write - 1] = control[lastControl];
            samples[write - 1] = new StrandSample(lastControl - 1, 1f);
            return write;
        }

        int steps = System.Math.Max(1, FlattenSteps);
        float invSteps = 1f / steps;
        float lengthStep = total / (write - 1);
        int cursor = 0;

        positions[0] = control[0];
        samples[0] = new StrandSample(0, 0f);

        for (int i = 1; i < write - 1; i++)
        {
            float target = lengthStep * i;
            while (cursor + 1 < _tableCount - 1 && _cumulative[cursor + 1] < target)
                cursor++;

            float lo = _cumulative[cursor];
            float hi = _cumulative[cursor + 1];
            float span = hi - lo;
            float local = span > 1e-9f ? (target - lo) / span : 0f;

            // Flattened index -> (segment, t). Steps per segment is constant, so this is exact.
            float flat = cursor + local;
            int segment = System.Math.Min((int)(flat * invSteps), lastControl - 1);
            float t = System.Math.Clamp(flat * invSteps - segment, 0f, 1f);

            positions[i] = CatmullRom.Evaluate(control, segment, t);
            samples[i] = new StrandSample(segment, t);
        }

        positions[write - 1] = control[lastControl];
        samples[write - 1] = new StrandSample(lastControl - 1, 1f);
        return write;
    }

    // Same expansion, subdividing evenly in the curve parameter instead of by arc length. Spacing
    // follows the control points, so it bunches wherever they do - cheap, and good enough for a strand
    // whose control points are already evenly spaced (a ribbon of regularly emitted particles).
    public int ExpandByParameter(ReadOnlySpan<float3> control, int outputCount, Span<float3> positions, Span<StrandSample> samples)
    {
        if (outputCount <= 0 || control.Length == 0)
            return 0;

        int write = System.Math.Min(outputCount, System.Math.Min(positions.Length, samples.Length));
        if (write <= 0)
            return 0;

        if (control.Length == 1)
        {
            positions.Slice(0, write).Fill(control[0]);
            samples.Slice(0, write).Fill(new StrandSample(0, 0f));
            return write;
        }

        if (write == 1)
        {
            positions[0] = control[0];
            samples[0] = new StrandSample(0, 0f);
            return 1;
        }

        int lastControl = control.Length - 1;
        float step = lastControl / (float)(write - 1);
        for (int i = 0; i < write; i++)
        {
            float u = i * step;
            int segment = System.Math.Min((int)u, lastControl - 1);
            float t = System.Math.Clamp(u - segment, 0f, 1f);
            positions[i] = CatmullRom.Evaluate(control, segment, t);
            samples[i] = new StrandSample(segment, t);
        }
        positions[write - 1] = control[lastControl];
        samples[write - 1] = new StrandSample(lastControl - 1, 1f);
        return write;
    }

    // Number of vertices Expand needs to draw a strand at the given per-segment smoothing.
    public static int VertexCount(int controlCount, int smoothing)
    {
        if (controlCount <= 1)
            return controlCount;
        return (controlCount - 1) * System.Math.Max(1, smoothing) + 1;
    }

    private void BuildArcTable(ReadOnlySpan<float3> control)
    {
        int segments = control.Length - 1;
        int steps = System.Math.Max(1, FlattenSteps);
        int required = segments * steps + 1;
        if (_cumulative.Length < required)
            _cumulative = new float[System.Math.Max(required, _cumulative.Length * 2)];

        _tableCount = required;
        _cumulative[0] = 0f;

        var previous = control[0];
        float accumulated = 0f;
        float invSteps = 1f / steps;
        int index = 1;

        for (int segment = 0; segment < segments; segment++)
        {
            for (int step = 1; step <= steps; step++)
            {
                var point = step == steps
                    ? control[segment + 1]
                    : CatmullRom.Evaluate(control, segment, step * invSteps);
                accumulated += (point - previous).Length;
                _cumulative[index++] = accumulated;
                previous = point;
            }
        }

        LastLength = accumulated;
    }
}
