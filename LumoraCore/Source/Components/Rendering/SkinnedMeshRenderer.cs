// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

/// <summary>
/// Renders a skinned mesh that deforms based on a skeleton's bone transforms.
/// Stores direct bone slot references for skeletal mesh deformation.
/// </summary>
[ComponentCategory("Rendering")]
public class SkinnedMeshRenderer : ImplementableComponent
{
    // BONE REFERENCES

    /// <summary>
    /// Direct references to bone slots in order matching mesh bone indices.
    /// Bone index 0 in mesh data corresponds to Bones[0], etc.
    /// </summary>
    public SyncRefList<Slot> Bones { get; private set; } = null!;

    /// <summary>
    /// Names of bones in order (for debugging and lookup).
    /// </summary>
    public SyncFieldList<string> BoneNames { get; private set; } = null!;

    /// <summary>
    /// Reference to the SkeletonBuilder (for skeleton binding).
    /// </summary>
    public readonly SyncRef<SkeletonBuilder> Skeleton = null!;

    /// <summary>
    /// Phos mesh asset source (the universal pipeline). When set, the hook sources geometry, bone bindings AND
    /// bind poses from this content-hashed asset and drives skinning through an explicit Skin - instead of the
    /// inline Vertices/BoneIndices/BoneWeights lists below (which exist for the legacy Godot-glTF import path).
    /// This is what lets a skinned avatar travel as one asset instead of thousands of synced elements.
    /// MUST be an AssetRef, not a plain SyncRef: an AssetRef reference-counts the MeshProvider (so its underlying
    /// MeshDataAsset is actually requested + decoded - a StaticAssetProvider only loads while AssetReferenceCount
    /// > 0) AND re-fires ApplyChanges when the async decode finishes (AssetRef.AssetUpdated). A bare SyncRef did
    /// NEITHER, so the provider never loaded and a Phos-imported avatar rendered as nothing. -xlinka
    /// </summary>
    public readonly AssetRef<MeshDataAsset> MeshAsset = null!;

    // MESH DATA

    /// <summary>
    /// Mesh vertex positions (XYZ coordinates).
    /// </summary>
    public SyncFieldList<float3> Vertices { get; private set; } = null!;

    /// <summary>
    /// Mesh normals for lighting calculations.
    /// </summary>
    public SyncFieldList<float3> Normals { get; private set; } = null!;

    /// <summary>
    /// UV texture coordinates.
    /// </summary>
    public SyncFieldList<float2> UVs { get; private set; } = null!;

    /// <summary>
    /// Triangle indices (3 indices per triangle).
    /// </summary>
    public SyncFieldList<int> Indices { get; private set; } = null!;

    /// <summary>
    /// Bone indices for each vertex (up to 4 bones per vertex).
    /// These indices reference bones in the Bones list.
    /// </summary>
    public SyncFieldList<int4> BoneIndices { get; private set; } = null!;

    /// <summary>
    /// Bone weights for each vertex (up to 4 bones per vertex).
    /// </summary>
    public SyncFieldList<float4> BoneWeights { get; private set; } = null!;

    // BLENDSHAPES (morph targets, e.g. facial expressions / visemes / blink)

    /// <summary>Blendshape names, one per shape (mesh-level, shared across surfaces of a mesh).</summary>
    public SyncFieldList<string> BlendShapeNames { get; private set; } = null!;

    /// <summary>
    /// Per-shape vertex positions, flattened: shape k occupies [k*VertexCount, (k+1)*VertexCount).
    /// Stored exactly as the source mesh reported them so the round-trip honors <see cref="BlendShapeMode"/>.
    /// </summary>
    public SyncFieldList<float3> BlendShapeVertices { get; private set; } = null!;

    /// <summary>
    /// Per-shape vertex NORMAL deltas, flattened like <see cref="BlendShapeVertices"/>. Optional - empty when the
    /// source carried no normal morph data; when present the hook morphs normals too, so lighting follows the
    /// expression (matches how the morph was authored, with per-frame normal deltas). -xlinka
    /// </summary>
    public SyncFieldList<float3> BlendShapeNormals { get; private set; } = null!;

