// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

﻿using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

/// <summary>
/// Hook for ModelData component → runtime model scene instantiation.
/// Loads GLB/GLTF/VRM into the slot's Godot node, then builds a Lumora-style
/// component hierarchy in Lumora slots:
///   - ObjectRoot + Grabbable on root
///   - SkeletonBuilder + BipedRig for armatures
///   - SkinnedMeshRenderer with real mesh data (vertices/normals/UVs/indices/bones)
///   - MeshRenderer for static meshes
///   - PBS_Metallic material components populated from Godot StandardMaterial3D
/// </summary>
public sealed class ModelDataHook : ComponentHook<ModelData>
{
    private Node3D? _modelRoot;
    private readonly List<Slot> _createdSlots = new();
    private SkeletonBuilder? _builtSkeleton;
    private string _loadedSourceKey = string.Empty;
    private const int MaxSlotNodes = 4096;

    private struct PendingSkinnedMesh
    {
        public MeshInstance3D GodotMesh;
        public Slot MeshSlot;
        public Skeleton3D? GltfSkeleton;
    }

    public static IHook<ModelData> Constructor() => new ModelDataHook();

    public override void Initialize()
    {
        base.Initialize();
        TryLoadModel(force: true);
    }

    public override void ApplyChanges()
    {
        var sourceChanged = Owner.SourcePath.GetWasChangedAndClear();
        var uriChanged = Owner.LocalUri.GetWasChangedAndClear();

        if (sourceChanged || uriChanged || _modelRoot == null)
            TryLoadModel(force: sourceChanged || uriChanged);
    }

    // ------------------------------------------------------------------
    // Model loading
    // ------------------------------------------------------------------

    private void TryLoadModel(bool force)
    {
        var sourcePath = ResolveSourcePath();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            Owner.IsLoaded.Value = false;
            return;
        }

        if (!force && string.Equals(_loadedSourceKey, sourcePath, StringComparison.Ordinal) && _modelRoot != null)
            return;

        ClearLoadedModel();

        var loadedNode = LoadSceneNode(sourcePath);
        if (loadedNode == null)
        {
            LumoraLogger.Warn($"ModelDataHook: Failed to load model '{sourcePath}'");
            Owner.IsLoaded.Value = false;
            return;
        }

        if (loadedNode is Node3D node3D)
            _modelRoot = node3D;
        else
        {
            _modelRoot = new Node3D { Name = "ImportedModelRoot" };
            _modelRoot.AddChild(loadedNode);
        }

        _modelRoot.Name = "ImportedModelRoot";
        attachedNode.AddChild(_modelRoot);
        ApplyImportSettings(_modelRoot);

        BuildSlotHierarchy(_modelRoot);

        _loadedSourceKey = sourcePath;
        Owner.IsLoaded.Value = true;

