// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Math;
using AssimpContext = Assimp.AssimpContext;
using AssimpMaterial = Assimp.Material;
using AssimpMesh = Assimp.Mesh;
using AssimpNode = Assimp.Node;
using AssimpPostProcessSteps = Assimp.PostProcessSteps;
using AssimpScene = Assimp.Scene;
using NumericsMatrix4x4 = System.Numerics.Matrix4x4;
using NumericsVector3 = System.Numerics.Vector3;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

/// <summary>
/// Hook for ModelData component to runtime model scene instantiation.
/// Loads GLB/GLTF/VRM through Godot's scene loader and Assimp-backed formats
/// through a direct runtime import path, then mirrors the model into Lumora slots.
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

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        bool loaded = IsGodotNativeModelExtension(extension)
            ? TryLoadGodotNativeModel(sourcePath)
            : TryLoadAssimpModel(sourcePath);

        if (!loaded)
        {
            LumoraLogger.Warn($"ModelDataHook: Failed to load model '{sourcePath}'");
            Owner.IsLoaded.Value = false;
            return;
        }

        _loadedSourceKey = sourcePath;
        Owner.IsLoaded.Value = true;
        LumoraLogger.Log($"ModelDataHook: Loaded model '{Path.GetFileName(sourcePath)}' on slot '{Owner.Slot.SlotName.Value}'");
    }

    private bool TryLoadGodotNativeModel(string sourcePath)
    {
        var loadedNode = LoadSceneNode(sourcePath);
        if (loadedNode == null)
            return false;

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
        return true;
    }

    private bool TryLoadAssimpModel(string sourcePath)
    {
        var resolvedPath = ResolveAssimpSourcePath(sourcePath);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            LumoraLogger.Warn($"ModelDataHook: Assimp source path '{resolvedPath}' does not exist");
            return false;
        }

        try
        {
            using var context = new AssimpContext();
            var scene = context.ImportFile(resolvedPath, GetAssimpScenePostProcessSteps());
            if (scene?.RootNode == null)
            {
                LumoraLogger.Warn($"ModelDataHook: Assimp returned no scene for '{resolvedPath}'");
                return false;
            }

            _modelRoot = new Node3D { Name = "ImportedModelRoot" };
            attachedNode.AddChild(_modelRoot);

            var importRootSlot = BuildAssimpSlotHierarchy(scene);
            if (importRootSlot != null)
            {
                if (TryCalculateAssimpBounds(scene, out var bounds))
                    ApplyImportSettings(importRootSlot, bounds);
                else
                    ApplyImportSettings(importRootSlot, null);
            }

            return true;
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"ModelDataHook: Exception loading '{resolvedPath}' through Assimp: {ex.Message}");
            return false;
        }
    }

    private static bool IsGodotNativeModelExtension(string extension)
    {
        return extension is ".glb" or ".gltf" or ".vrm";
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

    private static string ResolveAssimpSourcePath(string sourcePath)
    {
        var trimmed = (sourcePath ?? string.Empty).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
            return string.Empty;

        if (trimmed.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            return ProjectSettings.GlobalizePath(trimmed);

        if (File.Exists(trimmed))
            return Path.GetFullPath(trimmed);

        var localized = ProjectSettings.LocalizePath(trimmed);
        if (!string.IsNullOrWhiteSpace(localized) &&
            localized.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSettings.GlobalizePath(localized);
        }

        return trimmed;
    }

    private static global::Godot.Node? LoadSceneNode(string sourcePath)
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

    private static AssimpPostProcessSteps GetAssimpScenePostProcessSteps()
    {
        return AssimpPostProcessSteps.Triangulate
             | AssimpPostProcessSteps.JoinIdenticalVertices
             | AssimpPostProcessSteps.GenerateSmoothNormals
             | AssimpPostProcessSteps.SortByPrimitiveType
             | AssimpPostProcessSteps.ImproveCacheLocality
             | AssimpPostProcessSteps.LimitBoneWeights
             | AssimpPostProcessSteps.FindInvalidData
             | AssimpPostProcessSteps.ValidateDataStructure
             | AssimpPostProcessSteps.FlipUVs;
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

    private void ApplyImportSettings(Slot importRootSlot, Aabb? bounds)
    {
        var settings = Owner.ImportSettings ?? new ModelImportSettings();
        var uniformScale = Mathf.Max(0.0001f, settings.Scale);

        if (bounds.HasValue)
        {
            bool shouldRescale = settings.Rescale || settings.IsAvatarImport || Owner.IsAvatar.Value;
            if (shouldRescale)
            {
                var sourceHeight = Mathf.Max(bounds.Value.Size.Y, 0.0001f);
                var targetHeight = Mathf.Max(0.01f, settings.TargetHeight);
                uniformScale *= targetHeight / sourceHeight;
            }

            var center = bounds.Value.Position + (bounds.Value.Size * 0.5f);
            Vector3 offset = Vector3.Zero;
            if (settings.Center)
            {
                bool avatarImport = settings.IsAvatarImport || Owner.IsAvatar.Value;
                offset = avatarImport
                    ? new Vector3(-center.X, -bounds.Value.Position.Y, -center.Z) * uniformScale
                    : -center * uniformScale;
            }

            importRootSlot.LocalPosition.Value = new float3(offset.X, offset.Y, offset.Z);
            importRootSlot.LocalScale.Value = new float3(uniformScale, uniformScale, uniformScale);
            LumoraLogger.Log($"ModelDataHook: Applied import scale={uniformScale:0.###} size={bounds.Value.Size}");
            return;
        }

        importRootSlot.LocalPosition.Value = float3.Zero;
        importRootSlot.LocalScale.Value = new float3(uniformScale, uniformScale, uniformScale);
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

    private static bool TryCalculateAssimpBounds(AssimpScene scene, out Aabb bounds)
    {
        bounds = default;
        if (scene?.RootNode == null || !scene.HasMeshes)
            return false;

        bool hasAny = false;
        Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);

        AccumulateAssimpBounds(scene, scene.RootNode, NumericsMatrix4x4.Identity, ref hasAny, ref min, ref max);
        if (!hasAny)
            return false;

        bounds = new Aabb(min, max - min);
        return true;
    }

    private static void AccumulateAssimpBounds(
        AssimpScene scene,
        AssimpNode node,
        NumericsMatrix4x4 parentTransform,
        ref bool hasAny,
        ref Vector3 min,
        ref Vector3 max)
    {
        var worldTransform = node.Transform * parentTransform;

        if (node.HasMeshes)
        {
            foreach (var meshIndex in node.MeshIndices)
            {
                if (meshIndex < 0 || meshIndex >= scene.MeshCount)
                    continue;

                var mesh = scene.Meshes[meshIndex];
                if (mesh == null || !mesh.HasVertices)
                    continue;

                foreach (var vertex in mesh.Vertices)
                {
                    var transformed = NumericsVector3.Transform(vertex, worldTransform);
                    var point = new Vector3(transformed.X, transformed.Y, transformed.Z);
                    min.X = Mathf.Min(min.X, point.X);
                    min.Y = Mathf.Min(min.Y, point.Y);
                    min.Z = Mathf.Min(min.Z, point.Z);
                    max.X = Mathf.Max(max.X, point.X);
                    max.Y = Mathf.Max(max.Y, point.Y);
                    max.Z = Mathf.Max(max.Z, point.Z);
                    hasAny = true;
                }
            }
        }

        if (!node.HasChildren)
            return;

        foreach (var child in node.Children)
            AccumulateAssimpBounds(scene, child, worldTransform, ref hasAny, ref min, ref max);
    }

    private static IEnumerable<MeshInstance3D> EnumerateMeshInstances(global::Godot.Node root)
    {
        var stack = new Stack<global::Godot.Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is MeshInstance3D mi) yield return mi;
            foreach (var child in node.GetChildren())
                if (child is global::Godot.Node cn) stack.Push(cn);
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

        if (modelSlot.GetComponent<ObjectRoot>() == null)
        {
            var objRoot = modelSlot.AttachComponent<ObjectRoot>();
            objRoot.ObjectName.Value = modelSlot.SlotName.Value;
        }

        if (modelSlot.GetComponent<Grabbable>() == null)
            modelSlot.AttachComponent<Grabbable>();

        int created = 0;
        Skeleton3D? foundSkeleton = null;
        Slot? skeletonParentSlot = null;
        var pendingSkinnedMeshes = new List<PendingSkinnedMesh>();

        WalkNodeTree(modelRoot, modelSlot, ref created,
            ref foundSkeleton, ref skeletonParentSlot, pendingSkinnedMeshes);

        SkeletonBuilder? skelBuilder = null;
        if (foundSkeleton != null && skeletonParentSlot != null)
            skelBuilder = BuildSkeletonComponents(foundSkeleton, skeletonParentSlot);

        foreach (var pending in pendingSkinnedMeshes)
        {
            AttachSkinnedMeshComponents(
                pending.MeshSlot, pending.GodotMesh,
                pending.GltfSkeleton ?? foundSkeleton, skelBuilder);
        }

        LumoraLogger.Log($"ModelDataHook: Built {created} hierarchy slots");
    }

    private Slot? BuildAssimpSlotHierarchy(AssimpScene scene)
    {
        ClearCreatedSlots();

        var modelSlot = Owner.Slot;
        if (modelSlot.GetComponent<ObjectRoot>() == null)
        {
            var objRoot = modelSlot.AttachComponent<ObjectRoot>();
            objRoot.ObjectName.Value = modelSlot.SlotName.Value;
        }

        if (modelSlot.GetComponent<Grabbable>() == null)
            modelSlot.AttachComponent<Grabbable>();

        int created = 0;
        var namedSlots = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
        var pendingSkinnedMeshes = new List<SkinnedMeshRenderer>();

        var importRootSlot = modelSlot.AddSlot("ImportedSceneRoot");
        _createdSlots.Add(importRootSlot);
        created++;

        var sceneRootSlot = importRootSlot.AddSlot(GetSafeSlotName(scene.RootNode.Name, "SceneRoot"));
        _createdSlots.Add(sceneRootSlot);
        created++;
        RegisterNamedSlot(sceneRootSlot, namedSlots);
        ApplyAssimpNodeTransform(sceneRootSlot, scene.RootNode.Transform);

        PopulateAssimpNode(scene, scene.RootNode, sceneRootSlot, ref created, namedSlots, pendingSkinnedMeshes);

        var boneNames = CollectAssimpBoneNames(scene);
        if (boneNames.Count > 0)
        {
            var skelBuilder = BuildAssimpSkeletonComponents(importRootSlot, namedSlots, boneNames);
            if (skelBuilder != null)
            {
                foreach (var renderer in pendingSkinnedMeshes)
                {
                    renderer.Skeleton.Target = skelBuilder;
                    renderer.SetupBonesFromSkeleton(skelBuilder);
                }
            }
        }

        LumoraLogger.Log($"ModelDataHook: Built {created} hierarchy slots from Assimp scene");
        return importRootSlot;
    }

    /// <summary>
    /// Walk the Godot node tree and mirror it into Lumora slots with real components.
    /// </summary>
    private void WalkNodeTree(global::Godot.Node node, Slot parentSlot, ref int created,
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
                Skeleton3D? gltfSkel = ResolveMeshSkeleton(meshInstance);
                pendingSkinnedMeshes.Add(new PendingSkinnedMesh
                {
                    GodotMesh = meshInstance,
                    MeshSlot = nodeSlot,
                    GltfSkeleton = gltfSkel
                });
                meshInstance.Visible = false;
            }
            else
            {
                AttachStaticMeshComponents(nodeSlot, meshInstance);
            }
        }

        foreach (var child in node.GetChildren())
        {
            if (child is global::Godot.Node childNode)
                WalkNodeTree(childNode, nodeSlot, ref created,
                    ref foundSkeleton, ref skeletonParentSlot, pendingSkinnedMeshes);
        }
    }

    private void PopulateAssimpNode(
        AssimpScene scene,
        AssimpNode sourceNode,
        Slot targetSlot,
        ref int created,
        Dictionary<string, Slot> namedSlots,
        List<SkinnedMeshRenderer> pendingSkinnedMeshes)
    {
        if (created >= MaxSlotNodes)
            return;

        if (sourceNode.HasMeshes)
        {
            for (int i = 0; i < sourceNode.MeshIndices.Count && created < MaxSlotNodes; i++)
            {
                int meshIndex = sourceNode.MeshIndices[i];
                if (meshIndex < 0 || meshIndex >= scene.MeshCount)
                    continue;

                var mesh = scene.Meshes[meshIndex];
                if (mesh == null || !mesh.HasVertices || mesh.FaceCount == 0)
                    continue;

                Slot meshSlot = targetSlot;
                if (sourceNode.MeshIndices.Count > 1)
                {
                    meshSlot = targetSlot.AddSlot(GetSafeSlotName(mesh.Name, $"Mesh_{i}"));
                    _createdSlots.Add(meshSlot);
                    created++;
                }

                AssimpMaterial? material = null;
                if (mesh.MaterialIndex >= 0 && mesh.MaterialIndex < scene.MaterialCount)
                    material = scene.Materials[mesh.MaterialIndex];

                var renderer = AttachAssimpMeshComponents(meshSlot, mesh, material);
                if (renderer != null && mesh.HasBones)
                    pendingSkinnedMeshes.Add(renderer);
            }
        }

        if (!sourceNode.HasChildren)
            return;

        foreach (var child in sourceNode.Children)
        {
            if (created >= MaxSlotNodes)
                break;

            var childSlot = targetSlot.AddSlot(GetSafeSlotName(child.Name, "Node"));
            _createdSlots.Add(childSlot);
            created++;
            RegisterNamedSlot(childSlot, namedSlots);
            ApplyAssimpNodeTransform(childSlot, child.Transform);
            PopulateAssimpNode(scene, child, childSlot, ref created, namedSlots, pendingSkinnedMeshes);
        }
    }

    private SkinnedMeshRenderer? AttachAssimpMeshComponents(Slot meshSlot, AssimpMesh mesh, AssimpMaterial? material)
    {
        if (!ExtractAssimpMeshData(mesh,
                out var vertices,
                out var normals,
                out var uvs,
                out var indices,
                out var boneIndices,
                out var boneWeights,
                out var boneNames))
        {
            return null;
        }

        var smr = meshSlot.AttachComponent<SkinnedMeshRenderer>();
        smr.SetMeshData(vertices, normals, uvs, indices, boneIndices, boneWeights, boneNames);

        var matSlot = meshSlot.AddSlot("Material");
        _createdSlots.Add(matSlot);
        var pbs = matSlot.AttachComponent<PBS_Metallic>();
        PopulatePBSFromAssimpMaterial(pbs, material);
        smr.Material.Target = pbs;
        return smr;
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
    /// Add bone slots inline in the hierarchy.
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

            var rest = skeleton.GetBoneRest(i);
            boneSlot.LocalPosition.Value = new float3(rest.Origin.X, rest.Origin.Y, rest.Origin.Z);
            var q = rest.Basis.GetRotationQuaternion();
            boneSlot.LocalRotation.Value = new floatQ(q.X, q.Y, q.Z, q.W);
            var s = rest.Basis.Scale;
            boneSlot.LocalScale.Value = new float3(s.X, s.Y, s.Z);
        }
    }

    // ------------------------------------------------------------------
    // Skeleton + BipedRig
    // ------------------------------------------------------------------

    private SkeletonBuilder? BuildSkeletonComponents(Skeleton3D skeleton, Slot skeletonSlot)
    {
        int boneCount = skeleton.GetBoneCount();
        if (boneCount == 0) return null;

        var boneNameToSlot = new Dictionary<string, Slot>();
        CollectBoneSlots(skeletonSlot, boneNameToSlot);

        var skelBuilder = skeletonSlot.AttachComponent<SkeletonBuilder>();

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

        FinalizeSkeletonBuilder(skelBuilder);
        return skelBuilder;
    }

    private SkeletonBuilder? BuildAssimpSkeletonComponents(
        Slot importRootSlot,
        Dictionary<string, Slot> namedSlots,
        HashSet<string> weightedBoneNames)
    {
        var weightedBoneSlots = new List<Slot>();
        foreach (var boneName in weightedBoneNames)
        {
            if (namedSlots.TryGetValue(boneName, out var slot) && !weightedBoneSlots.Contains(slot))
                weightedBoneSlots.Add(slot);
        }

        if (weightedBoneSlots.Count == 0)
            return null;

        var skeletonAnchor = FindLowestCommonAncestor(weightedBoneSlots) ?? importRootSlot;
        var requiredBoneSlots = new HashSet<Slot>();
        foreach (var boneSlot in weightedBoneSlots)
        {
            var current = boneSlot;
            while (current != null && current != skeletonAnchor)
            {
                requiredBoneSlots.Add(current);
                current = current.Parent;
            }

            if (current == skeletonAnchor && weightedBoneSlots.Contains(current))
                requiredBoneSlots.Add(current);
        }

        if (requiredBoneSlots.Count == 0)
            return null;

        var skelBuilder = skeletonAnchor.AttachComponent<SkeletonBuilder>();
        var rootBoneCandidates = new List<Slot>();
        foreach (var boneSlot in requiredBoneSlots)
        {
            if (!HasAncestorInSet(boneSlot, requiredBoneSlots))
                rootBoneCandidates.Add(boneSlot);
        }

        skelBuilder.RootBone.Target = rootBoneCandidates.Count == 1
            ? rootBoneCandidates[0]
            : skeletonAnchor;

        AddAssimpBonesRecursive(skeletonAnchor, requiredBoneSlots, skelBuilder);
        FinalizeSkeletonBuilder(skelBuilder);
        return skelBuilder;
    }

    private void FinalizeSkeletonBuilder(SkeletonBuilder skelBuilder)
    {
        skelBuilder.IsBuilt.Value = true;
        _builtSkeleton = skelBuilder;
        LumoraLogger.Log($"ModelDataHook: Built SkeletonBuilder with {skelBuilder.BoneCount} bones");

        bool isAvatar = Owner.IsAvatar.Value || (Owner.ImportSettings?.IsAvatarImport ?? false);
        if (isAvatar)
        {
            var rig = skelBuilder.Slot.AttachComponent<BipedRig>();
            rig.PopulateFromSkeleton(skelBuilder);
            LumoraLogger.Log($"ModelDataHook: Created BipedRig, IsBiped={rig.IsBiped}");
        }
    }

    private void AddAssimpBonesRecursive(Slot current, HashSet<Slot> requiredBoneSlots, SkeletonBuilder skeletonBuilder)
    {
        if (requiredBoneSlots.Contains(current))
        {
            skeletonBuilder.AddBone(current.SlotName.Value, current, SlotTransformToFloat4x4(current));
        }

        foreach (var child in current.Children)
            AddAssimpBonesRecursive(child, requiredBoneSlots, skeletonBuilder);
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

    private static bool HasAncestorInSet(Slot slot, HashSet<Slot> set)
    {
        var parent = slot.Parent;
        while (parent != null)
        {
            if (set.Contains(parent))
                return true;
            parent = parent.Parent;
        }

        return false;
    }

    private static Slot? FindLowestCommonAncestor(IReadOnlyList<Slot> slots)
    {
        if (slots == null || slots.Count == 0)
            return null;

        var candidate = slots[0];
        while (candidate != null)
        {
            bool isCommon = true;
            for (int i = 1; i < slots.Count; i++)
            {
                if (!IsAncestorOrSelf(candidate, slots[i]))
                {
                    isCommon = false;
                    break;
                }
            }

            if (isCommon)
                return candidate;

            candidate = candidate.Parent;
        }

        return null;
    }

    private static bool IsAncestorOrSelf(Slot ancestor, Slot slot)
    {
        var current = slot;
        while (current != null)
        {
            if (current == ancestor)
                return true;
            current = current.Parent;
        }

        return false;
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

    private static float4x4 SlotTransformToFloat4x4(Slot slot)
    {
        return float4x4.TRS(slot.LocalPosition.Value, slot.LocalRotation.Value, slot.LocalScale.Value);
    }

    // ------------------------------------------------------------------
    // Mesh component attachment
    // ------------------------------------------------------------------

    /// <summary>
    /// Attach MeshRenderer + PBS_Metallic to a static mesh node.
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

            if (ExtractSurfaceMeshData(mesh, surfIdx,
                out var verts, out var normals, out var uvs, out var indices,
                out var boneIdx, out var boneWgt))
            {
                smr.SetMeshData(verts, normals, uvs, indices, boneIdx, boneWgt, boneNamesArray);
            }

            if (skelBuilder != null)
            {
                smr.Skeleton.Target = skelBuilder;
                smr.SetupBonesFromSkeleton(skelBuilder);
            }

            var matSlot = surfSlot.AddSlot("Material");
            _createdSlots.Add(matSlot);
            var pbs = matSlot.AttachComponent<PBS_Metallic>();
            PopulatePBSFromGodotMaterial(pbs, godotMesh.GetActiveMaterial(surfIdx));
            smr.Material.Target = pbs;
        }

        LumoraLogger.Log($"ModelDataHook: Attached SkinnedMeshRenderer(s) for '{godotMesh.Name}' ({surfaceCount} surfaces)");
    }

    // ------------------------------------------------------------------
    // Mesh data extraction
    // ------------------------------------------------------------------

    private static bool ExtractSurfaceMeshData(global::Godot.Mesh mesh, int surfIdx,
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

        var vertVariant = arrays[(int)Mesh.ArrayType.Vertex];
        if (vertVariant.VariantType == Variant.Type.Nil) return false;
        var gVerts = vertVariant.AsVector3Array();
        if (gVerts == null || gVerts.Length == 0) return false;

        vertices = new float3[gVerts.Length];
        for (int i = 0; i < gVerts.Length; i++)
            vertices[i] = new float3(gVerts[i].X, gVerts[i].Y, gVerts[i].Z);

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

    private static bool ExtractAssimpMeshData(
        AssimpMesh mesh,
        out float3[] vertices,
        out float3[]? normals,
        out float2[]? uvs,
        out int[] indices,
        out int4[]? boneIndices,
        out float4[]? boneWeights,
        out string[]? boneNames)
    {
        vertices = Array.Empty<float3>();
        normals = null;
        uvs = null;
        indices = Array.Empty<int>();
        boneIndices = null;
        boneWeights = null;
        boneNames = null;

        if (!mesh.HasVertices || mesh.VertexCount == 0)
            return false;

        vertices = new float3[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var v = mesh.Vertices[i];
            vertices[i] = new float3(v.X, v.Y, v.Z);
        }

        if (mesh.HasNormals && mesh.Normals.Count == mesh.VertexCount)
        {
            normals = new float3[mesh.VertexCount];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var n = mesh.Normals[i];
                normals[i] = new float3(n.X, n.Y, n.Z);
            }
        }

        if (mesh.TextureCoordinateChannelCount > 0 &&
            mesh.TextureCoordinateChannels[0] != null &&
            mesh.TextureCoordinateChannels[0].Count == mesh.VertexCount)
        {
            uvs = new float2[mesh.VertexCount];
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                var uv = mesh.TextureCoordinateChannels[0][i];
                uvs[i] = new float2(uv.X, uv.Y);
            }
        }

        var triangleIndices = new List<int>(mesh.FaceCount * 3);
        foreach (var face in mesh.Faces)
        {
            if (!face.HasIndices || face.IndexCount < 3)
                continue;

            if (face.IndexCount == 3)
            {
                triangleIndices.Add(face.Indices[0]);
                triangleIndices.Add(face.Indices[2]);
                triangleIndices.Add(face.Indices[1]);
                continue;
            }

            for (int i = 1; i < face.IndexCount - 1; i++)
            {
                triangleIndices.Add(face.Indices[0]);
                triangleIndices.Add(face.Indices[i + 1]);
                triangleIndices.Add(face.Indices[i]);
            }
        }

        if (triangleIndices.Count == 0)
            return false;

        indices = triangleIndices.ToArray();

        if (!mesh.HasBones || mesh.BoneCount == 0)
            return true;

        boneNames = new string[mesh.BoneCount];
        boneIndices = new int4[mesh.VertexCount];
        boneWeights = new float4[mesh.VertexCount];

        for (int boneIndex = 0; boneIndex < mesh.BoneCount; boneIndex++)
        {
            var bone = mesh.Bones[boneIndex];
            boneNames[boneIndex] = GetSafeSlotName(bone.Name, $"Bone_{boneIndex}");

            foreach (var vertexWeight in bone.VertexWeights)
            {
                int vertexId = vertexWeight.VertexID;
                if (vertexId < 0 || vertexId >= mesh.VertexCount)
                    continue;

                AddBoneWeight(ref boneIndices[vertexId], ref boneWeights[vertexId], boneIndex, vertexWeight.Weight);
            }
        }

        for (int vertexIndex = 0; vertexIndex < mesh.VertexCount; vertexIndex++)
            NormalizeBoneWeights(ref boneWeights[vertexIndex]);

        return true;
    }

    private static void AddBoneWeight(ref int4 indices, ref float4 weights, int boneIndex, float weight)
    {
        if (weight <= 0f)
            return;

        if (weight > weights.x)
        {
            weights.w = weights.z;
            indices.w = indices.z;
            weights.z = weights.y;
            indices.z = indices.y;
            weights.y = weights.x;
            indices.y = indices.x;
            weights.x = weight;
            indices.x = boneIndex;
            return;
        }

        if (weight > weights.y)
        {
            weights.w = weights.z;
            indices.w = indices.z;
            weights.z = weights.y;
            indices.z = indices.y;
            weights.y = weight;
            indices.y = boneIndex;
            return;
        }

        if (weight > weights.z)
        {
            weights.w = weights.z;
            indices.w = indices.z;
            weights.z = weight;
            indices.z = boneIndex;
            return;
        }

        if (weight > weights.w)
        {
            weights.w = weight;
            indices.w = boneIndex;
        }
    }

    private static void NormalizeBoneWeights(ref float4 weights)
    {
        float total = weights.x + weights.y + weights.z + weights.w;
        if (total <= 0f)
        {
            weights = new float4(1f, 0f, 0f, 0f);
            return;
        }

        float inv = 1f / total;
        weights.x *= inv;
        weights.y *= inv;
        weights.z *= inv;
        weights.w *= inv;
    }

    // ------------------------------------------------------------------
    // Material population
    // ------------------------------------------------------------------

    private static void PopulatePBSFromGodotMaterial(PBS_Metallic pbs, global::Godot.Material? godotMat)
    {
        if (godotMat is not StandardMaterial3D std) return;

        var c = std.AlbedoColor;
        pbs.AlbedoColor.Value = new colorHDR(c.R, c.G, c.B, c.A);

        pbs.Metallic.Value = std.Metallic;
        pbs.Smoothness.Value = 1.0f - std.Roughness;
        pbs.NormalScale.Value = std.NormalScale;

        if (std.EmissionEnabled)
        {
            var e = std.Emission;
            float mult = std.EmissionEnergyMultiplier;
            pbs.EmissiveColor.Value = new colorHDR(e.R * mult, e.G * mult, e.B * mult, 1f);
        }

        pbs.BlendMode.Value = std.Transparency switch
        {
            BaseMaterial3D.TransparencyEnum.AlphaScissor => BlendMode.Cutout,
            BaseMaterial3D.TransparencyEnum.Disabled => BlendMode.Opaque,
            _ => BlendMode.Transparent,
        };

        pbs.AlphaCutoff.Value = std.AlphaScissorThreshold;
        pbs.Culling.Value = std.CullMode == BaseMaterial3D.CullModeEnum.Disabled
            ? Culling.None
            : Culling.Back;
    }

    private static void PopulatePBSFromAssimpMaterial(PBS_Metallic pbs, AssimpMaterial? material)
    {
        if (material == null)
            return;

        if (material.HasColorDiffuse)
        {
            var color = material.ColorDiffuse;
            float alpha = material.HasOpacity ? Mathf.Clamp(material.Opacity, 0f, 1f) : Mathf.Clamp(color.W, 0f, 1f);
            pbs.AlbedoColor.Value = new colorHDR(color.X, color.Y, color.Z, alpha);
        }
        else if (material.HasOpacity)
        {
            var existing = pbs.AlbedoColor.Value;
            pbs.AlbedoColor.Value = new colorHDR(existing.r, existing.g, existing.b, Mathf.Clamp(material.Opacity, 0f, 1f));
        }

        if (material.HasColorEmissive)
        {
            var emissive = material.ColorEmissive;
            pbs.EmissiveColor.Value = new colorHDR(emissive.X, emissive.Y, emissive.Z, 1f);
        }

        if (material.HasShininess)
        {
            pbs.Smoothness.Value = Mathf.Clamp(material.Shininess / 128f, 0f, 1f);
        }

        pbs.Culling.Value = material.HasTwoSided && material.IsTwoSided
            ? Culling.None
            : Culling.Back;

        if (material.HasOpacity && material.Opacity < 0.999f)
            pbs.BlendMode.Value = BlendMode.Transparent;
        else
            pbs.BlendMode.Value = BlendMode.Opaque;
    }

    // ------------------------------------------------------------------
    // Assimp helpers
    // ------------------------------------------------------------------

    private static void RegisterNamedSlot(Slot slot, Dictionary<string, Slot> namedSlots)
    {
        var name = slot.SlotName.Value;
        if (!string.IsNullOrWhiteSpace(name) && !namedSlots.ContainsKey(name))
            namedSlots[name] = slot;
    }

    private static HashSet<string> CollectAssimpBoneNames(AssimpScene scene)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!scene.HasMeshes)
            return names;

        foreach (var mesh in scene.Meshes)
        {
            if (mesh == null || !mesh.HasBones)
                continue;

            foreach (var bone in mesh.Bones)
            {
                if (!string.IsNullOrWhiteSpace(bone.Name))
                    names.Add(bone.Name.Trim());
            }
        }

        return names;
    }

    private static string GetSafeSlotName(string? rawName, string fallback)
    {
        var trimmed = rawName?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static void ApplyAssimpNodeTransform(Slot slot, NumericsMatrix4x4 transform)
    {
        if (!NumericsMatrix4x4.Decompose(transform, out var scale, out var rotation, out var translation))
        {
            scale = NumericsVector3.One;
            rotation = System.Numerics.Quaternion.Identity;
            translation = new NumericsVector3(transform.M41, transform.M42, transform.M43);
        }

        slot.LocalPosition.Value = new float3(translation.X, translation.Y, translation.Z);
        slot.LocalRotation.Value = new floatQ(rotation.X, rotation.Y, rotation.Z, rotation.W);
        slot.LocalScale.Value = new float3(scale.X, scale.Y, scale.Z);
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
