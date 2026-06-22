// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using System.Threading.Tasks;
using Lumora.Core.Math;
using Lumora.Core.Phos;

namespace Lumora.Core.Assets;

/// <summary>
/// Asset containing mesh geometry data. URL instances gather and decode their file in
/// <see cref="LoadSelf"/>; procedural instances are created via <c>InitializeDynamic</c> and fed
/// geometry through <see cref="SetMeshData"/>.
/// </summary>
public class MeshDataAsset : ImplementableAsset<IMeshAssetHook>
{
    private PhosMesh _meshData = null!;
    private BoundingBox _bounds;
    private bool _keepReadable;

    /// <summary>
    /// The mesh geometry data.
    /// </summary>
    public PhosMesh MeshData => _meshData;

    /// <summary>
    /// Bounding box of the mesh.
    /// </summary>
    public BoundingBox Bounds => _bounds;

    /// <summary>
    /// Number of vertices in the mesh.
    /// </summary>
    public int VertexCount => _meshData?.VertexCount ?? 0;

    // Skinning surface - lets a skinned renderer rebind off the asset's own bone table + blendshape
    // names instead of carrying that data as per-element synced lists. Returns empties until the decoder
    // populates them (the bake/glTF-skinning producer is the next step). -xlinka

    /// <summary>Number of bones in the mesh's skeleton table.</summary>
    public int BoneCount => _meshData?.BoneCount ?? 0;

    /// <summary>Bone name at the given index, or null if out of range.</summary>
    public string? GetBoneName(int index) =>
        (_meshData != null && index >= 0 && index < _meshData.BoneCount) ? _meshData.GetBoneName(index) : null;

    /// <summary>Bone bind pose at the given index (identity if out of range).</summary>
    public Lumora.Core.Math.float4x4 GetBoneBindPose(int index) =>
        (_meshData != null && index >= 0 && index < _meshData.BoneCount) ? _meshData.GetBoneBindPose(index) : Lumora.Core.Math.float4x4.Identity;

    /// <summary>Number of blend shapes on the mesh.</summary>
    public int BlendShapeCount => _meshData?.BlendShapeCount ?? 0;

    /// <summary>Blend shape name at the given index, or null if out of range.</summary>
    public string? GetBlendShapeName(int index) =>
        (_meshData != null && index >= 0 && index < _meshData.BlendShapeCount) ? _meshData.BlendShapes[index].Name : null;

    /// <summary>Index of the named blend shape, or -1 if absent.</summary>
    public int BlendShapeIndex(string name) => _meshData?.BlendShapeIndex(name) ?? -1;

    /// <summary>
    /// Number of triangles in the mesh.
    /// </summary>
    public int TriangleCount
    {
        get
        {
            if (_meshData == null || _meshData.Submeshes.Count == 0) return 0;
            int total = 0;
            foreach (var submesh in _meshData.Submeshes)
            {
                total += submesh.IndexCount / 3;
            }
            return total;
        }
    }

    /// <summary>
    /// Whether to keep mesh data readable after upload to GPU.
    /// </summary>
    public bool KeepReadable
    {
        get => _keepReadable;
        set => _keepReadable = value;
    }

    /// <summary>
    /// Gather and decode this mesh from its URL. Only runs for URL (static) instances;
    /// procedural instances set their data directly via <see cref="SetMeshData"/>.
    /// </summary>
    protected override async Task LoadSelf()
    {
        var bytes = await AssetManager.RequestGather(AssetURL).ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0)
        {
            FailLoad($"No mesh data gathered for {AssetURL}");
            return;
        }

        var descriptor = TargetVariant as MeshVariantDescriptor ?? MeshVariantDescriptor.Default;
        string ext = Path.GetExtension(AssetURL.IsFile ? AssetURL.LocalPath : AssetURL.AbsolutePath) ?? "";

        var mesh = MeshDecoder.Decode(bytes, ext);
        if (mesh == null)
        {
            FailLoad($"Failed to decode mesh {AssetURL}");
            return;
        }

        if (System.Math.Abs(descriptor.ImportScale - 1.0f) > 0.0001f)
        {
            MeshDecoder.ScaleMesh(mesh, descriptor.ImportScale);
        }

        _keepReadable = descriptor.KeepReadable;
        SetMeshData(mesh);
    }

    /// <summary>
    /// Set the mesh data.
    /// </summary>
    /// <param name="mesh">The PhosMesh containing geometry data</param>
    public void SetMeshData(PhosMesh mesh)
    {
        _meshData = mesh;
        _bounds = mesh?.CalculateBoundingBox() ?? new BoundingBox();
        Version++;

        // Upload to hook if available
        if (Hook != null && mesh != null)
        {
            Hook.UploadMesh(mesh);
        }
    }

    /// <summary>
    /// Set a custom bounding box override.
    /// </summary>
    public void SetBounds(BoundingBox bounds)
    {
        _bounds = bounds;
    }

    /// <summary>
    /// Recalculate bounding box from mesh data.
    /// </summary>
    public void RecalculateBounds()
    {
        if (_meshData != null)
        {
            _bounds = _meshData.CalculateBoundingBox();
        }
    }

    /// <summary>
    /// Create a MeshDataAsset from a PhosMesh.
    /// </summary>
    public static MeshDataAsset FromPhosMesh(PhosMesh mesh)
    {
        var asset = new MeshDataAsset();
        asset.InitializeDynamic();
        asset.SetMeshData(mesh);
        return asset;
    }

    public override void Unload()
    {
        if (!_keepReadable)
        {
            _meshData?.Clear();
        }
        _meshData = null!;
        _bounds = new BoundingBox();
        base.Unload();
    }
}
