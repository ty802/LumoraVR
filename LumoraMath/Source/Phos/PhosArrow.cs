// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Phos;

// A cylinder shaft along +Y from the origin with a cone tip stacked on top; shaft bottom and cone
// base are capped so the mesh is closed. Total length along +Y is ShaftLength + TipLength. Segment
// count is baked per instance (rebuild to change); dimensions update in place.
public class PhosArrow : PhosShape
{
    public PhosVertex FirstVertex;

    public float ShaftRadius = 0.007f;

    public float ShaftLength = 0.12f;

    public float TipRadius = 0.02f;

    public float TipLength = 0.03f;

    public color? Color;

    public readonly int Segments;

    private readonly int _totalVertices;
    private readonly int _totalTriangles;

    // Vertex layout offsets (relative to FirstVertex), all a function of segment count S:
    //   shaftBottomRing : S+1   (y=0,          r=shaft, radial normal)
    //   shaftTopRing    : S+1   (y=shaftLen,   r=shaft, radial normal)
    //   shaftCapCenter  : 1     (y=0,          -Y)
    //   shaftCapRing    : S+1   (y=0,          r=shaft, -Y)
    //   coneCapCenter   : 1     (y=shaftLen,   -Y)
    //   coneCapRing     : S+1   (y=shaftLen,   r=tip,   -Y)
    //   coneSideBase    : S+1   (y=shaftLen,   r=tip,   slant normal)
    //   coneApex        : S     (y=shaftLen+tipLen, slant normal, mid-angle)
    // - xlinka

    private int ShaftBottomRing => FirstVertex.Index;
    private int ShaftTopRing => ShaftBottomRing + (Segments + 1);
    private int ShaftCapCenter => ShaftTopRing + (Segments + 1);
    private int ShaftCapRing => ShaftCapCenter + 1;
    private int ConeCapCenter => ShaftCapRing + (Segments + 1);
    private int ConeCapRing => ConeCapCenter + 1;
    private int ConeSideBase => ConeCapRing + (Segments + 1);
    private int ConeApex => ConeSideBase + (Segments + 1);

    // Constructors

    public PhosArrow(PhosTriangleSubmesh submesh, int segments = 12) : base(submesh.Mesh)
    {
        Segments = System.Math.Max(3, segments);
        _totalVertices = 6 * Segments + 7; // 5*(S+1) + S + 2
        _totalTriangles = Segments * 5;    // side 2S + bottom cap S + cone cap S + cone side S

        Mesh.HasNormals = true;
        Mesh.HasTangents = true;
        Mesh.HasUV0s = true;

        FirstVertex = AddArrowGeometry(submesh);
    }

    public override void Remove()
    {
        Mesh.RemoveVertices(FirstVertex.Index, _totalVertices, updateSubmeshes: false);
        base.Remove();
    }

    public override void Update()
    {
        Mesh.HasColors = Color.HasValue;
        UpdateArrowVertices();
    }

    private PhosVertex AddArrowGeometry(PhosTriangleSubmesh submesh)
    {
        PhosMesh mesh = submesh.Mesh;

        mesh.IncreaseVertexCount(_totalVertices);
        PhosVertex firstVertex = mesh.GetVertex(mesh.VertexCount - _totalVertices);
        FirstVertex = firstVertex;

        int firstTriangle = 0;
        for (int i = 0; i < _totalTriangles; i++)
        {
            PhosTriangle triangle = submesh.AddTriangle();
            if (i == 0)
                firstTriangle = triangle.IndexUnsafe;
            AllTriangles?.Add(triangle);
        }

        // Connect triangles
        int triIndex = firstTriangle;
        int s = Segments;
        int shaftBottom = ShaftBottomRing;
        int shaftTop = ShaftTopRing;
        int shaftCapCenter = ShaftCapCenter;
        int shaftCapRing = ShaftCapRing;
        int coneCapCenter = ConeCapCenter;
        int coneCapRing = ConeCapRing;
        int coneSideBase = ConeSideBase;
        int coneApex = ConeApex;

        // Shaft side (outer surface as front face - matches the cylinder's proven winding)
        for (int i = 0; i < s; i++)
        {
            int bl = shaftBottom + i;
            int br = shaftBottom + i + 1;
            int tl = shaftTop + i;
            int tr = shaftTop + i + 1;

            submesh.SetTriangle(triIndex++, bl, br, tr);
            submesh.SetTriangle(triIndex++, bl, tr, tl);
        }

        // Shaft bottom cap (faces -Y)
        for (int i = 0; i < s; i++)
            submesh.SetTriangle(triIndex++, shaftCapCenter, shaftCapRing + i + 1, shaftCapRing + i);

        // Cone base cap (faces -Y, closes the underside of the cone)
        for (int i = 0; i < s; i++)
            submesh.SetTriangle(triIndex++, coneCapCenter, coneCapRing + i + 1, coneCapRing + i);

        // Cone side to apex
        for (int i = 0; i < s; i++)
            submesh.SetTriangle(triIndex++, coneSideBase + i, coneSideBase + i + 1, coneApex + i);

        return firstVertex;
    }

