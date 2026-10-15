// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Components.Meshes;

// A mesh something else bends: it starts as the mesh plugged into Mesh (a procedural mesh, an imported one, or the
// one a renderer draws) and a deformer such as a soft body moves its vertices every frame. Everything the source
// carries comes along (UVs, colours, tangents, every submesh), and its normals and tangents turn with the surface
// as it bends instead of being worked out again from scratch, so hard edges and seams look as they did.
//
// A deformer can also hand in a shape of its own, a part of a skinned mesh for instance, which then stands in for
// Mesh on this machine. Left with neither, it draws nothing. -xlinka
[ComponentCategory("Assets/Procedural Meshes")]
public class DeformableMesh : ProceduralMesh
{
    public readonly SyncRef<Component> Mesh;

    // The shape being bent, kept as plain arrays: a copy of Mesh, or what a deformer handed in.
    private float3[] _basePositions = Array.Empty<float3>();
    private float3[]? _baseNormals;
    private float4[]? _baseTangents;
    private color[]? _baseColors;
    private float2[]? _baseUV0;
    private float2[]? _baseUV1;
    private readonly List<int[]> _baseSubmeshes = new();
    private int[] _baseTriangles = Array.Empty<int>();
    private int[] _spot = Array.Empty<int>();            // one id per position, shared by seam copies
    private int _spotCount;
    private float3[]? _baseFacing;                         // the base's own surface facing per spot
    private bool _handedIn;
    private int _baseVersion;
    private string? _sourceProblem;

    // Where the vertices are now, and the shape their normals are measured against.
    private float3[] _positions = Array.Empty<float3>();
    private float3[]? _referencePositions;
    private float3[]? _referenceNormals;
    private float4[]? _referenceTangents;
    private bool _bent;
    private float3[] _facing = Array.Empty<float3>();
    private float3[] _referenceFacing = Array.Empty<float3>();
    private bool _builtOutput;

    // The old way in: positions, a triangle list and UVs, with normals worked out from the positions every update.
    private bool _legacy;
    private float3[] _legacyNormals = Array.Empty<float3>();

    private readonly MeshSourceWatch _watch;
    private ProceduralMesh? _watchedProcedural;
    private int _sourceRetries;

    public DeformableMesh()
    {
        Mesh = new SyncRef<Component>(this);
        _watch = new MeshSourceWatch(() =>
        {
            if (!IsDestroyed)
                ReadSource();
        });
    }

    // Raised when the shape being bent changes (a new source, the source rebuilt, a shape handed in). A deformer
    // builds its body again from it; the vertex order of everything below changes with it.
    public event Action<DeformableMesh>? BaseChanged;

    public int BaseVersion => _baseVersion;

    public bool HasBase => _basePositions.Length > 0 && _baseTriangles.Length > 0;

    public int VertexCount => _basePositions.Length;

    // Read only. Base positions in this mesh's own space.
    public float3[] BasePositions => _basePositions;

    // Every triangle of every submesh, in submesh order.
    public int[] BaseTriangles => _baseTriangles;

    public int BaseSubmeshCount => _baseSubmeshes.Count;

    // Why Mesh gives nothing to bend, or null.
    public string? SourceProblem => _sourceProblem;

    public override void OnAwake()
    {
        base.OnAwake();
        Mesh.OnChanged += _ =>
        {
            _sourceRetries = 0;
            ReadSource();
        };
    }

    public override void OnStart()
    {
        base.OnStart();
        ReadSource();
    }

    public override void OnDestroy()
    {
        _watch.Watch(null);
        WatchProcedural(null);
        base.OnDestroy();
    }

    // LEGACY GEOMETRY

    // Topology and first positions, local space; normals follow from the positions.
    public void SetGeometry(float3[] positions, int[] indices, float2[]? uvs)
    {
        _legacy = true;
        _handedIn = true;
        _basePositions = positions;
        _baseTriangles = indices;
        _baseUV0 = uvs != null && uvs.Length == positions.Length ? uvs : null;
        _baseNormals = null;
        _baseTangents = null;
        _baseColors = null;
        _baseUV1 = null;
        _baseSubmeshes.Clear();
        _baseSubmeshes.Add(indices);
        _positions = (float3[])positions.Clone();
        _legacyNormals = new float3[positions.Length];
        RecomputeLegacyNormals();
        _bent = true;
        _baseVersion++;
        _builtOutput = false;
        if (phosMesh != null)
            RegenerateMesh();
        BaseChanged?.Invoke(this);
    }