        LumoraLogger.Log($"ModelDataHook: Loaded model '{Path.GetFileName(sourcePath)}' on slot '{Owner.Slot.SlotName.Value}'");
    }

    private string ResolveSourcePath()
    {
        var sourcePath = (Owner.SourcePath.Value ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(sourcePath))
            return string.Empty;

        if (sourcePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return sourcePath;

        if (File.Exists(sourcePath))
            return sourcePath;

        var localized = ProjectSettings.LocalizePath(sourcePath);
        if (!string.IsNullOrWhiteSpace(localized) &&
            localized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return localized;

        return sourcePath;
    }

    private static Node? LoadSceneNode(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();

        var packedScene = ResourceLoader.Load<PackedScene>(sourcePath);
        if (packedScene != null)
            return packedScene.Instantiate();

        if (extension is ".glb" or ".gltf" or ".vrm")
        {
            try
            {
                var doc = new GltfDocument();
                var state = new GltfState();
                var appendErr = doc.AppendFromFile(sourcePath, state);
                if (appendErr != Error.Ok)
                {
                    LumoraLogger.Warn($"ModelDataHook: GLTF append failed for '{sourcePath}' ({appendErr})");
                    return null;
                }

                var scene = doc.GenerateScene(state);
                if (scene == null)
                    LumoraLogger.Warn($"ModelDataHook: GLTF generate scene failed for '{sourcePath}'");
                return scene;
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"ModelDataHook: Exception loading '{sourcePath}': {ex.Message}");
                return null;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Scale / bounds
    // ------------------------------------------------------------------

    private void ApplyImportSettings(Node3D modelRoot)
    {
        var settings = Owner.ImportSettings ?? new ModelImportSettings();

        if (!TryCalculateBounds(modelRoot, out var bounds))
        {
            modelRoot.Position = Vector3.Zero;
            modelRoot.Scale = Vector3.One * Mathf.Max(0.0001f, settings.Scale);
            return;
        }

        var uniformScale = Mathf.Max(0.0001f, settings.Scale);
        bool shouldRescale = settings.Rescale || settings.IsAvatarImport || Owner.IsAvatar.Value;
        if (shouldRescale)
        {
            var sourceHeight = Mathf.Max(bounds.Size.Y, 0.0001f);
            var targetHeight = Mathf.Max(0.01f, settings.TargetHeight);
            uniformScale *= targetHeight / sourceHeight;
        }

        var center = bounds.Position + (bounds.Size * 0.5f);
        Vector3 offset = Vector3.Zero;
        if (settings.Center)
        {
            bool avatarImport = settings.IsAvatarImport || Owner.IsAvatar.Value;
            offset = avatarImport
                ? new Vector3(-center.X, -bounds.Position.Y, -center.Z) * uniformScale
                : -center * uniformScale;
        }

        modelRoot.Scale = Vector3.One * uniformScale;
        modelRoot.Position = offset;
        LumoraLogger.Log($"ModelDataHook: Applied import scale={uniformScale:0.###} size={bounds.Size}");
    }

    private static bool TryCalculateBounds(Node3D root, out Aabb bounds)
    {
        bounds = default;
        if (root == null || !GodotObject.IsInstanceValid(root)) return false;

        var rootInverse = root.GlobalTransform.AffineInverse();
        bool hasAny = false;
        Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);

        foreach (var meshInstance in EnumerateMeshInstances(root))
        {
            var mesh = meshInstance.Mesh;
            if (mesh == null) continue;
            var localAabb = mesh.GetAabb();
            if (localAabb.Size == Vector3.Zero) continue;

            var meshGlobal = meshInstance.GlobalTransform;
            foreach (var corner in GetAabbCorners(localAabb))
            {
                var worldPoint = meshGlobal * corner;
                var rootPoint = rootInverse * worldPoint;
                min.X = Mathf.Min(min.X, rootPoint.X);
                min.Y = Mathf.Min(min.Y, rootPoint.Y);
                min.Z = Mathf.Min(min.Z, rootPoint.Z);
                max.X = Mathf.Max(max.X, rootPoint.X);
                max.Y = Mathf.Max(max.Y, rootPoint.Y);
                max.Z = Mathf.Max(max.Z, rootPoint.Z);
                hasAny = true;
            }
        }

        if (!hasAny) return false;
        bounds = new Aabb(min, max - min);
        return true;
    }

    private static IEnumerable<MeshInstance3D> EnumerateMeshInstances(Node root)
    {
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is MeshInstance3D mi) yield return mi;
            foreach (var child in node.GetChildren())
                if (child is Node cn) stack.Push(cn);
        }
    }

    private static IEnumerable<Vector3> GetAabbCorners(Aabb aabb)
    {
        var p = aabb.Position;
        var s = aabb.Size;
        yield return p;
        yield return p + new Vector3(s.X, 0f, 0f);
        yield return p + new Vector3(0f, s.Y, 0f);
        yield return p + new Vector3(0f, 0f, s.Z);
        yield return p + new Vector3(s.X, s.Y, 0f);
        yield return p + new Vector3(s.X, 0f, s.Z);
        yield return p + new Vector3(0f, s.Y, s.Z);
        yield return p + s;
    }

    // ------------------------------------------------------------------
    // Slot hierarchy with real Lumora components
    // ------------------------------------------------------------------

    private void BuildSlotHierarchy(Node3D modelRoot)
    {
        ClearCreatedSlots();

        var modelSlot = Owner.Slot;

        // ObjectRoot on the model root (matching Lumora runtime expectations)
        if (modelSlot.GetComponent<ObjectRoot>() == null)
        {
            var objRoot = modelSlot.AttachComponent<ObjectRoot>();
            objRoot.ObjectName.Value = modelSlot.SlotName.Value;
        }

        // Grabbable so players can pick it up
        if (modelSlot.GetComponent<Grabbable>() == null)
            modelSlot.AttachComponent<Grabbable>();

        int created = 0;
        Skeleton3D? foundSkeleton = null;
        Slot? skeletonParentSlot = null;
        var pendingSkinnedMeshes = new List<PendingSkinnedMesh>();

        WalkNodeTree(modelRoot, modelSlot, ref created,
            ref foundSkeleton, ref skeletonParentSlot, pendingSkinnedMeshes);

        // Build SkeletonBuilder + BipedRig now that all bone slots exist
        SkeletonBuilder? skelBuilder = null;
        if (foundSkeleton != null && skeletonParentSlot != null)
            skelBuilder = BuildSkeletonComponents(foundSkeleton, skeletonParentSlot);

        // Now attach real SkinnedMeshRenderer components (needs skeleton ready)
        foreach (var pending in pendingSkinnedMeshes)
        {
            AttachSkinnedMeshComponents(
                pending.MeshSlot, pending.GodotMesh,
                pending.GltfSkeleton ?? foundSkeleton, skelBuilder);
        }

        LumoraLogger.Log($"ModelDataHook: Built {created} hierarchy slots");
    }

    /// <summary>
    /// Walk the Godot node tree and mirror it into Lumora slots with real components.
    /// </summary>
    private void WalkNodeTree(Node node, Slot parentSlot, ref int created,
        ref Skeleton3D? foundSkeleton, ref Slot? skeletonParentSlot,
        List<PendingSkinnedMesh> pendingSkinnedMeshes)
    {
        if (created >= MaxSlotNodes) return;

        string rawName = node.Name.ToString();
        if (string.IsNullOrWhiteSpace(rawName))
            rawName = node.GetType().Name;

        var nodeSlot = parentSlot.AddSlot(rawName);
        _createdSlots.Add(nodeSlot);
        created++;

        // Mirror local transform from Godot node to Lumora slot
        if (node is Node3D n3d)
        {
            var t = n3d.Transform;
            nodeSlot.LocalPosition.Value = new float3(t.Origin.X, t.Origin.Y, t.Origin.Z);
            var q = t.Basis.GetRotationQuaternion();
            nodeSlot.LocalRotation.Value = new floatQ(q.X, q.Y, q.Z, q.W);
            var sc = t.Basis.Scale;
            nodeSlot.LocalScale.Value = new float3(sc.X, sc.Y, sc.Z);
        }

        if (node is Skeleton3D skeleton)
        {
            foundSkeleton = skeleton;
            skeletonParentSlot = nodeSlot;
            AddBonesInline(skeleton, nodeSlot, ref created);
        }
        else if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
        {
            bool skinned = IsMeshSkinned(meshInstance);
            if (skinned)
            {
                // Queue for processing after skeleton is built
                Skeleton3D? gltfSkel = ResolveMeshSkeleton(meshInstance);
                pendingSkinnedMeshes.Add(new PendingSkinnedMesh
                {
                    GodotMesh = meshInstance,
                    MeshSlot = nodeSlot,
                    GltfSkeleton = gltfSkel
                });
                // Hide original - SkinnedMeshHook will create its own MeshInstance3D
                meshInstance.Visible = false;
            }
            else
            {
                AttachStaticMeshComponents(nodeSlot, meshInstance);
            }
        }

        // Recurse children
        foreach (var child in node.GetChildren())
        {
            if (child is Node childNode)
                WalkNodeTree(childNode, nodeSlot, ref created,
                    ref foundSkeleton, ref skeletonParentSlot, pendingSkinnedMeshes);
        }
    }

    /// <summary>
    /// Detect if a MeshInstance3D uses skeletal animation.
    /// </summary>
    private static bool IsMeshSkinned(MeshInstance3D mi)
    {
        if (!mi.Skeleton.IsEmpty) return true;
        if (mi.GetParent() is Skeleton3D) return true;

        if (mi.Mesh != null && mi.Mesh.GetSurfaceCount() > 0)
        {
            var arrays = mi.Mesh.SurfaceGetArrays(0);
            if (arrays != null && arrays.Count > (int)Mesh.ArrayType.Bones)
            {
                var bonesVariant = arrays[(int)Mesh.ArrayType.Bones];
                if (bonesVariant.VariantType != Variant.Type.Nil)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Find the Godot Skeleton3D that a MeshInstance3D is bound to.
    /// </summary>
    private static Skeleton3D? ResolveMeshSkeleton(MeshInstance3D mi)
    {
        if (!mi.Skeleton.IsEmpty)
        {
            var node = mi.GetNodeOrNull(mi.Skeleton);
            if (node is Skeleton3D skel) return skel;
        }
        var parent = mi.GetParent();
        while (parent != null)
        {
            if (parent is Skeleton3D skel) return skel;
            parent = parent.GetParent();
        }
        return null;
    }

    /// <summary>
    /// Add bone slots inline in the hierarchy (Lumora style, no wrapper).
    /// </summary>
    private void AddBonesInline(Skeleton3D skeleton, Slot skeletonSlot, ref int created)
    {
        int boneCount = skeleton.GetBoneCount();
        var indexToSlot = new Dictionary<int, Slot>();

        for (int i = 0; i < boneCount && created < MaxSlotNodes; i++)
        {
            string boneName = skeleton.GetBoneName(i).ToString();
            if (string.IsNullOrWhiteSpace(boneName))
                boneName = $"Bone_{i}";

            int parentIndex = skeleton.GetBoneParent(i);
            var parent = (parentIndex >= 0 && indexToSlot.TryGetValue(parentIndex, out var parentBoneSlot))
                ? parentBoneSlot
                : skeletonSlot;

            var boneSlot = parent.AddSlot(boneName);
            _createdSlots.Add(boneSlot);
            indexToSlot[i] = boneSlot;
            created++;

            // Set rest pose transforms so SkeletonHook can read them
            var rest = skeleton.GetBoneRest(i);
            boneSlot.LocalPosition.Value = new float3(rest.Origin.X, rest.Origin.Y, rest.Origin.Z);
            var q = rest.Basis.GetRotationQuaternion();
            boneSlot.LocalRotation.Value = new floatQ(q.X, q.Y, q.Z, q.W);
            var s = rest.Basis.Scale;
            boneSlot.LocalScale.Value = new float3(s.X, s.Y, s.Z);
        }
    }

    // ------------------------------------------------------------------
    // Skeleton + BipedRig + IKSolver
    // ------------------------------------------------------------------

    private SkeletonBuilder? BuildSkeletonComponents(Skeleton3D skeleton, Slot skeletonSlot)
    {
        int boneCount = skeleton.GetBoneCount();
        if (boneCount == 0) return null;

        // Collect bone slots by name
        var boneNameToSlot = new Dictionary<string, Slot>();
        CollectBoneSlots(skeletonSlot, boneNameToSlot);

        // Create SkeletonBuilder on the skeleton slot
        var skelBuilder = skeletonSlot.AttachComponent<SkeletonBuilder>();

        // Find and set root bone
        for (int i = 0; i < boneCount; i++)
        {
            if (skeleton.GetBoneParent(i) < 0)
            {
                string rootBoneName = skeleton.GetBoneName(i).ToString();
                if (boneNameToSlot.TryGetValue(rootBoneName, out var rbs))
                {
                    skelBuilder.RootBone.Target = rbs;
                    break;
                }
            }
        }

        // Add bones in skeleton order (matches GLTF bone index order)
        for (int i = 0; i < boneCount; i++)
        {
            string boneName = skeleton.GetBoneName(i).ToString();
            if (string.IsNullOrWhiteSpace(boneName)) boneName = $"Bone_{i}";

            if (boneNameToSlot.TryGetValue(boneName, out var boneSlot))
            {
                var rest = skeleton.GetBoneRest(i);
                var restMatrix = GodotTransformToFloat4x4(rest);
                skelBuilder.AddBone(boneName, boneSlot, restMatrix);
            }
        }

        skelBuilder.IsBuilt.Value = true;
        _builtSkeleton = skelBuilder;

        LumoraLogger.Log($"ModelDataHook: Built SkeletonBuilder with {skelBuilder.BoneCount} bones");

        // BipedRig for avatar imports
        bool isAvatar = Owner.IsAvatar.Value || (Owner.ImportSettings?.IsAvatarImport ?? false);
        if (isAvatar)
        {
            var rig = skeletonSlot.AttachComponent<BipedRig>();
            rig.PopulateFromSkeleton(skelBuilder);
            LumoraLogger.Log($"ModelDataHook: Created BipedRig, IsBiped={rig.IsBiped}");
        }

        return skelBuilder;
    }

    private static void CollectBoneSlots(Slot parent, Dictionary<string, Slot> map)
    {
        foreach (var child in parent.Children)
        {
            string name = child.SlotName.Value;
            if (!string.IsNullOrWhiteSpace(name) && !map.ContainsKey(name))
                map[name] = child;
            CollectBoneSlots(child, map);
        }
    }

    private static float4x4 GodotTransformToFloat4x4(Transform3D t)
    {
        var b = t.Basis;
        return new float4x4(
            new float4(b.Column0.X, b.Column0.Y, b.Column0.Z, 0f),
            new float4(b.Column1.X, b.Column1.Y, b.Column1.Z, 0f),
            new float4(b.Column2.X, b.Column2.Y, b.Column2.Z, 0f),
            new float4(t.Origin.X, t.Origin.Y, t.Origin.Z, 1f));
    }

    // ------------------------------------------------------------------
    // Mesh component attachment
    // ------------------------------------------------------------------

    /// <summary>
    /// Attach MeshRenderer + PBS_Metallic to a static (non-skinned) mesh node.
    /// </summary>
    private void AttachStaticMeshComponents(Slot meshSlot, MeshInstance3D godotMesh)
    {
        meshSlot.AttachComponent<MeshRenderer>();

        if (godotMesh.Mesh != null && godotMesh.Mesh.GetSurfaceCount() > 0)
        {
            var matSlot = meshSlot.AddSlot("Material");
            _createdSlots.Add(matSlot);
            var pbs = matSlot.AttachComponent<PBS_Metallic>();
            PopulatePBSFromGodotMaterial(pbs, godotMesh.GetActiveMaterial(0));
        }
    }

    /// <summary>
    /// Extract mesh data from GLTF MeshInstance3D and attach SkinnedMeshRenderer + PBS_Metallic.
    /// One SkinnedMeshRenderer is created per mesh surface.
    /// </summary>
    private void AttachSkinnedMeshComponents(Slot meshSlot, MeshInstance3D godotMesh,
        Skeleton3D? gltfSkeleton, SkeletonBuilder? skelBuilder)
    {
        var mesh = godotMesh.Mesh;
        if (mesh == null) return;

        int surfaceCount = mesh.GetSurfaceCount();
        if (surfaceCount == 0) return;

        // Build bone name array from GLTF skeleton (matches bone index order in mesh data)
        string[]? boneNamesArray = null;
        if (gltfSkeleton != null)
        {
            int bc = gltfSkeleton.GetBoneCount();
            boneNamesArray = new string[bc];
            for (int b = 0; b < bc; b++)
                boneNamesArray[b] = gltfSkeleton.GetBoneName(b);
        }

        for (int surfIdx = 0; surfIdx < surfaceCount; surfIdx++)
        {
            // For multi-surface meshes, create a child slot per surface
            Slot surfSlot;
            if (surfaceCount == 1)
            {
                surfSlot = meshSlot;
            }
            else
            {
                surfSlot = meshSlot.AddSlot($"Surface_{surfIdx}");
                _createdSlots.Add(surfSlot);
            }

            var smr = surfSlot.AttachComponent<SkinnedMeshRenderer>();

            // Extract and set mesh data
            if (ExtractSurfaceMeshData(mesh, surfIdx,
                out var verts, out var normals, out var uvs, out var indices,
                out var boneIdx, out var boneWgt))
            {
                smr.SetMeshData(verts, normals, uvs, indices, boneIdx, boneWgt, boneNamesArray);
            }

            // Link to SkeletonBuilder so SkinnedMeshHook can find the Godot Skeleton3D
            if (skelBuilder != null)
            {
                smr.Skeleton.Target = skelBuilder;
                smr.SetupBonesFromSkeleton(skelBuilder);
            }

            // Attach PBS_Metallic material component
            var matSlot = surfSlot.AddSlot("Material");
            _createdSlots.Add(matSlot);
            var pbs = matSlot.AttachComponent<PBS_Metallic>();
            PopulatePBSFromGodotMaterial(pbs, godotMesh.GetActiveMaterial(surfIdx));
            smr.Material.Target = pbs;
        }

        LumoraLogger.Log($"ModelDataHook: Attached SkinnedMeshRenderer(s) for '{godotMesh.Name}' ({surfaceCount} surfaces)");
    }

    // ------------------------------------------------------------------
    // Mesh data extraction from Godot surfaces
    // ------------------------------------------------------------------

    private static bool ExtractSurfaceMeshData(Mesh mesh, int surfIdx,
        out float3[] vertices, out float3[] normals, out float2[] uvs, out int[] indices,
        out int4[] boneIndices, out float4[] boneWeights)
    {
        vertices = Array.Empty<float3>();
        normals = Array.Empty<float3>();
        uvs = Array.Empty<float2>();
        indices = Array.Empty<int>();
        boneIndices = Array.Empty<int4>();
        boneWeights = Array.Empty<float4>();

        var arrays = mesh.SurfaceGetArrays(surfIdx);
        if (arrays == null || arrays.Count == 0) return false;

        // Vertices (required)
        var vertVariant = arrays[(int)Mesh.ArrayType.Vertex];
        if (vertVariant.VariantType == Variant.Type.Nil) return false;
        var gVerts = vertVariant.AsVector3Array();
        if (gVerts == null || gVerts.Length == 0) return false;

        vertices = new float3[gVerts.Length];
        for (int i = 0; i < gVerts.Length; i++)
            vertices[i] = new float3(gVerts[i].X, gVerts[i].Y, gVerts[i].Z);

        // Normals (optional)
        var normVariant = arrays[(int)Mesh.ArrayType.Normal];
        if (normVariant.VariantType != Variant.Type.Nil)
        {
            var gNorms = normVariant.AsVector3Array();
            if (gNorms != null && gNorms.Length == gVerts.Length)
            {
                normals = new float3[gNorms.Length];
                for (int i = 0; i < gNorms.Length; i++)
                    normals[i] = new float3(gNorms[i].X, gNorms[i].Y, gNorms[i].Z);
            }
        }

        // UVs (optional)
        var uvVariant = arrays[(int)Mesh.ArrayType.TexUV];
        if (uvVariant.VariantType != Variant.Type.Nil)
        {
            var gUVs = uvVariant.AsVector2Array();
            if (gUVs != null && gUVs.Length == gVerts.Length)
            {
                uvs = new float2[gUVs.Length];
                for (int i = 0; i < gUVs.Length; i++)
                    uvs[i] = new float2(gUVs[i].X, gUVs[i].Y);
            }
        }

        // Indices (optional - generate sequential if absent)
        var idxVariant = arrays[(int)Mesh.ArrayType.Index];
        if (idxVariant.VariantType != Variant.Type.Nil)
        {
            var gIdx = idxVariant.AsInt32Array();
            if (gIdx != null) indices = gIdx;
        }
        else
        {
            indices = new int[gVerts.Length];
            for (int i = 0; i < gVerts.Length; i++) indices[i] = i;
        }

        // Bone indices and weights (optional, 4 per vertex packed)
        var boneIdxVariant = arrays[(int)Mesh.ArrayType.Bones];
        var boneWgtVariant = arrays[(int)Mesh.ArrayType.Weights];

        if (boneIdxVariant.VariantType != Variant.Type.Nil &&
            boneWgtVariant.VariantType != Variant.Type.Nil)
        {
            var gBoneIdx = boneIdxVariant.AsInt32Array();
            var gBoneWgt = boneWgtVariant.AsFloat32Array();

            if (gBoneIdx != null && gBoneWgt != null &&
                gBoneIdx.Length == gVerts.Length * 4 &&
                gBoneWgt.Length == gVerts.Length * 4)
            {
                boneIndices = new int4[gVerts.Length];
                boneWeights = new float4[gVerts.Length];
                for (int i = 0; i < gVerts.Length; i++)
                {
                    boneIndices[i] = new int4(
                        gBoneIdx[i * 4 + 0], gBoneIdx[i * 4 + 1],
                        gBoneIdx[i * 4 + 2], gBoneIdx[i * 4 + 3]);
                    boneWeights[i] = new float4(
                        gBoneWgt[i * 4 + 0], gBoneWgt[i * 4 + 1],
                        gBoneWgt[i * 4 + 2], gBoneWgt[i * 4 + 3]);
                }
            }
        }

        return true;
    }

    // ------------------------------------------------------------------
    // Material population from Godot StandardMaterial3D
    // ------------------------------------------------------------------

    private static void PopulatePBSFromGodotMaterial(PBS_Metallic pbs, Material? godotMat)
    {
        if (godotMat is not StandardMaterial3D std) return;

        // Albedo color
        var c = std.AlbedoColor;
        pbs.AlbedoColor.Value = new colorHDR(c.R, c.G, c.B, c.A);

        // Metallic / smoothness
        pbs.Metallic.Value = std.Metallic;
        pbs.Smoothness.Value = 1.0f - std.Roughness;

        // Normal scale
        pbs.NormalScale.Value = std.NormalScale;

        // Emission
        if (std.EmissionEnabled)
        {
            var e = std.Emission;
            float mult = std.EmissionEnergyMultiplier;
            pbs.EmissiveColor.Value = new colorHDR(e.R * mult, e.G * mult, e.B * mult, 1f);
        }

        // Blend mode
        pbs.BlendMode.Value = std.Transparency switch
        {
            BaseMaterial3D.TransparencyEnum.AlphaScissor => BlendMode.Cutout,
            BaseMaterial3D.TransparencyEnum.Disabled     => BlendMode.Opaque,
            _                                            => BlendMode.Transparent,
        };

        // Alpha cutoff
        pbs.AlphaCutoff.Value = std.AlphaScissorThreshold;

        // Face culling
        pbs.Culling.Value = std.CullMode == BaseMaterial3D.CullModeEnum.Disabled
            ? Culling.None
            : Culling.Back;
    }

    // ------------------------------------------------------------------
    // Cleanup
    // ------------------------------------------------------------------

    private void ClearLoadedModel()
    {
        if (_modelRoot != null && GodotObject.IsInstanceValid(_modelRoot))
            _modelRoot.QueueFree();

        ClearCreatedSlots();
        _builtSkeleton = null;
        _modelRoot = null;
        _loadedSourceKey = string.Empty;
    }

    private void ClearCreatedSlots()
    {
        foreach (var slot in _createdSlots)
        {
            if (slot != null && !slot.IsDestroyed)
                slot.Destroy();
        }
        _createdSlots.Clear();
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (!destroyingWorld)
            ClearLoadedModel();

        base.Destroy(destroyingWorld);
    }
}
