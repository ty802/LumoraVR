// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// A rounded rectangle lying in the XY plane facing +Z, with REAL corner geometry. The arcs are
// generated at the rect's own half-extents, so a wide panel gets the same corner as a tall one -
// which is the whole point, because a square rounded-rect TEXTURE stretched over a quad turns its
// corners into ellipses the moment the aspect leaves 1:1 and the shape reads as a blobby pill.
//
// RING MODE swaps the centre fan for a strip between two outlines. Offsetting a rounded rect outward
// by d gives a rect grown by d on every side with the radius grown by d, and the corner CENTRES do
// not move - so the two loops share their angles point for point and the strip needs no seam
// handling at any radius. That is the only reason this is one class and not two.
//
// Topology is fixed at construction (segment count, ring, dual sided), so size, radius, rim width,
// UVs and colours all animate with zero allocation and no rebuild. A zero radius collapses each arc
// onto its corner rather than changing the vertex count: degenerate triangles cost nothing to
// rasterise, and keeping the count fixed is what lets a radius be driven. -xlinka
public class PhosRoundedRect : PhosShape
{
    public const int MaxCornerSegments = 32;

    private const float QuarterTurn = MathF.PI * 0.5f;

    public PhosVertex FirstVertex;

    // In ring mode this is the INNER rect: the band grows outward from it.
    public float2 Size = float2.One;

    public float CornerRadius = 0.1f;

    // Negative follows CornerRadius, which is every existing caller. Set it and the two BOTTOM corners
    // take this radius instead - a card whose top is rounded and whose bottom is nearly square reads as
    // attached to whatever it sits on rather than floating over it. Each corner still clamps on its own,
    // so top + bottom can never exceed the height. -xlinka
    public float BottomCornerRadius = -1f;

    // Ring mode only.
    public float RimWidth = 0.01f;

    public float2 UVScale = float2.One;

    public float2 UVOffset = float2.Zero;

    public bool UseColors;

    public color UpperLeftColor = color.White;

    public color UpperRightColor = color.White;

    public color LowerLeftColor = color.White;

    public color LowerRightColor = color.White;

    // Arc subdivisions per corner. 1 gives a bevel, not a square: the arc still has two endpoints.
    public readonly int CornerSegments;

    public readonly bool Ring;

    public readonly bool DualSided;

    private readonly int _loopCount;
    private readonly int _sideVertices;
    private readonly int _totalVertices;
    private readonly int _totalTriangles;

    public color? Color
    {
        get => UseColors ? UpperLeftColor : null;
        set
        {
            if (!value.HasValue)
            {
                UseColors = false;
                return;
            }
            UseColors = true;
            UpperLeftColor = value.Value;
            UpperRightColor = value.Value;
            LowerLeftColor = value.Value;
            LowerRightColor = value.Value;
        }
    }

    public PhosRoundedRect(PhosTriangleSubmesh submesh, int cornerSegments = 6, bool ring = false, bool dualSided = false)
        : base(submesh.Mesh)
    {
        CornerSegments = System.Math.Clamp(cornerSegments, 1, MaxCornerSegments);
        Ring = ring;
        DualSided = dualSided;

        _loopCount = 4 * (CornerSegments + 1);
        _sideVertices = Ring ? _loopCount * 2 : _loopCount + 1;
        _totalVertices = _sideVertices * (DualSided ? 2 : 1);
        _totalTriangles = (Ring ? _loopCount * 2 : _loopCount) * (DualSided ? 2 : 1);

        Mesh.HasNormals = true;
        Mesh.HasTangents = true;
        Mesh.HasUV0s = true;

        FirstVertex = AddGeometry(submesh);
    }

    // Past half the SHORT side the arcs would cross each other and the outline would fold inside out.
    public static float ClampRadius(in float2 size, float radius)
    {
        float limit = MathF.Min(MathF.Abs(size.x), MathF.Abs(size.y)) * 0.5f;
        return System.Math.Clamp(radius, 0f, MathF.Max(0f, limit));
    }

    public override void Remove()
    {
        Mesh.RemoveVertices(FirstVertex.Index, _totalVertices, updateSubmeshes: false);
        base.Remove();
    }

    public override void Update()
    {
        Mesh.HasColors = UseColors;
        // CheckColors only allocates while the flag is still OFF, and IncreaseVertexCount only grows a
        // buffer that already exists - so a mesh that added its vertices before colours were switched
        // on has the flag set and no buffer behind it. Walk it back through the off state to get one.
        if (UseColors && Mesh.RawColors.Length < Mesh.VertexCount)
        {
            Mesh.HasColors = false;
            Mesh.CheckColors();
        }

        float2 half = Size * 0.5f;
        float radius = ClampRadius(Size, CornerRadius);
        _bottomRadius = BottomCornerRadius < 0f ? radius : ClampRadius(Size, BottomCornerRadius);

        if (Ring)
        {
            float rim = MathF.Max(0f, RimWidth);
            WriteRingSide(FirstVertex.Index, half, radius, rim, float3.Forward, 1f);
            if (DualSided)
                WriteRingSide(FirstVertex.Index + _sideVertices, half, radius, rim, float3.Backward, -1f);
            return;
        }

        WriteFilledSide(FirstVertex.Index, half, radius, float3.Forward, 1f);
        if (DualSided)
            WriteFilledSide(FirstVertex.Index + _sideVertices, half, radius, float3.Backward, -1f);
    }