    // A shape of the deformer's own, standing in for Mesh on this machine until cleared.
    public void SetGeometry(PhosMesh shape)
    {
        _legacy = false;
        _handedIn = true;
        CopyBase(shape);
        Rebased();
    }

    // Back to whatever Mesh holds.
    public void ClearHandedGeometry()
    {
        if (!_handedIn)
            return;
        _handedIn = false;
        _legacy = false;
        ReadSource();
    }

    // SOURCE

    private void ReadSource()
    {
        if (IsDestroyed || World == null || _handedIn)
            return;

        var target = Mesh.Target;
        _watch.Watch(MeshSource.Provider(target));
        WatchProcedural(MeshSource.MeshOf(target) as ProceduralMesh);

        _sourceProblem = target == null ? null : MeshSource.Refusal(target, keepsRenderers: false);
        if (_sourceProblem == null && FeedsBackInto(target))
            _sourceProblem = "this mesh is built from itself";
        if (_sourceProblem != null)
        {
            ClearBase();
            return;
        }

        var geometry = MeshSource.Geometry(target);
        if (geometry == null || geometry.VertexCount == 0)
        {
            ClearBase();
            // A procedural mesh builds on its own first update and says so; asset meshes say so through the watch.
            // This only covers a source that exists but has not built yet and does not announce it.
            if (target != null && _sourceRetries++ < 60)
                World.RunInUpdates(10, ReadSource);
            return;
        }

        CopyBase(geometry);
        Rebased();
    }

    private bool FeedsBackInto(Component? target)
    {
        var walk = MeshSource.MeshOf(target);
        for (int depth = 0; walk != null && depth < 16; depth++)
        {
            if (ReferenceEquals(walk, this))
                return true;
            walk = walk is DeformableMesh deformable ? MeshSource.MeshOf(deformable.Mesh.Target) : null;
        }
        return false;
    }

    private void WatchProcedural(ProceduralMesh? procedural)
    {
        if (ReferenceEquals(procedural, _watchedProcedural))
            return;
        if (_watchedProcedural != null)
            _watchedProcedural.Regenerated -= OnSourceRegenerated;
        _watchedProcedural = procedural;
        if (procedural != null && !ReferenceEquals(procedural, this))
            procedural.Regenerated += OnSourceRegenerated;
    }

    private void OnSourceRegenerated(ProceduralMesh source)
    {
        if (!IsDestroyed && !_handedIn)
            ReadSource();
    }

    private void ClearBase()
    {
        if (_basePositions.Length == 0 && !_legacy)
            return;
        _legacy = false;
        _basePositions = Array.Empty<float3>();
        _baseTriangles = Array.Empty<int>();
        _baseNormals = null;
        _baseTangents = null;
        _baseColors = null;
        _baseUV0 = null;
        _baseUV1 = null;
        _baseSubmeshes.Clear();
        _positions = Array.Empty<float3>();
        Rebased();
    }

    private void CopyBase(PhosMesh mesh)
    {
        int n = mesh.VertexCount;
        _basePositions = Slice(mesh.RawPositions, n) ?? Array.Empty<float3>();
        _baseNormals = mesh.HasNormals ? Slice(mesh.RawNormals, n) : null;
        _baseTangents = mesh.HasTangents ? Slice(mesh.RawTangents, n) : null;
        _baseColors = mesh.HasColors ? Slice(mesh.RawColors, n) : null;
        _baseUV0 = mesh.HasUV0s ? Slice(mesh.RawUV0s, n) : null;
        _baseUV1 = mesh.HasUV1s ? Slice(mesh.RawUV1s, n) : null;

        // Only submeshes that draw: a surface is made per drawing triangle submesh, and materials count them that way.
        _baseSubmeshes.Clear();
        int total = 0;
        foreach (var submesh in mesh.Submeshes)
        {
            if (submesh is not PhosTriangleSubmesh || submesh.IndexCount < 3)
                continue;
            var indices = new int[submesh.IndexCount - submesh.IndexCount % 3];
            Array.Copy(submesh.RawIndices, indices, indices.Length);
            _baseSubmeshes.Add(indices);
            total += indices.Length;
        }
        _baseTriangles = new int[total];
        int at = 0;
        foreach (var indices in _baseSubmeshes)
        {
            Array.Copy(indices, 0, _baseTriangles, at, indices.Length);
            at += indices.Length;
        }
        _positions = (float3[])_basePositions.Clone();
    }

