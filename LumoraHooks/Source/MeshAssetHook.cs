// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Godot;
using Lumora.Core.Assets;
using Lumora.Core.Phos;

namespace Lumora.Godot.Hooks;

// Godot implementation of mesh asset hook.
// Creates and manages Godot ArrayMesh resources.
public class MeshAssetHook : AssetHook, IMeshAssetHook
{
    private ArrayMesh _godotMesh = null!;
    private bool _loggedSurfaceOverflow;
    private bool _unloaded;
    private long _uploadSerial;
    private long _appliedSerial;

    // RenderingServer refuses surface 257 and up (MAX_MESH_SURFACES), one engine error per rejected surface.
    // Stop at the ceiling and say it once instead. -xlinka
    private const int MaxGodotMeshSurfaces = 256;

    // Get the Godot ArrayMesh.
    public ArrayMesh GodotMesh => _godotMesh;

    // Whether the mesh is valid and usable.
    public bool IsValid => _godotMesh != null;

    private sealed class MeshUpload
    {
        public long Serial;
        public List<global::Godot.Collections.Array> Surfaces = new();
        public int Dropped;
        public int SubmeshCount;
        public MeshLodSet? Lods;
    }

    // Upload PhosMesh data to the Godot mesh.
    public void UploadMesh(PhosMesh mesh)
    {
        if (mesh == null || mesh.VertexCount == 0) return;

        var upload = BuildSurfaces(mesh);
        if (upload == null) return;

        // Everything up to here is plain array conversion, so it stays on the caller's thread; only the
        // AddSurfaceFromArrays at the end touches the RenderingServer and has to be deferred to the main
        // thread. A URL mesh reaches us from the asset load thread, which is where we want the LOD bake
        // too - it costs tens of milliseconds and must never land on the frame loop. -xlinka
        if (!ShouldBakeLods(mesh))
        {
            Commit(upload);
            return;
        }

        string label = DescribeSource();
        if (IsMainThread())
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                upload.Lods = ResolveLods(mesh, upload, label);
                Commit(upload);
            });
            return;
        }

        upload.Lods = ResolveLods(mesh, upload, label);
        Commit(upload);
    }

    private void Commit(MeshUpload upload) =>
        global::Godot.Callable.From(() => ApplyUpload(upload)).CallDeferred();

    // LOD levels are attached when a surface is created and there is no API to add them afterwards, so a
    // "render now, swap in the levels later" split would have to clear and re-add every surface. Clearing
    // an ArrayMesh's surfaces makes every MeshInstance3D holding it drop its per-surface material
    // overrides, which is how you get a world that finishes loading and then turns default-grey. So the
    // levels are resolved before the one and only build. On a cache hit that costs a file read; on a miss
    // the mesh appears a few frames later than it would have, on a thread nobody is waiting on. -xlinka
    private MeshLodSet? ResolveLods(PhosMesh mesh, MeshUpload upload, string label)
    {
        if (_unloaded) return null;
        var directory = MeshLodCache.GetDirectory();
        var key = MeshLodCache.BuildKey(mesh);
        return MeshLodCache.Resolve(directory, key, upload.Surfaces, label);
    }

    private bool ShouldBakeLods(PhosMesh mesh)
    {
        if (!MeshLodCache.IsBakeableAsset(asset))
            return false;
        if (!MeshLodCache.IsBakeableGeometry(mesh, out _, out _))
            return false;
        // The unindexed fallback surface below has no index buffer to reduce.
        return mesh.Submeshes.Count > 0;
    }

    private string DescribeSource() =>
        (asset as Asset)?.AssetURL?.ToString() ?? asset?.GetType().Name ?? "mesh";

    private static bool IsMainThread() => OS.GetThreadCallerId() == OS.GetMainThreadId();

    // Converts the mesh to Godot surface arrays. No RenderingServer contact, so it runs wherever the
    // caller is.
    private MeshUpload? BuildSurfaces(PhosMesh mesh)
    {
        var upload = new MeshUpload
        {
            Serial = System.Threading.Interlocked.Increment(ref _uploadSerial),
            SubmeshCount = mesh.Submeshes.Count,
        };

        foreach (var submesh in mesh.Submeshes)
        {
            if (submesh.IndexCount == 0) continue;

            if (upload.Surfaces.Count >= MaxGodotMeshSurfaces)
            {
                upload.Dropped++;
                continue;
            }

            var arrays = BuildSurfaceArrays(mesh, submesh);
            if (arrays != null)
                upload.Surfaces.Add(arrays);
        }

        // If no submeshes but we have vertex data, create a single surface
        if (mesh.Submeshes.Count == 0 && mesh.VertexCount > 0)
        {
            if ((mesh.VertexCount % 3) != 0)
            {
                Lumora.Core.Logging.Logger.Warn($"MeshAssetHook.UploadMesh: Skipping surface - no indices and vertex count {mesh.VertexCount} is not a multiple of 3");
                return null;
            }

            var arrays = BuildSurfaceArraysNoIndices(mesh);
            if (arrays != null)
                upload.Surfaces.Add(arrays);
        }

        return upload;
    }

    private void ApplyUpload(MeshUpload upload)
    {
        if (_unloaded) return;

        // Two uploads can be in flight when one of them stopped to bake. Whichever mesh data is newest
        // wins regardless of which finished first.
        if (upload.Serial < _appliedSerial) return;
        _appliedSerial = upload.Serial;

        if (_godotMesh == null)
        {
            _godotMesh = new ArrayMesh();
        }
        else
        {
            _godotMesh.ClearSurfaces();
        }

        for (int i = 0; i < upload.Surfaces.Count; i++)
        {
            var lods = upload.Lods?.For(i)?.ToGodot();
            _godotMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, upload.Surfaces[i], null, lods);
        }

        if (upload.Dropped > 0)
        {
            if (!_loggedSurfaceOverflow)
            {
                _loggedSurfaceOverflow = true;
                Lumora.Core.Logging.Logger.Warn($"MeshAssetHook.UploadMesh: mesh has {upload.SubmeshCount} submeshes but Godot caps a mesh at {MaxGodotMeshSurfaces} surfaces - {upload.Dropped} surface(s) past the cap were dropped and will not render.");
            }
        }
        else
        {
            _loggedSurfaceOverflow = false;
        }
    }

    private global::Godot.Collections.Array BuildSurfaceArrays(PhosMesh mesh, PhosSubmesh submesh)
    {
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);

        int vertexCount = mesh.VertexCount;

        // Positions (required)
        var rawPositions = mesh.RawPositions;
        if (rawPositions != null && rawPositions.Length > 0)
        {
            var positions = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var pos = rawPositions[i];
                positions[i] = new Vector3(pos.x, pos.y, pos.z);
            }
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
        }

        // Normals
        var rawNormals = mesh.RawNormals;
        if (rawNormals != null && rawNormals.Length > 0)
        {
            var normals = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var normal = rawNormals[i];
                normals[i] = new Vector3(normal.x, normal.y, normal.z);
            }
            arrays[(int)Mesh.ArrayType.Normal] = normals;
        }

        // Tangents
        var rawTangents = mesh.RawTangents;
        if (rawTangents != null && rawTangents.Length > 0)
        {
            var tangents = new float[vertexCount * 4];
            for (int i = 0; i < vertexCount; i++)
            {
                var t = rawTangents[i];
                tangents[i * 4] = t.x;
                tangents[i * 4 + 1] = t.y;
                tangents[i * 4 + 2] = t.z;
                tangents[i * 4 + 3] = t.w;
            }
            arrays[(int)Mesh.ArrayType.Tangent] = tangents;
        }

        // UV0
        var rawUV0 = mesh.RawUV0s;
        if (rawUV0 != null && rawUV0.Length > 0)
        {
            var uvs = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var uv = rawUV0[i];
                uvs[i] = new Vector2(uv.x, uv.y);
            }
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        }

        // UV1
        var rawUV1 = mesh.RawUV1s;
        if (rawUV1 != null && rawUV1.Length > 0)
        {
            var uvs = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var uv = rawUV1[i];
                uvs[i] = new Vector2(uv.x, uv.y);
            }
            arrays[(int)Mesh.ArrayType.TexUV2] = uvs;
        }

        // Vertex colors
        var rawColors = mesh.RawColors;
        if (rawColors != null && rawColors.Length > 0)
        {
            var colors = new Color[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var c = rawColors[i];
                colors[i] = new Color(c.r, c.g, c.b, c.a);
            }
            arrays[(int)Mesh.ArrayType.Color] = colors;
        }

        // Bone indices + weights (skinning). Godot wants 4 influences per vertex: a PackedInt32 bones array
        // and a PackedFloat32 weights array, both sized vertexCount*4. The skeleton + Skin (bind poses) that
        // make these meaningful are built on the renderer side from the asset's bone table. -xlinka
        if (mesh.HasBoneBindings)
        {
            var bindings = mesh.RawBoneBindings;
            if (bindings != null && bindings.Length >= vertexCount)
            {
                var bones = new int[vertexCount * 4];
                var weights = new float[vertexCount * 4];
                for (int i = 0; i < vertexCount; i++)
                {
                    var b = bindings[i];
                    bones[i * 4 + 0] = (int)b.boneIndices.x;
                    bones[i * 4 + 1] = (int)b.boneIndices.y;
                    bones[i * 4 + 2] = (int)b.boneIndices.z;
                    bones[i * 4 + 3] = (int)b.boneIndices.w;
                    weights[i * 4 + 0] = b.boneWeights.x;
                    weights[i * 4 + 1] = b.boneWeights.y;
                    weights[i * 4 + 2] = b.boneWeights.z;
                    weights[i * 4 + 3] = b.boneWeights.w;
                }
                arrays[(int)Mesh.ArrayType.Bones] = bones;
                arrays[(int)Mesh.ArrayType.Weights] = weights;
            }
        }

        // Indices from submesh
        var rawIndices = submesh.RawIndices;
        if (rawIndices != null && rawIndices.Length > 0)
        {
            var indices = new int[submesh.IndexCount];
            System.Array.Copy(rawIndices, indices, submesh.IndexCount);
            arrays[(int)Mesh.ArrayType.Index] = indices;
        }

        return arrays;
    }

    private global::Godot.Collections.Array? BuildSurfaceArraysNoIndices(PhosMesh mesh)
    {
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);

        int vertexCount = mesh.VertexCount;

        // Positions (required)
        var rawPositions = mesh.RawPositions;
        if (rawPositions == null || rawPositions.Length == 0) return null;

        var positions = new Vector3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
        {
            var pos = rawPositions[i];
            positions[i] = new Vector3(pos.x, pos.y, pos.z);
        }
        arrays[(int)Mesh.ArrayType.Vertex] = positions;

        // Normals
        var rawNormals = mesh.RawNormals;
        if (rawNormals != null && rawNormals.Length > 0)
        {
            var normals = new Vector3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var normal = rawNormals[i];
                normals[i] = new Vector3(normal.x, normal.y, normal.z);
            }
            arrays[(int)Mesh.ArrayType.Normal] = normals;
        }

        // UV0
        var rawUV0 = mesh.RawUV0s;
        if (rawUV0 != null && rawUV0.Length > 0)
        {
            var uvs = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                var uv = rawUV0[i];
                uvs[i] = new Vector2(uv.x, uv.y);
            }
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        }

        return arrays;
    }

    // Unload and dispose the Godot mesh.
    public override void Unload()
    {
        // A bake that is still running will finish and post its apply; the flag is what stops it
        // resurrecting a mesh for an asset that is gone.
        _unloaded = true;
        if (_godotMesh != null)
        {
            _godotMesh.Dispose();
            _godotMesh = null!;
        }
    }
}
