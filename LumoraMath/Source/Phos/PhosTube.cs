// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// A circular profile carried along a polyline, with an optional flat cap at each end. The path and
// the per-point radius are plain arrays the caller fills before calling Update, so the straight, bent
// and bezier tube components all drive this one generator instead of each growing their own copy of
// the sweep.
//
// Frames are parallel-transported rather than rebuilt from a fixed up vector. Recomputing a frame per
// segment makes the profile spin around the path wherever the curve tips through vertical, and a
// fixed up vector degenerates outright when the path runs along it. Transport carries the previous
// frame through the smallest rotation that lines it up with the new direction, so a texture stays
// straight along the whole run. -xlinka
public class PhosTube : PhosShape
{
    public PhosVertex FirstVertex;

    public float2 UVScale = new float2(1f, 1f);

    public color? Color;

    public readonly int Sides;

    public readonly int Segments;

    public readonly bool Caps;

    // Segments + 1 of them; write into this before calling Update
    public readonly float3[] Path;

    // Segments + 1 of them
    public readonly float[] Radii;

    private readonly int _cols;
    private readonly int _ringVertices;
    private readonly int _totalVertices;
    private readonly int _totalTriangles;

    private readonly float3[] _directions;
    private readonly float3[] _frameX;
    private readonly float3[] _frameY;
    private readonly float[] _distances;

    // Constructors

    public PhosTube(PhosTriangleSubmesh submesh, int sides = 12, int segments = 1, bool caps = true) : base(submesh.Mesh)
    {
        Sides = System.Math.Max(3, sides);
        Segments = System.Math.Max(1, segments);
        Caps = caps;

        _cols = Sides + 1;
        _ringVertices = (Segments + 1) * _cols;
        _totalVertices = _ringVertices + (Caps ? 2 * (Sides + 2) : 0);
        _totalTriangles = Segments * Sides * 2 + (Caps ? Sides * 2 : 0);

        Path = new float3[Segments + 1];
        Radii = new float[Segments + 1];
        _directions = new float3[Segments + 1];
        _frameX = new float3[Segments + 1];
        _frameY = new float3[Segments + 1];
        _distances = new float[Segments + 1];

        for (int i = 0; i < Radii.Length; i++)
            Radii[i] = 0.05f;

        Mesh.HasNormals = true;
        Mesh.HasTangents = true;
        Mesh.HasUV0s = true;

        FirstVertex = AddTubeGeometry(submesh);
    }

    public void SetRadius(float radius)
    {
        for (int i = 0; i < Radii.Length; i++)
            Radii[i] = radius;
    }

    public override void Remove()
    {
        Mesh.RemoveVertices(FirstVertex.Index, _totalVertices, updateSubmeshes: false);
        base.Remove();
    }

    public override void Update()
    {
        Mesh.HasColors = Color.HasValue;
        BuildFrames();
        UpdateTubeVertices();
    }

    private int StartCapCenter => FirstVertex.Index + _ringVertices;
    private int StartCapRing => StartCapCenter + 1;
    private int EndCapCenter => StartCapRing + _cols;
    private int EndCapRing => EndCapCenter + 1;

    private PhosVertex AddTubeGeometry(PhosTriangleSubmesh submesh)
    {
        PhosMesh mesh = submesh.Mesh;

        mesh.IncreaseVertexCount(_totalVertices);
        PhosVertex firstVertex = mesh.GetVertex(mesh.VertexCount - _totalVertices);
        int firstVertexIndex = firstVertex.IndexUnsafe;

        int firstTriangle = 0;
        for (int i = 0; i < _totalTriangles; i++)
        {
            PhosTriangle triangle = submesh.AddTriangle();
            if (i == 0)
                firstTriangle = triangle.IndexUnsafe;
            AllTriangles?.Add(triangle);
        }

        int triIndex = firstTriangle;
        for (int seg = 0; seg < Segments; seg++)
        {
            int lower = firstVertexIndex + seg * _cols;
            int upper = lower + _cols;
            for (int i = 0; i < Sides; i++)
            {
                submesh.SetTriangle(triIndex++, lower + i, lower + i + 1, upper + i + 1);
                submesh.SetTriangle(triIndex++, lower + i, upper + i + 1, upper + i);
            }
        }

        if (Caps)
        {
            int startCenter = firstVertexIndex + _ringVertices;
            int startRing = startCenter + 1;
            int endCenter = startRing + _cols;
            int endRing = endCenter + 1;

            // Start cap faces backwards along the path, so its fan is wound the opposite way to the end.
            for (int i = 0; i < Sides; i++)
                submesh.SetTriangle(triIndex++, startCenter, startRing + i + 1, startRing + i);
            for (int i = 0; i < Sides; i++)
                submesh.SetTriangle(triIndex++, endCenter, endRing + i, endRing + i + 1);
        }

        return firstVertex;
    }