    private static T[]? Slice<T>(T[]? array, int n)
    {
        if (array == null || array.Length < n)
            return null;
        var copy = new T[n];
        Array.Copy(array, copy, n);
        return copy;
    }

    private void Rebased()
    {
        _bent = false;
        _referencePositions = null;
        _referenceNormals = null;
        _referenceTangents = null;
        BuildSpots();
        _baseFacing = null;
        _baseVersion++;
        _builtOutput = false;
        if (phosMesh != null)
            RegenerateMesh();
        BaseChanged?.Invoke(this);
    }

    // Seam copies share an id, so the facing worked out at one is the facing at all of them and a seam never shows a
    // crease the source did not have.
    private void BuildSpots()
    {
        int n = _basePositions.Length;
        _spot = new int[n];
        var bySpot = new Dictionary<(int, int, int), int>(n);
        for (int v = 0; v < n; v++)
        {
            var p = _basePositions[v];
            var key = (BitConverter.SingleToInt32Bits(p.x + 0f), BitConverter.SingleToInt32Bits(p.y + 0f), BitConverter.SingleToInt32Bits(p.z + 0f));
            if (!bySpot.TryGetValue(key, out int spot))
            {
                spot = bySpot.Count;
                bySpot[key] = spot;
            }
            _spot[v] = spot;
        }
        _spotCount = bySpot.Count;
    }

    // BENDING

    // New vertex positions, this mesh's space. Normals and tangents turn from the base's with the surface.
    public void UpdatePositions(float3[] positions)
    {
        if (positions.Length != _basePositions.Length || _basePositions.Length == 0)
            return;
        Array.Copy(positions, _positions, positions.Length);
        _referencePositions = null;
        _referenceNormals = null;
        _referenceTangents = null;
        _bent = true;
        if (_legacy)
            RecomputeLegacyNormals();
        RegenerateQuietly();
    }

    // New positions measured against a shape that moves too, a posed skin: normals and tangents start from the
    // reference's and turn with the difference between the two surfaces.
    public void UpdatePositions(float3[] positions, float3[] referencePositions, float3[] referenceNormals, float4[]? referenceTangents)
    {
        int n = _basePositions.Length;
        if (_legacy || n == 0 || positions.Length != n || referencePositions.Length != n || referenceNormals.Length != n)
            return;
        Array.Copy(positions, _positions, n);
        _referencePositions = referencePositions;
        _referenceNormals = referenceNormals;
        _referenceTangents = referenceTangents != null && referenceTangents.Length == n ? referenceTangents : null;
        _bent = true;
        RegenerateQuietly();
    }

    // Back to the base shape.
    public void ResetPositions()
    {
        if (_basePositions.Length == 0)
            return;
        Array.Copy(_basePositions, _positions, _basePositions.Length);
        _referencePositions = null;
        _referenceNormals = null;
        _referenceTangents = null;
        _bent = false;
        if (_legacy)
            RecomputeLegacyNormals();
        RegenerateQuietly();
    }

