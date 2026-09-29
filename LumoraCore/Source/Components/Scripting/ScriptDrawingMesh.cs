// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Scripting;

// Every shape the drawing layer holds as one mesh, coloured by vertex: a line is a four-sided rod, a
// triangle a single face the material draws from both sides. Built on the machine that draws it from the
// list the layer hands in, never synced. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptDrawingMesh : ProceduralMesh
{
    private const int LineVertices = 8;
    private const int LineTriangles = 8;

    private readonly List<ScriptDrawnShape> _shapes = new();
    private ScriptDrawnShape[] _building = Array.Empty<ScriptDrawnShape>();

    public int ShapeCount => _shapes.Count;

    public void SetShapes(IReadOnlyList<ScriptDrawnShape> shapes)
    {
        _shapes.Clear();
        for (int i = 0; i < shapes.Count; i++)
            _shapes.Add(shapes[i]);
        RegenerateMesh();
    }

    protected override void PrepareAssetUpdateData()
    {
        if (_building.Length != _shapes.Count)
            _building = new ScriptDrawnShape[_shapes.Count];
        _shapes.CopyTo(_building);
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        int lines = 0;
        foreach (var shape in _building)
        {
            if (shape.Line)
                lines++;
        }
        int faces = _building.Length - lines;

        mesh.Clear();
        mesh.HasNormals = true;
        mesh.HasColors = true;
        mesh.IncreaseVertexCount(lines * LineVertices + faces * 3);
        var submesh = new PhosTriangleSubmesh(mesh);
        mesh.Submeshes.Add(submesh);
        submesh.SetCount(lines * LineTriangles + faces);

        int vertex = 0;
        int triangle = 0;
        foreach (var shape in _building)
        {
            if (shape.Line)
                BuildLine(mesh, submesh, shape, ref vertex, ref triangle);
            else
                BuildFace(mesh, submesh, shape, ref vertex, ref triangle);
        }

        uploadHint[MeshUploadHint.Flag.Geometry] = true;
        uploadHint[MeshUploadHint.Flag.Dynamic] = true;
        uploadHint[MeshUploadHint.Flag.Normals] = true;
        uploadHint[MeshUploadHint.Flag.Colors] = true;
    }

    protected override void ClearMeshData()
    {
    }

    private static void BuildLine(PhosMesh mesh, PhosTriangleSubmesh submesh, in ScriptDrawnShape shape, ref int vertex, ref int triangle)
    {
        var along = shape.B - shape.A;
        float length = along.Length;
        var direction = length > 1e-7f ? along / length : float3.Up;
        var across = float3.Cross(direction, MathF.Abs(direction.y) < 0.9f ? float3.Up : float3.Right).Normalized;
        var up = float3.Cross(across, direction).Normalized;
        float radius = shape.Radius;

        int first = vertex;
        for (int end = 0; end < 2; end++)
        {
            var center = end == 0 ? shape.A : shape.B;
            for (int side = 0; side < 4; side++)
            {
                var radial = side switch
                {
                    0 => across,
                    1 => up,
                    2 => -across,
                    _ => -up,
                };
                mesh.RawPositions[vertex] = center + radial * radius;
                mesh.RawNormals[vertex] = radial;
                mesh.RawColors[vertex] = shape.Color;
                vertex++;
            }
        }
        for (int side = 0; side < 4; side++)
        {
            int next = (side + 1) % 4;
            submesh.SetTriangle(triangle++, first + side, first + 4 + side, first + next);
            submesh.SetTriangle(triangle++, first + next, first + 4 + side, first + 4 + next);
        }
    }

    private static void BuildFace(PhosMesh mesh, PhosTriangleSubmesh submesh, in ScriptDrawnShape shape, ref int vertex, ref int triangle)
    {
        var normal = float3.Cross(shape.B - shape.A, shape.C - shape.A);
        normal = normal.Length > 1e-12f ? normal.Normalized : float3.Up;
        int first = vertex;
        mesh.RawPositions[vertex] = shape.A;
        mesh.RawPositions[vertex + 1] = shape.B;
        mesh.RawPositions[vertex + 2] = shape.C;
        for (int i = 0; i < 3; i++)
        {
            mesh.RawNormals[vertex + i] = normal;
            mesh.RawColors[vertex + i] = shape.Color;
        }
        vertex += 3;
        submesh.SetTriangle(triangle++, first, first + 1, first + 2);
    }
}