    // Resolved once per Update so OutlinePoint stays a pure lookup.
    private float _bottomRadius;

    private PhosVertex AddGeometry(PhosTriangleSubmesh submesh)
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
        Wind(submesh, firstVertexIndex, ref triIndex, front: true);
        if (DualSided)
            Wind(submesh, firstVertexIndex + _sideVertices, ref triIndex, front: false);

        return firstVertex;
    }

    // The outline runs counter-clockwise seen from +Z and front faces wind clockwise, so the +Z side
    // is emitted reversed and the -Z side keeps the natural order.
    private void Wind(PhosTriangleSubmesh submesh, int baseIndex, ref int triIndex, bool front)
    {
        if (Ring)
        {
            int inner = baseIndex;
            int outer = baseIndex + _loopCount;
            for (int i = 0; i < _loopCount; i++)
            {
                int next = (i + 1) % _loopCount;
                if (front)
                {
                    submesh.SetTriangle(triIndex++, inner + i, outer + next, outer + i);
                    submesh.SetTriangle(triIndex++, inner + i, inner + next, outer + next);
                }
                else
                {
                    submesh.SetTriangle(triIndex++, inner + i, outer + i, outer + next);
                    submesh.SetTriangle(triIndex++, inner + i, outer + next, inner + next);
                }
            }
            return;
        }

        int center = baseIndex;
        int loop = baseIndex + 1;
        for (int i = 0; i < _loopCount; i++)
        {
            int next = (i + 1) % _loopCount;
            if (front)
                submesh.SetTriangle(triIndex++, center, loop + next, loop + i);
            else
                submesh.SetTriangle(triIndex++, center, loop + i, loop + next);
        }
    }

    private void WriteFilledSide(int baseIndex, float2 half, float radius, float3 normal, float tangentW)
    {
        WriteVertex(baseIndex, float2.Zero, half, normal, tangentW);

        for (int i = 0; i < _loopCount; i++)
            WriteVertex(baseIndex + 1 + i, OutlinePoint(i, half, radius, 0f), half, normal, tangentW);
    }

    // Both loops share the corner centres, so the band keeps a constant width around the whole
    // outline including through the arcs. UVs span the OUTER bounds so the inner loop lands inside
    // 0..1 rather than off the edge of it.
    private void WriteRingSide(int baseIndex, float2 half, float radius, float rim, float3 normal, float tangentW)
    {
        float2 outerHalf = half + new float2(rim, rim);

        for (int i = 0; i < _loopCount; i++)
            WriteVertex(baseIndex + i, OutlinePoint(i, half, radius, 0f), outerHalf, normal, tangentW);

        for (int i = 0; i < _loopCount; i++)
            WriteVertex(baseIndex + _loopCount + i, OutlinePoint(i, half, radius, rim), outerHalf, normal, tangentW);
    }

    // Corner 0 is upper right and the walk runs counter-clockwise. `grow` pushes the point outward
    // along the corner's own radius, which is what makes the ring's outer loop.
    private float2 OutlinePoint(int index, float2 half, float radius, float grow)
    {
        int corner = index / (CornerSegments + 1);
        int step = index % (CornerSegments + 1);
        float angle = (corner + (float)step / CornerSegments) * QuarterTurn;

        float signX = corner == 0 || corner == 3 ? 1f : -1f;
        float signY = corner <= 1 ? 1f : -1f;
        // Corners 0 and 1 are the top pair, 2 and 3 the bottom pair.
        if (corner > 1)
            radius = _bottomRadius;

        float centerX = half.x - radius;
        float centerY = half.y - radius;
        float arc = radius + grow;

        return new float2(
            signX * centerX + MathF.Cos(angle) * arc,
            signY * centerY + MathF.Sin(angle) * arc);
    }

    private void WriteVertex(int index, float2 local, float2 uvHalf, float3 normal, float tangentW)
    {
        float3 position = new float3(local.x, local.y, 0f);
        float2 span = new float2(MathF.Max(uvHalf.x * 2f, 1e-6f), MathF.Max(uvHalf.y * 2f, 1e-6f));
        float u = (local.x + uvHalf.x) / span.x;
        float v = (local.y + uvHalf.y) / span.y;

        Mesh.RawPositions[index] = Rotation * position + Position;
        Mesh.RawNormals[index] = Rotation * normal;
        // Flipping the tangent sign rather than the tangent itself mirrors the binormal, which is
        // what keeps a normal map reading correctly on the back side.
        Mesh.RawTangents[index] = new float4(Rotation * float3.Right, tangentW);
        Mesh.RawUV0s[index] = new float2(u, v) * UVScale + UVOffset;

        if (!UseColors)
            return;

        var top = color.Lerp(UpperLeftColor, UpperRightColor, u);
        var bottom = color.Lerp(LowerLeftColor, LowerRightColor, u);
        Mesh.RawColors[index] = color.Lerp(bottom, top, v);
    }
}