    private void RecomputeLegacyNormals()
    {
        var normals = _legacyNormals;
        var positions = _positions;
        var indices = _baseTriangles;
        for (int i = 0; i < normals.Length; i++)
            normals[i] = float3.Zero;
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            var n = float3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += n; normals[b] += n; normals[c] += n;
        }
        for (int i = 0; i < normals.Length; i++)
            normals[i] = normals[i].LengthSquared > 1e-10f ? normals[i].Normalized : float3.Up;
    }

    // Area-weighted facing per spot.
    private void Facing(float3[] positions, ref float3[] facing)
    {
        if (facing.Length != _spotCount)
            facing = new float3[_spotCount];
        else
            Array.Clear(facing, 0, facing.Length);
        var indices = _baseTriangles;
        var spot = _spot;
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            var n = float3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            facing[spot[a]] += n;
            facing[spot[b]] += n;
            facing[spot[c]] += n;
        }
        for (int i = 0; i < facing.Length; i++)
            facing[i] = facing[i].LengthSquared > 1e-20f ? facing[i].Normalized : float3.Zero;
    }

    // The smallest turn taking unit u onto unit v, applied to n.
    private static float3 Turn(in float3 n, in float3 u, in float3 v)
    {
        float c = float3.Dot(u, v);
        if (c <= -0.999f)
            return n;
        var w = float3.Cross(u, v);
        return n * c + float3.Cross(w, n) + w * (float3.Dot(w, n) / (1f + c));
    }

    // MESH

    protected override void PrepareAssetUpdateData() { }

    protected override void UpdateMeshData(PhosMesh mesh)
    {
        uploadHint[MeshUploadHint.Flag.Geometry] = true;
        uploadHint[MeshUploadHint.Flag.Deforming] = _bent;
        int n = _basePositions.Length;
        if (n == 0 || _baseTriangles.Length == 0)
        {
            mesh.Clear();
            _builtOutput = false;
            return;
        }

        // The topology only changes with the base; positions and normals are written in place every other time.
        if (!_builtOutput || mesh.VertexCount != n || mesh.Submeshes.Count != _baseSubmeshes.Count)
        {
            mesh.Clear();
            mesh.HasNormals = true;
            mesh.HasTangents = !_legacy && _baseTangents != null;
            mesh.HasColors = !_legacy && _baseColors != null;
            mesh.HasUV0s = _baseUV0 != null;
            if (!_legacy && _baseUV1 != null)
                mesh.SetHasUV(1, true);
            mesh.IncreaseVertexCount(n);
            foreach (var indices in _baseSubmeshes)
            {
                var submesh = new PhosTriangleSubmesh(mesh);
                mesh.Submeshes.Add(submesh);
                for (int t = 0; t + 2 < indices.Length; t += 3)
                    submesh.AddTriangle(indices[t], indices[t + 1], indices[t + 2]);
            }
            if (_baseUV0 != null)
                Array.Copy(_baseUV0, mesh.RawUV0s, n);
            if (!_legacy && _baseUV1 != null)
                Array.Copy(_baseUV1, mesh.RawUV1s, n);
            if (mesh.HasColors)
                Array.Copy(_baseColors!, mesh.RawColors, n);
            _builtOutput = true;
        }

        Array.Copy(_positions, mesh.RawPositions, n);

        if (_legacy)
        {
            Array.Copy(_legacyNormals, mesh.RawNormals, n);
            return;
        }

        var normals = mesh.RawNormals;
        var tangents = mesh.HasTangents ? mesh.RawTangents : null;
        var referenceNormals = _referenceNormals ?? _baseNormals;
        var referenceTangents = _referenceNormals != null ? _referenceTangents : _baseTangents;

        if (!_bent && _baseNormals != null)
        {
            Array.Copy(_baseNormals, normals, n);
            if (tangents != null)
                Array.Copy(_baseTangents!, tangents, n);
            return;
        }

        Facing(_positions, ref _facing);
        if (referenceNormals == null)
        {
            for (int v = 0; v < n; v++)
            {
                var f = _facing[_spot[v]];
                normals[v] = f.LengthSquared > 0.5f ? f : float3.Up;
            }
            return;
        }

        float3[] from;
        if (_referencePositions != null)
        {
            Facing(_referencePositions, ref _referenceFacing);
            from = _referenceFacing;
        }
        else
        {
            if (_baseFacing == null)
            {
                var facing = Array.Empty<float3>();
                Facing(_basePositions, ref facing);
                _baseFacing = facing;
            }
            from = _baseFacing;
        }

        for (int v = 0; v < n; v++)
        {
            int s = _spot[v];
            var u = from[s];
            var w = _facing[s];
            var normal = referenceNormals[v];
            if (u.LengthSquared < 0.5f || w.LengthSquared < 0.5f)
            {
                normals[v] = normal;
                if (tangents != null)
                    tangents[v] = referenceTangents != null ? referenceTangents[v] : float4.Zero;
                continue;
            }
            var turned = Turn(normal, u, w);
            normals[v] = turned.LengthSquared > 1e-12f ? turned.Normalized : normal;
            if (tangents != null && referenceTangents != null)
            {
                var t = referenceTangents[v];
                var tt = Turn(new float3(t.x, t.y, t.z), u, w);
                tangents[v] = new float4(tt.x, tt.y, tt.z, t.w);
            }
        }
    }

    // A wireframe bake rebuilds the buffers from scratch; the base and the bend stay as they are.
    protected override void ClearMeshData()
    {
        _builtOutput = false;
    }

    public override void BuildInspectorBody(UIBuilder ui)
    {
        if (_sourceProblem != null)
            InspectorStats.AddRow(ui, "Mesh", $"not used: {_sourceProblem}");
        else if (_handedIn)
            InspectorStats.AddRow(ui, "Shape", "handed in by the deformer driving it");
        else if (Mesh.Target != null && !HasBase)
            InspectorStats.AddRow(ui, "Mesh", "waiting for the mesh to load");
        base.BuildInspectorBody(ui);
    }
}
