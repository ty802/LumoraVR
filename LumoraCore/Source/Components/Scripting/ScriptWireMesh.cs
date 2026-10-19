// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Scripting;

// Every wire of a graph as one mesh, so a graph is one draw call however many wires it has and moving a
// card re-uploads wires, never canvases. Each wire is a thin tube along an S-curve, coloured from its
// output end to its input end. Built on the machine that draws it from a list handed in by the graph's
// view, never synced: every peer makes its own. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptWireMesh : ProceduralMesh
{
    public const float Radius = 0.004f;
    private const int Sides = 10;
    public const int Rings = 32;

    // A pulse is a short glowing stretch remade every frame it moves, so it is a lighter tube than a wire:
    // a fifth of a wire's length needs no wire's worth of rings. -xlinka
    private const int PulseSides = 6;
    private const int PulseRings = 12;

    // Metres of wire one turn of a lengthwise pattern covers.
    public const float StreamSpacing = 0.12f;

    // Measures each wire along its length into UV0's u, in turns of StreamSpacing, so a pattern scrolled
    // along u runs down every wire at the same pace whatever its length. Set before the first wires.
    public bool Lengthwise { get; set; }

    private readonly List<ScriptWireCurve> _wires = new();
    private ScriptWireCurve[] _building = Array.Empty<ScriptWireCurve>();
    private PhosTriangleSubmesh? _submesh;
    private int _lastVertexCount = -1;
    private int _lastTriangleCount = -1;

    public int WireCount => _wires.Count;

    public void SetWires(IReadOnlyList<ScriptWireCurve> wires)
    {
        _wires.Clear();
        _wires.AddRange(wires);
        RegenerateMesh();
    }

    protected override void PrepareAssetUpdateData()
    {
        if (_building.Length != _wires.Count)
            _building = new ScriptWireCurve[_wires.Count];
        _wires.CopyTo(_building);
    }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        int vertexCount = 0;
        int triangleCount = 0;
        foreach (var wire in _building)
        {
            Shape(wire, out int rings, out int sides);
            vertexCount += rings * sides;
            triangleCount += (rings - 1) * sides * 2;
        }
        bool topologyChanged = _submesh == null || vertexCount != _lastVertexCount || triangleCount != _lastTriangleCount;
        if (topologyChanged)
        {
            mesh.Clear();
            mesh.HasNormals = true;
            mesh.HasColors = true;
            mesh.HasUV0s = Lengthwise;
            mesh.IncreaseVertexCount(vertexCount);
            _submesh = new PhosTriangleSubmesh(mesh);
            mesh.Submeshes.Add(_submesh);
            _submesh.SetCount(triangleCount);
            _lastVertexCount = vertexCount;
            _lastTriangleCount = triangleCount;
        }
        mesh.HasNormals = true;
        mesh.HasColors = true;

        int vertex = 0;
        int triangle = 0;
        foreach (var wire in _building)
            BuildWire(mesh, _submesh!, wire, Lengthwise, ref vertex, ref triangle);

        // Positions change when a card moves or a pulse advances, even with identical topology.
        uploadHint[MeshUploadHint.Flag.Geometry] = true;
        uploadHint[MeshUploadHint.Flag.Dynamic] = true;
        uploadHint[MeshUploadHint.Flag.Normals] = true;
        uploadHint[MeshUploadHint.Flag.Colors] = true;
        uploadHint[MeshUploadHint.Flag.UV0] = Lengthwise;
    }

    protected override void ClearMeshData()
    {
        _submesh = null;
        _lastVertexCount = -1;
        _lastTriangleCount = -1;
    }

    private static void Shape(in ScriptWireCurve wire, out int rings, out int sides)
    {
        rings = wire.Pulse ? PulseRings : Rings;
        sides = wire.Pulse ? PulseSides : Sides;
    }

    // The curve's four control points. A wire leaves and arrives along +X, reaching further the further
    // apart its ends are; a straight one is a line.
    public static void ControlPoints(in ScriptWireCurve wire, out float3 p0, out float3 p1, out float3 p2, out float3 p3)
    {
        p0 = Finite(wire.From);
        p3 = Finite(wire.To);
        if (wire.Straight)
        {
            p1 = float3.Lerp(p0, p3, 1f / 3f);
            p2 = float3.Lerp(p0, p3, 2f / 3f);
            return;
        }
        float reach = MathF.Max(0.03f, MathF.Abs(p3.x - p0.x) * 0.5f + 0.02f);
        p1 = p0 + new float3(reach, 0f, 0f);
        p2 = p3 - new float3(reach, 0f, 0f);
    }

    // A point along a wire, t from its output end at 0 to its input end at 1.
    public static float3 PointOn(in ScriptWireCurve wire, float t)
    {
        ControlPoints(wire, out var p0, out var p1, out var p2, out var p3);
        return Bezier(p0, p1, p2, p3, System.Math.Clamp(t, 0f, 1f));
    }

    private static void BuildWire(PhosMesh mesh, PhosTriangleSubmesh submesh, in ScriptWireCurve wire, bool lengthwise, ref int vertex, ref int triangle)
    {
        ControlPoints(wire, out var p0, out var p1, out var p2, out var p3);
        Shape(wire, out int rings, out int sides);

        int first = vertex;
        float travelled = 0f;
        var previous = float3.Zero;
        for (int ring = 0; ring < rings; ring++)
        {
            float along = ring / (float)(rings - 1);
            float t = System.Math.Clamp(wire.Start + (wire.End - wire.Start) * along, 0f, 1f);
            var center = Bezier(p0, p1, p2, p3, t);
            if (ring > 0)
                travelled += (center - previous).Length;
            previous = center;
            var tangent = BezierTangent(p0, p1, p2, p3, t);
            Frame(tangent, out var normal, out var binormal);
            var tint = color.Lerp(wire.FromColor, wire.ToColor, t);
            float radius = Radius * wire.Width;
            if (wire.Pulse)
            {
                // Keep the envelope on the untrimmed segment so it enters and leaves the port
                // smoothly, instead of shrinking back into a dot at each end of the connection.
                float phase = (t - wire.Start) / MathF.Max(0.0001f, wire.End - wire.Start);
                float glow = MathF.Sin(phase * MathF.PI);
                tint.a *= glow * glow;
                radius *= 1.05f + 0.3f * glow;
            }
            for (int side = 0; side < sides; side++)
            {
                float angle = MathF.PI * 2f * side / sides;
                var radial = normal * MathF.Cos(angle) + binormal * MathF.Sin(angle);
                mesh.RawPositions[vertex] = center + radial * radius;
                mesh.RawNormals[vertex] = radial;
                mesh.RawColors[vertex] = tint;
                if (lengthwise)
                    mesh.RawUV0s[vertex] = new float2(travelled / StreamSpacing, side / (float)sides);
                vertex++;
            }
        }

        for (int ring = 0; ring < rings - 1; ring++)
        {
            int row = first + ring * sides;
            int next = row + sides;
            for (int side = 0; side < sides; side++)
            {
                int nextSide = (side + 1) % sides;
                submesh.SetTriangle(triangle++, row + side, next + side, row + nextSide);
                submesh.SetTriangle(triangle++, row + nextSide, next + side, next + nextSide);
            }
        }
    }

    private static float3 Bezier(in float3 p0, in float3 p1, in float3 p2, in float3 p3, float t)
    {
        float u = 1f - t;
        return p0 * (u * u * u) + p1 * (3f * u * u * t) + p2 * (3f * u * t * t) + p3 * (t * t * t);
    }

    private static float3 BezierTangent(in float3 p0, in float3 p1, in float3 p2, in float3 p3, float t)
    {
        float u = 1f - t;
        var d = (p1 - p0) * (3f * u * u) + (p2 - p1) * (6f * u * t) + (p3 - p2) * (3f * t * t);
        return d.LengthSquared > 1e-12f ? d.Normalized : float3.Right;
    }

    private static void Frame(in float3 tangent, out float3 normal, out float3 binormal)
    {
        var up = MathF.Abs(float3.Dot(tangent, float3.Forward)) > 0.95f ? float3.Up : float3.Forward;
        normal = float3.Cross(up, tangent);
        normal = normal.LengthSquared > 1e-12f ? normal.Normalized : float3.Up;
        binormal = float3.Cross(tangent, normal);
        binormal = binormal.LengthSquared > 1e-12f ? binormal.Normalized : float3.Forward;
    }

    private static float3 Finite(in float3 value)
        => new(float.IsFinite(value.x) ? value.x : 0f, float.IsFinite(value.y) ? value.y : 0f, float.IsFinite(value.z) ? value.z : 0f);
}