    /// <summary>True when normal morph deltas are present for every shape vertex (so the hook morphs normals).</summary>
    public bool HasBlendShapeNormals => BlendShapeVertices.Count > 0 && BlendShapeNormals.Count == BlendShapeVertices.Count;

    /// <summary>Current weight (0..1) for each blendshape, driven by expression/viseme/blink drivers.</summary>
    public SyncFieldList<float> BlendShapeWeights { get; private set; } = null!;

    /// <summary>Source blendshape mode: 0 = Normalized, 1 = Relative (Godot Mesh.BlendShapeMode).</summary>
    public readonly Sync<int> BlendShapeMode = new();

    /// <summary>Set true when only weights changed - the hook reapplies weights without a mesh rebuild.</summary>
    public bool BlendWeightsChanged { get; set; }

    public int BlendShapeCount => BlendShapeNames.Count;

    public string BlendShapeName(int index)
        => (index >= 0 && index < BlendShapeNames.Count) ? BlendShapeNames[index] : null!;

    public int GetBlendShapeIndex(string name)
    {
        for (int i = 0; i < BlendShapeNames.Count; i++)
        {
            if (string.Equals(BlendShapeNames[i], name, System.StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    public float GetBlendShapeWeight(int index)
        => (index >= 0 && index < BlendShapeWeights.Count) ? BlendShapeWeights[index] : 0f;

    public void SetBlendShapeWeight(int index, float weight)
    {
        if (index < 0 || index >= BlendShapeWeights.Count)
            return;
        if (BlendShapeWeights[index] == weight)
            return;
        BlendShapeWeights[index] = weight;
        BlendWeightsChanged = true;
        RunApplyChanges();
    }

    public void SetBlendShapeWeight(string name, float weight)
        => SetBlendShapeWeight(GetBlendShapeIndex(name), weight);

    // Local (non-replicated) per-frame override buffer, written by blink/viseme/expression drivers.
    // Every peer runs those drivers from replicated inputs (voice, timers), so broadcasting per-frame
    // weights would just be churn - same reasoning as the IK bone writes.
    private float[]? _runtimeWeights;

    /// <summary>Drive a blendshape weight LOCALLY (no network broadcast). For per-frame animation.</summary>
    public void DriveBlendShapeWeight(int index, float weight)
    {
        if (index < 0 || index >= BlendShapeNames.Count)
            return;
        if (_runtimeWeights == null || _runtimeWeights.Length != BlendShapeNames.Count)
            System.Array.Resize(ref _runtimeWeights, BlendShapeNames.Count);
        if (_runtimeWeights[index] == weight)
            return;
        _runtimeWeights[index] = weight;
        BlendWeightsChanged = true;
        RunApplyChanges();
    }

    public void DriveBlendShapeWeight(string name, float weight)
        => DriveBlendShapeWeight(GetBlendShapeIndex(name), weight);

    /// <summary>Weight the hook should apply: the local driver override if set, else the synced value.</summary>
    public float GetEffectiveBlendShapeWeight(int index)
    {
        if (_runtimeWeights != null && index >= 0 && index < _runtimeWeights.Length)
            return _runtimeWeights[index];
        return (index >= 0 && index < BlendShapeWeights.Count) ? BlendShapeWeights[index] : 0f;
    }

    /// <summary>Populate blendshapes (called at import). names.Length shapes, each with VertexCount verts.</summary>
    public void SetBlendShapes(string[]? names, float3[]? flattenedVertices, int mode)
        => SetBlendShapes(names, flattenedVertices, null, mode);

    /// <summary>
    /// Populate blendshapes with optional NORMAL morph deltas (carried when the source provided them, so normals
    /// morph with the expression instead of staying frozen at the base). flattenedNormals must line up 1:1 with
    /// flattenedVertices or it's ignored. -xlinka
    /// </summary>
    public void SetBlendShapes(string[]? names, float3[]? flattenedVertices, float3[]? flattenedNormals, int mode)
    {
        BlendShapeNames.Clear();
        BlendShapeVertices.Clear();
        BlendShapeNormals.Clear();
        BlendShapeWeights.Clear();

        if (names == null || names.Length == 0 || flattenedVertices == null)
            return;

        BlendShapeMode.Value = mode;
        foreach (var n in names)
        {
            BlendShapeNames.Add(n);
            BlendShapeWeights.Add(0f);
        }
        foreach (var v in flattenedVertices)
            BlendShapeVertices.Add(v);

        if (flattenedNormals != null && flattenedNormals.Length == flattenedVertices.Length)
        {
            foreach (var n in flattenedNormals)
                BlendShapeNormals.Add(n);
        }

        MeshDataChanged = true;
        LumoraLogger.Log($"SkinnedMeshRenderer: Set {names.Length} blendshapes (normals: {(BlendShapeNormals.Count > 0 ? "yes" : "no")})");
    }

    // SETTINGS

    /// <summary>
    /// Shadow casting mode.
    /// </summary>
    public readonly Sync<ShadowCastMode> ShadowCastMode = new();

    /// <summary>
    /// The material to use for rendering.
    /// </summary>
    public AssetRef<MaterialAsset> Material { get; private set; } = null!;

    /// <summary>
    /// Whether to update the mesh when bones move.
    /// </summary>
    public readonly Sync<bool> UpdateWhenOffscreen = new();

    /// <summary>
    /// Quality of skinning (number of bones per vertex).
    /// </summary>
    public readonly Sync<SkinQuality> Quality = new();

    // CHANGE FLAGS

    /// <summary>
    /// Flag indicating mesh data has changed and needs rebuild.
    /// </summary>
    public bool MeshDataChanged { get; set; }

    /// <summary>
    /// Flag indicating skeleton/bones reference has changed.
    /// </summary>
    public bool SkeletonChanged { get; set; }

    /// <summary>
    /// Flag indicating bones have been setup.
    /// </summary>
    public bool BonesReady => Bones.Count > 0 && Bones[0] != null;

    /// <summary>
    /// Flag indicating the hook has successfully bound to skeleton.
    /// Set by the hook when binding is complete.
    /// </summary>
    public bool HookBindingComplete { get; set; }

    public override void OnAwake()
    {
        base.OnAwake();

        // Initialize bone references
        Bones = new SyncRefList<Slot>(this);
        BoneNames = new SyncFieldList<string>(this);
        // Initialize mesh data
        Vertices = new SyncFieldList<float3>(this);
        Normals = new SyncFieldList<float3>(this);
        UVs = new SyncFieldList<float2>(this);
        Indices = new SyncFieldList<int>(this);
        BoneIndices = new SyncFieldList<int4>(this);
        BoneWeights = new SyncFieldList<float4>(this);
        // Blendshapes
        BlendShapeNames = new SyncFieldList<string>(this);
        BlendShapeVertices = new SyncFieldList<float3>(this);
        BlendShapeNormals = new SyncFieldList<float3>(this);
        BlendShapeWeights = new SyncFieldList<float>(this);

        // Initialize settings
        Material = new AssetRef<MaterialAsset>(this);

        // Subscribe to changes
        Skeleton.OnChanged += (field) => { SkeletonChanged = true; RunApplyChanges(); };
        MeshAsset.OnChanged += (field) => { MeshDataChanged = true; RunApplyChanges(); };
        Bones.OnChanged += (list) => { SkeletonChanged = true; RunApplyChanges(); };
        Vertices.OnChanged += (list) => { MeshDataChanged = true; RunApplyChanges(); };
        Normals.OnChanged += (list) => { MeshDataChanged = true; RunApplyChanges(); };
        UVs.OnChanged += (list) => { MeshDataChanged = true; RunApplyChanges(); };
        Indices.OnChanged += (list) => { MeshDataChanged = true; RunApplyChanges(); };
        BoneIndices.OnChanged += (list) => { MeshDataChanged = true; RunApplyChanges(); };
        BoneWeights.OnChanged += (list) => { MeshDataChanged = true; RunApplyChanges(); };

        LumoraLogger.Log($"SkinnedMeshRenderer: Awake on slot '{Slot.SlotName.Value}'");
    }

    public override void OnInit()
    {
        base.OnInit();
        ShadowCastMode.Value    = Components.ShadowCastMode.On;
        UpdateWhenOffscreen.Value = true;
        Quality.Value           = SkinQuality.FourBones;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        // Keep re-driving the hook until it has actually applied the mesh (HookBindingComplete, set by the hook
        // once a real surface is built). Covers BOTH paths: the inline/legacy path waits for bones (BonesReady),
        // and the Phos asset path waits for the MeshProvider's async MeshDataAsset decode AND a ready skeleton.
        // The Phos path populates NO Bones, so the old BonesReady-only gate never fired - the asset would finish
        // decoding a few frames later with nothing to re-drive the build, and the mesh stayed invisible. -xlinka
        if (!HookBindingComplete && (BonesReady || MeshAsset.Target != null))
        {
            RunApplyChanges();
        }

        MeshDataChanged = false;
        SkeletonChanged = false;
    }

    public override void OnDestroy()
    {
        base.OnDestroy();
        LumoraLogger.Log($"SkinnedMeshRenderer: Destroyed on slot '{Slot?.SlotName.Value}'");
    }

    // BONE SETUP

    /// <summary>
    /// Setup bones by finding them in the hierarchy by name.
    /// Call this after mesh data is loaded and bone names are known.
    /// </summary>
    /// <param name="rootSlot">The root slot to search for bones (usually skeleton root).</param>
    public void SetupBones(Slot rootSlot)
    {
        if (rootSlot == null)
        {
            LumoraLogger.Warn("SkinnedMeshRenderer: Cannot setup bones with null root slot");
            return;
        }

        if (BoneNames.Count == 0)
        {
            LumoraLogger.Warn("SkinnedMeshRenderer: No bone names to setup");
            return;
        }

        Bones.Clear();

        for (int i = 0; i < BoneNames.Count; i++)
        {
            string boneName = BoneNames[i];
            Slot boneSlot = FindBoneInHierarchy(rootSlot, boneName);

            if (boneSlot != null)
            {
                Bones.Add(boneSlot);
            }
            else
            {
                // Add null reference to maintain bone index alignment
                Bones.Add(null);
                LumoraLogger.Warn($"SkinnedMeshRenderer: Bone '{boneName}' not found in hierarchy");
            }
        }

        SkeletonChanged = true;
        RunApplyChanges();
        LumoraLogger.Log($"SkinnedMeshRenderer: Setup {Bones.Count} bones from root '{rootSlot.SlotName.Value}'");
    }

    /// <summary>
    /// Setup bones from a SkeletonBuilder.
    /// Maps mesh bone names to skeleton bone slots.
    /// </summary>
    public void SetupBonesFromSkeleton(SkeletonBuilder skeleton)
    {
        if (skeleton == null || !skeleton.IsBuilt.Value)
        {
            LumoraLogger.Warn("SkinnedMeshRenderer: Cannot setup bones - skeleton not ready");
            return;
        }

        Skeleton.Target = skeleton;
        Bones.Clear();

        for (int i = 0; i < BoneNames.Count; i++)
        {
            string boneName = BoneNames[i];
            Slot boneSlot = skeleton.GetBoneSlot(boneName);

            if (boneSlot != null)
            {
                Bones.Add(boneSlot);
            }
            else
            {
                Bones.Add(null);
                LumoraLogger.Warn($"SkinnedMeshRenderer: Bone '{boneName}' not found in skeleton");
            }
        }

        SkeletonChanged = true;
        RunApplyChanges();
        LumoraLogger.Log($"SkinnedMeshRenderer: Setup {Bones.Count} bones from skeleton");
    }

    /// <summary>
    /// Find a bone slot by name in the hierarchy (recursive search).
    /// </summary>
    private static Slot FindBoneInHierarchy(Slot root, string boneName)
    {
        if (root == null || string.IsNullOrEmpty(boneName))
            return null!;

        if (root.SlotName.Value == boneName)
            return root;

        foreach (var child in root.Children)
        {
            var found = FindBoneInHierarchy(child, boneName);
            if (found != null)
                return found;
        }

        return null!;
    }

    /// <summary>
    /// Get a bone slot by index.
    /// </summary>
    public Slot GetBone(int index)
    {
        if (index < 0 || index >= Bones.Count)
            return null!;
        return Bones[index]!;
    }

    /// <summary>
    /// Get a bone slot by name.
    /// </summary>
    public Slot GetBone(string name)
    {
        int index = BoneNames.IndexOf(name);
        if (index < 0)
            return null!;
        return GetBone(index);
    }

    // MESH DATA METHODS

    /// <summary>
    /// Set the mesh data from arrays.
    /// </summary>
    public void SetMeshData(float3[]? vertices, float3[]? normals, float2[]? uvs, int[]? indices,
                            int4[]? boneIndices, float4[]? boneWeights, string[]? boneNames = null)
    {
        if (vertices == null || indices == null)
        {
            LumoraLogger.Warn("SkinnedMeshRenderer: Cannot set mesh data with null vertices or indices");
            return;
        }

        Vertices.Clear();
        Normals.Clear();
        UVs.Clear();
        Indices.Clear();
        BoneIndices.Clear();
        BoneWeights.Clear();
        BoneNames.Clear();

        foreach (var v in vertices)
            Vertices.Add(v);

        if (normals != null)
        {
            foreach (var n in normals)
                Normals.Add(n);
        }
        else
        {
            for (int i = 0; i < vertices.Length; i++)
                Normals.Add(new float3(0, 1, 0));
        }

        if (uvs != null)
        {
            foreach (var uv in uvs)
                UVs.Add(uv);
        }
        else
        {
            for (int i = 0; i < vertices.Length; i++)
                UVs.Add(new float2(0, 0));
        }

        foreach (var idx in indices)
            Indices.Add(idx);

        if (boneIndices != null)
        {
            foreach (var bi in boneIndices)
                BoneIndices.Add(bi);
        }
        else
        {
            for (int i = 0; i < vertices.Length; i++)
                BoneIndices.Add(new int4(0, 0, 0, 0));
        }

        if (boneWeights != null)
        {
            foreach (var bw in boneWeights)
                BoneWeights.Add(bw);
        }
        else
        {
            for (int i = 0; i < vertices.Length; i++)
                BoneWeights.Add(new float4(1, 0, 0, 0));
        }

        if (boneNames != null)
        {
            foreach (var name in boneNames)
                BoneNames.Add(name);
        }

        MeshDataChanged = true;
        LumoraLogger.Log($"SkinnedMeshRenderer: Set mesh data with {vertices.Length} vertices, {indices.Length / 3} triangles, {boneNames?.Length ?? 0} bones");
    }

    /// <summary>
    /// Clear all mesh data.
    /// </summary>
    public void ClearMesh()
    {
        Vertices.Clear();
        Normals.Clear();
        UVs.Clear();
        Indices.Clear();
        BoneIndices.Clear();
        BoneWeights.Clear();
        Bones.Clear();
        BoneNames.Clear();
        MeshDataChanged = true;
        LumoraLogger.Log("SkinnedMeshRenderer: Cleared mesh data");
    }
}

/// <summary>
/// Skinning quality settings.
/// </summary>
public enum SkinQuality
{
    Auto = 0,
    OneBone = 1,
    TwoBones = 2,
    FourBones = 4
}

