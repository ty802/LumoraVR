// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// A ribbon lying in the XY plane facing +Z, running along Y with a half-round cap at each end. For
// progress bars, underlines, laser trails, pill buttons and anything else that wants a capsule
// outline without being a capsule.
//
// Triangulated as a fan from the centre over a closed outline loop, so a square-ended stripe and a
// fully rounded one share one topology and only the outline positions change. CapSegments 1 collapses
// each arc to its two corners, which is a plain rectangle. -xlinka
public class PhosStripe : PhosShape
{
    public const int MaxCapSegments = 32;

    public PhosVertex FirstVertex;

    // the end caps are half-rounds of half this width
    public float Width = 0.1f;

    // between the two cap centres, excluding the caps themselves
    public float Length = 1f;

    public float2 UVScale = new float2(1f, 1f);

    public color? Color;

    // 1 gives square ends
    public readonly int CapSegments;

    public readonly bool DualSided;

    private readonly int _loopCount;
    private readonly int _sideVertices;
    private readonly int _totalVertices;
    private readonly int _totalTriangles;

    // Constructors

    public PhosStripe(PhosTriangleSubmesh submesh, int capSegments = 8, bool dualSided = false) : base(submesh.Mesh)
    {
        CapSegments = System.Math.Clamp(capSegments, 1, MaxCapSegments);
        DualSided = dualSided;

        _loopCount = 2 * (CapSegments + 1);
        _sideVertices = _loopCount + 1;
        _totalVertices = _sideVertices * (DualSided ? 2 : 1);
        _totalTriangles = _loopCount * (DualSided ? 2 : 1);

        Mesh.HasNormals = true;
        Mesh.HasTangents = true;
        Mesh.HasUV0s = true;

        FirstVertex = AddStripeGeometry(submesh);
    }

    public override void Remove()
    {
        Mesh.RemoveVertices(FirstVertex.Index, _totalVertices, updateSubmeshes: false);
        base.Remove();
    }

    public override void Update()
    {
        Mesh.HasColors = Color.HasValue;
        UpdateStripeVertices();
    }

    private PhosVertex AddStripeGeometry(PhosTriangleSubmesh submesh)
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

        // Outline runs counter-clockwise seen from +Z, so the front fan is emitted reversed to face
        // that way and the back fan keeps the natural order.
        int frontCenter = firstVertexIndex;
        int frontLoop = frontCenter + 1;
        for (int i = 0; i < _loopCount; i++)
        {
            int next = (i + 1) % _loopCount;
            submesh.SetTriangle(triIndex++, frontCenter, frontLoop + next, frontLoop + i);
        }

        if (DualSided)
        {
            int backCenter = firstVertexIndex + _sideVertices;
            int backLoop = backCenter + 1;
            for (int i = 0; i < _loopCount; i++)
            {
                int next = (i + 1) % _loopCount;
                submesh.SetTriangle(triIndex++, backCenter, backLoop + i, backLoop + next);
            }
        }

        return firstVertex;
    }

    private void UpdateStripeVertices()
    {
        float halfWidth = Width * 0.5f;
        float halfLength = Length * 0.5f;
        float spanX = System.Math.Max(Width, 1e-6f);
        float spanY = System.Math.Max(Length + Width, 1e-6f);

        WriteSide(FirstVertex.Index, float3.Forward, 1f, halfWidth, halfLength, spanX, spanY);
        if (DualSided)
            WriteSide(FirstVertex.Index + _sideVertices, float3.Backward, -1f, halfWidth, halfLength, spanX, spanY);
    }

    private void WriteSide(int centerIndex, float3 normal, float tangentW, float halfWidth, float halfLength, float spanX, float spanY)
    {
        WriteVertex(centerIndex, float3.Zero, normal, tangentW, spanX, spanY, halfWidth, halfLength);

        int index = centerIndex + 1;
        for (int i = 0; i < _loopCount; i++)
        {
            bool top = i <= CapSegments;
            int k = top ? i : i - (CapSegments + 1);
            float angle = (top ? 0f : MathF.PI) + (float)k / CapSegments * MathF.PI;
            float centerY = top ? halfLength : -halfLength;

            float3 p = new float3(MathF.Cos(angle) * halfWidth, centerY + MathF.Sin(angle) * halfWidth, 0f);
            WriteVertex(index++, p, normal, tangentW, spanX, spanY, halfWidth, halfLength);
        }
    }

    private void WriteVertex(int index, float3 local, float3 normal, float tangentW, float spanX, float spanY, float halfWidth, float halfLength)
    {
        Mesh.RawPositions[index] = local + Position;
        Mesh.RawNormals[index] = normal;
        // Flipping the tangent sign rather than the tangent itself mirrors the binormal, which is what
        // keeps a normal map reading correctly on the back side.
        Mesh.RawTangents[index] = new float4(1f, 0f, 0f, tangentW);
        Mesh.RawUV0s[index] = new float2(
            ((local.x + halfWidth) / spanX) * UVScale.x,
            ((local.y + halfLength + halfWidth) / spanY) * UVScale.y);
        if (Color.HasValue) Mesh.RawColors[index] = Color.Value;
    }
}