    private void BuildFrames()
    {
        int last = Segments;

        // Direction at each point: the segment direction at the ends, the average of the two adjoining
        // segments in the middle, so the profile does not kink at a shared point.
        float3 fallback = float3.Up;
        for (int i = 0; i <= last; i++)
        {
            float3 d;
            if (i == 0)
                d = Path[System.Math.Min(1, last)] - Path[0];
            else if (i == last)
                d = Path[last] - Path[last - 1];
            else
                d = Path[i + 1] - Path[i - 1];

            if (d.LengthSquared < 1e-12f)
                d = i > 0 ? _directions[i - 1] : fallback;
            _directions[i] = d.Normalized;
            if (_directions[i].LengthSquared < 0.5f)
                _directions[i] = fallback;
        }

        _distances[0] = 0f;
        for (int i = 1; i <= last; i++)
            _distances[i] = _distances[i - 1] + (Path[i] - Path[i - 1]).Length;

        // Seed the first frame from whichever axis is least aligned with the path, then transport it.
        float3 d0 = _directions[0];
        float3 seed = System.Math.Abs(d0.y) < 0.9f ? float3.Up : float3.Right;
        _frameX[0] = float3.Cross(seed, d0).Normalized;
        if (_frameX[0].LengthSquared < 0.5f)
            _frameX[0] = float3.Cross(float3.Forward, d0).Normalized;
        _frameY[0] = float3.Cross(_frameX[0], d0);

        for (int i = 1; i <= last; i++)
        {
            float3 x = RotateBetween(_directions[i - 1], _directions[i], _frameX[i - 1]);
            // Re-orthogonalize: the transport is exact in theory, drift is not.
            x = (x - _directions[i] * float3.Dot(x, _directions[i])).Normalized;
            if (x.LengthSquared < 0.5f)
                x = _frameX[i - 1];
            _frameX[i] = x;
            _frameY[i] = float3.Cross(x, _directions[i]);
        }
    }

    // Rodrigues rotation, with the two degenerate cases spelled out: identical directions leave the
    // vector alone, opposed directions are a half turn about any perpendicular.
    private static float3 RotateBetween(float3 from, float3 to, float3 v)
    {
        float3 axis = float3.Cross(from, to);
        float sin = axis.Length;
        float cos = float3.Dot(from, to);

        if (sin < 1e-7f)
        {
            if (cos > 0f)
                return v;
            float3 perp = System.Math.Abs(from.y) < 0.9f ? float3.Cross(float3.Up, from) : float3.Cross(float3.Right, from);
            perp = perp.Normalized;
            return perp * (2f * float3.Dot(v, perp)) - v;
        }

        float3 k = axis / sin;
        float angle = MathF.Atan2(sin, cos);
        float c = MathF.Cos(angle);
        float s = MathF.Sin(angle);
        return v * c + float3.Cross(k, v) * s + k * (float3.Dot(k, v) * (1f - c));
    }

    private void UpdateTubeVertices()
    {
        int last = Segments;
        float total = _distances[last];
        float invTotal = total > 1e-6f ? 1f / total : 0f;

        for (int seg = 0; seg <= last; seg++)
        {
            float3 center = Path[seg] + Position;
            float3 nx = _frameX[seg];
            float3 ny = _frameY[seg];
            float radius = Radii[seg];
            float v = _distances[seg] * invTotal * UVScale.y;
            int index = FirstVertex.Index + seg * _cols;

            for (int i = 0; i < _cols; i++)
            {
                float angle = (float)i / Sides * MathF.PI * 2f;
                float cosA = MathF.Cos(angle);
                float sinA = MathF.Sin(angle);

                float3 normal = nx * cosA + ny * sinA;
                float3 tangent = nx * -sinA + ny * cosA;

                Mesh.RawPositions[index] = center + normal * radius;
                Mesh.RawNormals[index] = normal;
                Mesh.RawTangents[index] = new float4(tangent.x, tangent.y, tangent.z, 1f);
                Mesh.RawUV0s[index] = new float2((float)i / Sides * UVScale.x, v);
                if (Color.HasValue) Mesh.RawColors[index] = Color.Value;
                index++;
            }
        }

        if (!Caps)
            return;

        WriteCap(StartCapCenter, StartCapRing, 0, -_directions[0]);
        WriteCap(EndCapCenter, EndCapRing, last, _directions[last]);
    }

    private void WriteCap(int centerIndex, int ringIndex, int seg, float3 normal)
    {
        float3 center = Path[seg] + Position;
        float3 nx = _frameX[seg];
        float3 ny = _frameY[seg];
        float radius = Radii[seg];

        Mesh.RawPositions[centerIndex] = center;
        Mesh.RawNormals[centerIndex] = normal;
        Mesh.RawTangents[centerIndex] = new float4(nx.x, nx.y, nx.z, 1f);
        Mesh.RawUV0s[centerIndex] = new float2(0.5f * UVScale.x, 0.5f * UVScale.y);
        if (Color.HasValue) Mesh.RawColors[centerIndex] = Color.Value;

        for (int i = 0; i < _cols; i++)
        {
            float angle = (float)i / Sides * MathF.PI * 2f;
            float cosA = MathF.Cos(angle);
            float sinA = MathF.Sin(angle);
            float3 radial = nx * cosA + ny * sinA;

            int index = ringIndex + i;
            Mesh.RawPositions[index] = center + radial * radius;
            Mesh.RawNormals[index] = normal;
            Mesh.RawTangents[index] = new float4(nx.x, nx.y, nx.z, 1f);
            Mesh.RawUV0s[index] = new float2((cosA * 0.5f + 0.5f) * UVScale.x, (sinA * 0.5f + 0.5f) * UVScale.y);
            if (Color.HasValue) Mesh.RawColors[index] = Color.Value;
        }
    }
}