    private void UpdateArrowVertices()
    {
        int s = Segments;
        float rShaft = ShaftRadius;
        float rTip = TipRadius;
        float y0 = 0f;
        float y1 = ShaftLength;
        float y2 = ShaftLength + TipLength;

        // Slant normal radial/axial split for the cone side
        float slantLen = MathF.Sqrt(TipLength * TipLength + rTip * rTip);
        float nAxial = slantLen > 0f ? rTip / slantLen : 0f;       // +Y component
        float nRadial = slantLen > 0f ? TipLength / slantLen : 1f; // outward component

        int shaftBottom = ShaftBottomRing;
        int shaftTop = ShaftTopRing;
        int shaftCapCenter = ShaftCapCenter;
        int shaftCapRing = ShaftCapRing;
        int coneCapCenter = ConeCapCenter;
        int coneCapRing = ConeCapRing;
        int coneSideBase = ConeSideBase;
        int coneApex = ConeApex;

        for (int i = 0; i <= s; i++)
        {
            float angle = (float)i / s * MathF.PI * 2f;
            float cos = MathF.Cos(angle);
            float sin = MathF.Sin(angle);
            float u = (float)i / s;

            float3 radial = new float3(cos, 0f, sin);
            float4 tangent = new float4(-sin, 0f, cos, 1f);

            // Shaft side rings
            SetVertex(shaftBottom + i, new float3(cos * rShaft, y0, sin * rShaft), radial, tangent, new float2(u, 0f));
            SetVertex(shaftTop + i, new float3(cos * rShaft, y1, sin * rShaft), radial, tangent, new float2(u, 1f));

            // Shaft bottom cap ring (-Y)
            SetVertex(shaftCapRing + i, new float3(cos * rShaft, y0, sin * rShaft), float3.Down,
                new float4(1f, 0f, 0f, 1f), new float2(cos * 0.5f + 0.5f, sin * 0.5f + 0.5f));

            // Cone base cap ring (-Y)
            SetVertex(coneCapRing + i, new float3(cos * rTip, y1, sin * rTip), float3.Down,
                new float4(1f, 0f, 0f, 1f), new float2(cos * 0.5f + 0.5f, sin * 0.5f + 0.5f));

            // Cone side base ring (slant normal)
            float3 slantNormal = new float3(cos * nRadial, nAxial, sin * nRadial);
            SetVertex(coneSideBase + i, new float3(cos * rTip, y1, sin * rTip), slantNormal, tangent, new float2(u, 0f));
        }

        // Cap centers
        SetVertex(shaftCapCenter, new float3(0f, y0, 0f), float3.Down, new float4(1f, 0f, 0f, 1f), new float2(0.5f, 0.5f));
        SetVertex(coneCapCenter, new float3(0f, y1, 0f), float3.Down, new float4(1f, 0f, 0f, 1f), new float2(0.5f, 0.5f));

        // Cone apex verts (one per segment, placed at the mid-angle for a clean tip normal)
        for (int i = 0; i < s; i++)
        {
            float midAngle = (i + 0.5f) / s * MathF.PI * 2f;
            float cos = MathF.Cos(midAngle);
            float sin = MathF.Sin(midAngle);
            float3 slantNormal = new float3(cos * nRadial, nAxial, sin * nRadial);
            float4 tangent = new float4(-sin, 0f, cos, 1f);
            SetVertex(coneApex + i, new float3(0f, y2, 0f), slantNormal, tangent, new float2((i + 0.5f) / s, 1f));
        }
    }

    private void SetVertex(int index, float3 pos, float3 normal, float4 tangent, float2 uv)
    {
        Mesh.RawPositions[index] = pos + Position;
        Mesh.RawNormals[index] = normal;
        Mesh.RawTangents[index] = tangent;
        Mesh.RawUV0s[index] = uv;
        if (Color.HasValue) Mesh.RawColors[index] = Color.Value;
    }
}
