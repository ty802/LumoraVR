// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Lumora.Core.Components;
using Lumora.Core.Components.Avatar;
using Lumora.Core.Input;
using Lumora.Core.Logging;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

/// <summary>
/// Settings for model import.
/// </summary>
public class ModelImportSettings
{
    /// <summary>Scale factor to apply during import.</summary>
    public float Scale { get; set; } = 1.0f;

    /// <summary>Whether to import bones/skeleton.</summary>
    public bool ImportBones { get; set; } = true;

    /// <summary>Whether to import animations.</summary>
    public bool ImportAnimations { get; set; } = true;

    /// <summary>Whether to import materials.</summary>
    public bool ImportMaterials { get; set; } = true;

    /// <summary>Whether to generate colliders.</summary>
    public bool GenerateColliders { get; set; } = false;

    /// <summary>Whether to setup IK for humanoid avatars.</summary>
    public bool SetupIK { get; set; } = true;

    /// <summary>Whether to center the model.</summary>
    public bool Center { get; set; } = true;

    /// <summary>Whether to rescale to standard height.</summary>
    public bool Rescale { get; set; } = true;

    /// <summary>Target height when rescaling.</summary>
    public float TargetHeight { get; set; } = 1.7f;

    /// <summary>Whether to force T-pose for humanoids.</summary>
    public bool ForceTpose { get; set; } = false;

    /// <summary>Whether this is an avatar import.</summary>
    public bool IsAvatarImport { get; set; } = false;
}

/// <summary>
/// Result of a model import operation.
/// </summary>
public class ModelImportResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = null!;
    public Slot RootSlot { get; set; } = null!;
    public SkeletonBuilder Skeleton { get; set; } = null!;
    public List<SkinnedMeshRenderer> SkinnedMeshes { get; set; } = new();
    public string LocalUri { get; set; } = null!;
}

/// <summary>
/// Imports 3D models using Godot's GLTFDocument.
/// </summary>
public static class ModelImporter
{
    /// <summary>
    /// Supported model file extensions.
    /// </summary>
    public static readonly string[] SupportedExtensions = new[]
    {
        ".glb",
        ".gltf",
        ".vrm",
        ".fbx",
        ".obj",
        ".dae",
        ".3ds",
        ".stl",
        ".ply",
        ".x",
        ".ase"
    };

    /// <summary>
    /// Check if a file is a supported model format.
    /// </summary>
    public static bool IsSupportedFormat(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        foreach (var ext in SupportedExtensions)
        {
            if (extension == ext)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Import a model file and create the slot hierarchy.
    /// This is the core API method called from Godot side.
    /// </summary>
    public static async Task<ModelImportResult> ImportModelAsync(
        string filePath,
        Slot targetSlot,
        ModelImportSettings? settings = null,
        LocalDB? localDB = null,
        IProgress<(float progress, string status)>? progress = null)
    {
        settings ??= new ModelImportSettings();
        var result = new ModelImportResult();

        if (!File.Exists(filePath))
        {
            result.ErrorMessage = $"File not found: {filePath}";
            Logger.Error($"ModelImporter: {result.ErrorMessage}");
            return result;
        }

        if (!IsSupportedFormat(filePath))
        {
            result.ErrorMessage = $"Unsupported format: {Path.GetExtension(filePath)}";
            Logger.Error($"ModelImporter: {result.ErrorMessage}");
            return result;
        }

        // EVERY supported format goes through the one Phos importer - a single Assimp pipeline (glb/gltf/vrm/fbx/
        // obj/dae/...) that decodes off-thread, builds the mesh on a worker thread, and points a SkinnedMeshRenderer
        // at a content-hashed MeshAsset instead of giant synced inline vertex lists. The old ModelData/ModelDataHook
        // path that set inline lists synchronously on the main thread (the import-freeze cause) has been removed. -xlinka
        return await ImportModelPhosAsync(filePath, targetSlot, settings, localDB, progress);
    }

    /// <summary>
    /// Import a model as an avatar.
    /// Sets up IK, skeleton, and avatar-specific components.
    /// </summary>
    public static async Task<ModelImportResult> ImportAvatarAsync(
        string filePath,
        Slot targetSlot,
        LocalDB? localDB = null,
        IProgress<(float progress, string status)>? progress = null)
    {
        var settings = new ModelImportSettings
        {
            ImportBones = true,
            ImportAnimations = false,
            SetupIK = true,
            IsAvatarImport = true,
            Rescale = true,
            TargetHeight = 1.7f
        };

        return await ImportModelAsync(filePath, targetSlot, settings, localDB, progress);
    }

    // The single Assimp + Phos import path (the reference's one-importer design): parse the file once with
    // Assimp, build a Slot per node with its local transform, build a SkeletonBuilder from the meshes' bone
    // names (bones are just named Slots), and per mesh attach a MeshProvider (decoding only that mesh via
    // MeshIndex) + a SkinnedMeshRenderer bound to the skeleton by name. No Godot GltfDocument, no SharpGLTF, no
    // ModelData/ModelDataHook. Assimp/glTF/Godot are all right-handed Y-up, so no coordinate mirror is needed
    // (unlike the left-handed reference engine, whose PreprocessScene mirror we deliberately skip). -xlinka
    private static async Task<ModelImportResult> ImportModelPhosAsync(
        string filePath, Slot targetSlot, ModelImportSettings settings, LocalDB? localDB,
        IProgress<(float progress, string status)>? progress)
    {
        var result = new ModelImportResult();
        try
        {
            progress?.Report((0.1f, "Reading model file..."));
            var bytes = await System.Threading.Tasks.Task.Run(() => File.ReadAllBytes(filePath)).ConfigureAwait(false);

            // Record in the content-addressed local DB (dedup + networking). The MeshProvider below points at this
            // local:// URI so it replicates to joiners; the loader resolves its extension back through LocalDB.
            if (localDB != null)
                result.LocalUri = await localDB.ImportLocalAssetAsync(filePath, LocalDB.ImportLocation.Copy).ConfigureAwait(false);

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            string hint = ext == ".vrm" ? ".glb" : ext; // VRM is GLB; Assimp needs a known hint
            string modelDir = Path.GetDirectoryName(filePath) ?? ""; // for resolving external texture files

            // SAME post-process steps as the per-mesh decode, or the mesh indices wouldn't line up.
            // Force the parse onto a worker thread: it's seconds of synchronous work, and if an upstream await
            // (file read / local-DB import) ever completes synchronously the continuation would otherwise run it
            // INLINE on the main thread and freeze rendering for the whole parse. Task.Run makes off-main deterministic.
            Assimp.Scene scene = await System.Threading.Tasks.Task.Run(() =>
            {
                using var actx = new Assimp.AssimpContext();
                using var ms = new MemoryStream(bytes, writable: false);
                return actx.ImportFileFromStream(ms, MeshDecoder.GetAssimpPostProcessSteps(perMesh: true), hint);
            }).ConfigureAwait(false);

            if (scene == null || !scene.HasMeshes || scene.MeshCount == 0)
            {
                result.ErrorMessage = "Assimp produced no meshes";
                Logger.Error($"ModelImporter(Phos): {result.ErrorMessage} for '{filePath}'");
                return result;
            }

            // PHASE A (off-thread): resolve every used material's textures into local:// URIs up front. Texture
            // saves hit the local DB (file IO) and must NOT run on the world thread - so do them here, off-thread.
            // The world-thread build below only ATTACHES components from these resolved results (no IO, no await).
            progress?.Report((0.3f, "Decoding materials..."));
            var resolvedMaterials = new Dictionary<int, ResolvedMaterial?>();
            foreach (var amesh in scene.Meshes)
            {
                if (amesh == null || resolvedMaterials.ContainsKey(amesh.MaterialIndex)) continue;
                resolvedMaterials[amesh.MaterialIndex] = await ResolveMaterialAsync(scene, amesh.MaterialIndex, modelDir, localDB).ConfigureAwait(false);
            }

            // PHASE B (world thread, chunked): EVERY data-model + Godot scene-tree write happens from here on, on
            // the world thread under the engine's Implementer lock - the only place they're legal. We hop on per
            // logical chunk (and per mesh node) via OnWorldAsync so each lands on its own frame and the world keeps
            // rendering between chunks (load-in-pieces). The heavy decode + texture IO already ran off-thread above. -xlinka
            World world = targetSlot.World;

            // Chunk 1: node hierarchy (one Slot per Assimp node, with its local TRS).
            progress?.Report((0.4f, "Building hierarchy..."));
            Slot modelSlot = null!;
            var nameToSlot = new Dictionary<string, Slot>();
            var meshNodeSlots = new List<(Assimp.Node node, Slot slot)>();
            await OnWorldAsync(world, () =>
            {
                modelSlot = targetSlot.AddSlot(Path.GetFileNameWithoutExtension(filePath));
                result.RootSlot = modelSlot;
                WalkAssimpNode(scene.RootNode, modelSlot, nameToSlot, meshNodeSlots);
            });

            // Chunk 2: skeleton (union of mesh bone names) + avatar rig/IK if it classifies as a biped.
            progress?.Report((0.55f, "Building skeleton..."));
            SkeletonBuilder? skelBuilder = null;
            await OnWorldAsync(world, () => skelBuilder = BuildSkeletonAndRig(scene, modelSlot, nameToSlot, settings, result));

            // Chunk 3..N: per-mesh renderers - one mesh NODE per frame so each renderer's GPU build lands on its own
            // frame and the world renders between them (load-in-pieces). Prefer the content-hashed local:// URI - it
            // replicates to joiners (they gather the same bytes by hash); fall back to the file path with no local DB.
            progress?.Report((0.7f, "Attaching renderers..."));
            var meshUri = !string.IsNullOrEmpty(result.LocalUri) ? new Uri(result.LocalUri) : new Uri(filePath);
            int totalMeshNodes = meshNodeSlots.Count;
            int meshNodeDone = 0;
            foreach (var (node, slot) in meshNodeSlots)
            {
                var capturedNode = node;
                var capturedSlot = slot;
                await OnWorldAsync(world, () =>
                {
                    var indices = capturedNode.MeshIndices;
                    for (int k = 0; k < indices.Count; k++)
                    {
                        int meshIdx = indices[k];
                        if (meshIdx < 0 || meshIdx >= scene.MeshCount) continue;
                        var amesh = scene.Meshes[meshIdx];
                        if (amesh == null) continue;

                        Slot meshSlot = indices.Count == 1 ? capturedSlot : capturedSlot.AddSlot($"Mesh{meshIdx}");
                        var provider = meshSlot.AttachComponent<MeshProvider>();
                        provider.URL.Value = meshUri;
                        provider.MeshIndex.Value = meshIdx;

                        resolvedMaterials.TryGetValue(amesh.MaterialIndex, out var rmat);
                        var material = BuildMaterialSync(meshSlot, rmat);

                        bool skinned = amesh.HasBones || amesh.HasMeshAnimationAttachments;
                        if (skinned)
                        {
                            var smr = meshSlot.AttachComponent<SkinnedMeshRenderer>();
                            smr.MeshAsset.Target = provider;
                            if (skelBuilder != null) smr.Skeleton.Target = skelBuilder;
                            if (material != null) smr.Material.Target = material;
                            result.SkinnedMeshes.Add(smr);
                        }
                        else
                        {
                            // Static mesh: a plain MeshRenderer off the Phos asset. (MeshRenderer's asset render
                            // path is still a hook TODO, so fully static models won't show until that lands.)
                            var mr = meshSlot.AttachComponent<MeshRenderer>();
                            mr.Mesh.Target = provider;
                            if (material != null) mr.Material.Target = material;

                            // Optional collision geometry off the same mesh provider (skinned meshes are skipped,
                            // like the reference - their geometry deforms, so a static collider wouldn't track it).
                            if (settings.GenerateColliders)
                                meshSlot.AttachComponent<MeshCollider>().Mesh.Target = provider;
                        }
                    }
                });
                meshNodeDone++;
                progress?.Report((0.7f + 0.25f * (totalMeshNodes > 0 ? (float)meshNodeDone / totalMeshNodes : 1f), "Building meshes..."));
            }

            // Final chunk: rescale to target height + center.
            if (settings.Rescale || settings.Center)
                await OnWorldAsync(world, () => ApplyModelTransform(scene, modelSlot, settings, skelBuilder));

            progress?.Report((1.0f, "Import complete!"));
            result.Success = true;
            Logger.Log($"ModelImporter(Phos/Assimp): '{Path.GetFileNameWithoutExtension(filePath)}' - {scene.MeshCount} meshes, {result.SkinnedMeshes.Count} skinned");
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            Logger.Error($"ModelImporter(Phos): failed '{filePath}': {ex.Message}");
        }
        return result;
    }

    // Marshal a chunk of data-model / Godot work onto the WORLD thread and await it. The action runs inside the
    // next ProcessSynchronousActions (Stage 1 of World.Update) - on the engine's render thread, under the
    // Implementer lock - which is the ONLY place these writes are legal: the data model rejects modifications from
    // a "non-locking thread" and Godot rejects scene-tree edits (AddChild) off the main thread. That's exactly the
    // crash we hit when the build ran on the parse's worker thread.
    // The TCS resumes the caller ASYNCHRONOUSLY (off-thread), so the NEXT awaited chunk is queued from off-thread
    // and lands on the FOLLOWING frame instead of draining inline this frame - that's what spreads the build across
    // frames so the world keeps rendering between chunks (load-in-pieces, like the reference). -xlinka
    private static System.Threading.Tasks.Task OnWorldAsync(World world, Action action)
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>(
            System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
        if (world == null)
        {
            // No world to marshal onto (shouldn't happen for a live import) - run inline so we never hang.
            try { action(); tcs.TrySetResult(true); } catch (Exception e) { tcs.TrySetException(e); }
            return tcs.Task;
        }
        world.RunSynchronously(() =>
        {
            try { action(); tcs.TrySetResult(true); }
            catch (Exception e) { tcs.TrySetException(e); }
        });
        return tcs.Task;
    }

    // Build the skeleton (union of the meshes' bone names) and, when it classifies as a biped, the avatar rig +
    // full-body IK. ALL data-model writes - call it on the world thread (from inside OnWorldAsync). Returns the
    // SkeletonBuilder, or null when the model has no bones. -xlinka
    private static SkeletonBuilder? BuildSkeletonAndRig(Assimp.Scene scene, Slot modelSlot,
        Dictionary<string, Slot> nameToSlot, ModelImportSettings settings, ModelImportResult result)
    {
        var boneNames = new List<string>();
        var boneSeen = new HashSet<string>();
        foreach (var amesh in scene.Meshes)
            if (amesh != null && amesh.HasBones)
                foreach (var b in amesh.Bones)
                    if (b != null && !string.IsNullOrEmpty(b.Name) && boneSeen.Add(b.Name))
                        boneNames.Add(b.Name);

        SkeletonBuilder? skelBuilder = null;
        if (boneNames.Count > 0)
        {
            skelBuilder = modelSlot.AttachComponent<SkeletonBuilder>();
            Slot? rootBoneSlot = null;
            foreach (var bn in boneNames)
            {
                if (!nameToSlot.TryGetValue(bn, out var bslot)) continue;
                var p = bslot.LocalPosition.Value;
                var rq = bslot.LocalRotation.Value;
                var sc = bslot.LocalScale.Value;
                var rest = float4x4.Translate(p) * float4x4.Rotate(rq) * float4x4.Scale(sc);
                skelBuilder.AddBone(bn, bslot, rest);
                rootBoneSlot ??= bslot;
            }
            if (rootBoneSlot != null) skelBuilder.RootBone.Target = rootBoneSlot;
            skelBuilder.IsBuilt.Value = true;
            result.Skeleton = skelBuilder;
        }

        // Avatar rig + full-body IK (mirrors AvatarCreator.RunCreate): classify the skeleton into a biped rig and,
        // if it really is a biped, attach AvatarRoot + AvatarIK + auto-placed reference points so the avatar is
        // wearable/drivable. Tracking auto-wires later on equip (AvatarManager.EquipAvatar). Content-gated, not
        // extension-gated: we classify ANY skinned model and only attach the avatar bits when it's actually a biped.
        if (settings.SetupIK && skelBuilder != null && skelBuilder.IsBuilt.Value)
        {
            var rig = modelSlot.GetComponent<BipedRig>() ?? modelSlot.AttachComponent<BipedRig>();
            rig.PopulateFromSkeleton(skelBuilder);
            // Normalize to a T-pose before IK captures the rest pose, when the import asked for it (an A-pose model
            // would otherwise IK-bind crooked). Opt-in - ForceTpose defaults off.
            if (settings.ForceTpose)
                rig.MakeTPose();
            if (rig.IsBiped)
            {
                if (modelSlot.GetComponent<AvatarRoot>() == null)
                    modelSlot.AttachComponent<AvatarRoot>();
                var avatarIk = modelSlot.GetComponent<AvatarIK>() ?? modelSlot.AttachComponent<AvatarIK>();
                avatarIk.Skeleton.Target = skelBuilder;
                avatarIk.Rig.Target = rig;
                bool hasFeet = rig.TryGetBone(BodyNode.LeftFoot) != null && rig.TryGetBone(BodyNode.RightFoot) != null;
                bool hasPelvis = rig.TryGetBone(BodyNode.Hips) != null;
                AvatarCalibration.AutoPlaceReferences(modelSlot, rig, hasFeet, hasPelvis);

                // Tell IK how tall the avatar actually is so it scales to the real body. With rescale on (the
                // default) the model ends up exactly TargetHeight, so use that; otherwise measure head-to-foot from
                // the raw rig. Clamped + sanity-gated so a weird rig can't poison the solver. -xlinka
                if (settings.Rescale)
                {
                    avatarIk.AvatarHeight.Value = settings.TargetHeight;
                }
                else
                {
                    var headBone = rig.TryGetBone(BodyNode.Head);
                    if (headBone != null)
                    {
                        float top = headBone.GlobalPosition.y;
                        float bottom;
                        var lf = rig.TryGetBone(BodyNode.LeftFoot);
                        var rf = rig.TryGetBone(BodyNode.RightFoot);
                        if (lf != null && rf != null)
                            bottom = System.Math.Min(lf.GlobalPosition.y, rf.GlobalPosition.y);
                        else if (hasPelvis)
                        {
                            var h = rig.TryGetBone(BodyNode.Hips);
                            bottom = h.GlobalPosition.y - System.Math.Abs(top - h.GlobalPosition.y);
                        }
                        else bottom = top - 1.7f;
                        float measured = (top - bottom) * 1.08f; // head crown + soles past the bones
                        if (measured > 0.3f && measured < 3.0f)
                            avatarIk.AvatarHeight.Value = measured;
                    }
                }

                // Body colliders (head sphere + per-limb capsules) so a worn avatar has grab/interaction hitboxes -
                // the IK foot raycast already excludes the user's own colliders expecting these.
                avatarIk.GenerateBodyColliders();

                // Draft avatars get see-through, grabbable bone handles so you can SEE the skeleton and pose it
                // before finalizing. Deferred a few frames so the core avatar shows first and the ~30 procedural
                // handle meshes don't pile onto the same frame as the avatar's own mesh builds. -xlinka
                if (settings.IsAvatarImport)
                {
                    var rigForHandles = rig;
                    modelSlot.World?.RunInUpdates(10, () =>
                    {
                        if (rigForHandles != null && !rigForHandles.IsDestroyed)
                            AvatarRigSetup.SetupPoseHandles(rigForHandles);
                    });
                }
                Logger.Log($"ModelImporter(Phos): rigged biped avatar ({rig.Bones.Count} bones)");
            }
            else
            {
                Logger.Log("ModelImporter(Phos): skeleton is not a biped - skipping IK rig.");
            }
        }
        return skelBuilder;
    }

    // Rescale to target height + center the model (otherwise it imports at authored scale/origin - a cm-authored
    // model would be 100x too big and spawn off-origin). Bounds from raw vertices. Data-model writes - call on the
    // world thread (from inside OnWorldAsync). -xlinka
    private static void ApplyModelTransform(Assimp.Scene scene, Slot modelSlot, ModelImportSettings settings, SkeletonBuilder? skelBuilder)
    {
        // The skinned avatar renders as (SkeletonOffset * rawVertex) at rest - SkeletonHook offsets the Skeleton3D
        // by the dropped non-bone-node up-axis rotation (the FBX Armature/RootNode Z-up->Y-up). So we MUST measure
        // the rescale/center bounds in that SAME oriented space; measuring raw (authored) vertices picks the wrong
        // axis for "height" and scales the model wrong (it came in far too big). Recreate that offset here =
        // topmost bone's non-bone parent transform relative to the model root. -xlinka
        float4x4 orient = float4x4.Identity;
        if (skelBuilder != null && modelSlot != null && skelBuilder.BoneCount > 0)
        {
            var boneSet = new HashSet<Slot>();
            for (int bi = 0; bi < skelBuilder.BoneCount; bi++)
            {
                var bs = skelBuilder.BoneSlots[bi];
                if (bs != null) boneSet.Add(bs);
            }
            Slot? topMost = null;
            for (int bi = 0; bi < skelBuilder.BoneCount; bi++)
            {
                var bs = skelBuilder.BoneSlots[bi];
                if (bs == null) continue;
                if (bs.Parent == null || !boneSet.Contains(bs.Parent)) { topMost = bs; break; }
            }
            if (topMost?.Parent != null && topMost.Parent != modelSlot)
            {
                var modelG = float4x4.TRS(modelSlot.GlobalPosition, modelSlot.GlobalRotation, modelSlot.GlobalScale);
                var parentG = float4x4.TRS(topMost.Parent.GlobalPosition, topMost.Parent.GlobalRotation, topMost.Parent.GlobalScale);
                orient = modelG.Inverse * parentG;
            }
        }

        float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
        bool any = false;
        foreach (var amesh in scene.Meshes)
        {
            if (amesh == null || !amesh.HasVertices) continue;
            foreach (var v in amesh.Vertices)
            {
                // Measure in the oriented (rendered) space so "height" is the real up-axis extent.
                var p = orient.MultiplyPoint(new float3(v.X, v.Y, v.Z));
                if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
                if (p.z < minZ) minZ = p.z; if (p.z > maxZ) maxZ = p.z;
                any = true;
            }
        }
        if (!any) return;
        float height = maxY - minY;
        float scale = (settings.Rescale && height > 0.001f) ? settings.TargetHeight / height : 1f;
        modelSlot.LocalScale.Value = new float3(scale, scale, scale);
        if (settings.Center)
        {
            float cx = (minX + maxX) * 0.5f, cz = (minZ + maxZ) * 0.5f;
            modelSlot.LocalPosition.Value = new float3(-cx * scale, -minY * scale, -cz * scale);
        }
    }

    // One Slot per Assimp node, local TRS from the node's transform, parented to the caller's slot. Records a
    // name -> Slot map (first wins on name collision) so mesh bone names resolve to their bone Slot. -xlinka
    private static void WalkAssimpNode(Assimp.Node node, Slot parentSlot,
        Dictionary<string, Slot> nameToSlot, List<(Assimp.Node, Slot)> meshNodeSlots)
    {
        if (node == null) return;

        var slot = parentSlot.AddSlot(string.IsNullOrEmpty(node.Name) ? "Node" : node.Name);
        // Transpose: AssimpNetter stores the row-major aiMatrix4x4 into System.Numerics by field name, so the
        // translation is in M14/M24/M34 - but Decompose reads M41/M42/M43 and would return (0,0,0) for every node,
        // collapsing the whole skeleton rest to the origin. Transposing puts translation where Decompose expects it.
        // This MUST match the transpose in MeshDecoder.ToFloat4x4 or the rest and the bind poses won't cancel. -xlinka
        if (System.Numerics.Matrix4x4.Decompose(System.Numerics.Matrix4x4.Transpose(node.Transform), out var scale, out var rot, out var trans))
        {
            slot.LocalPosition.Value = new float3(trans.X, trans.Y, trans.Z);
            slot.LocalRotation.Value = new floatQ(rot.X, rot.Y, rot.Z, rot.W);
            slot.LocalScale.Value = new float3(scale.X, scale.Y, scale.Z);
        }
        if (!string.IsNullOrEmpty(node.Name) && !nameToSlot.ContainsKey(node.Name))
            nameToSlot[node.Name] = slot;

        if (node.HasMeshes) meshNodeSlots.Add((node, slot));

        foreach (var child in node.Children)
            WalkAssimpNode(child, slot, nameToSlot, meshNodeSlots);
    }

    // A material's textures resolved to loadable local:// URIs plus its base colors. Produced OFF-thread
    // (ResolveMaterialAsync does the local-DB IO), consumed on the world thread (BuildMaterialSync attaches the
    // components). Splitting it this way keeps the file IO off the render thread. -xlinka
    private sealed class ResolvedMaterial
    {
        public colorHDR AlbedoColor = new colorHDR(1f, 1f, 1f, 1f);
        public bool HasAlbedoColor;
        public colorHDR EmissiveColor = new colorHDR(0f, 0f, 0f, 1f);
        public bool HasEmissiveColor;
        public Uri? AlbedoUri;
        public Uri? NormalUri;
        public Uri? EmissiveUri;
        public Uri? MetallicUri;
        public Uri? OcclusionUri;
    }

    // Resolve an Assimp material's colors + texture URIs (OFF-thread). Textures are recorded into the
    // content-addressed local DB so they replicate to joiners (embedded bytes saved by hash; external files
    // imported). Returns null on an invalid index. -xlinka
    private static async Task<ResolvedMaterial?> ResolveMaterialAsync(Assimp.Scene scene, int materialIndex, string modelDir, LocalDB? localDB)
    {
        if (materialIndex < 0 || materialIndex >= scene.MaterialCount)
            return null;
        var amat = scene.Materials[materialIndex];
        var rm = new ResolvedMaterial();

        if (amat.HasColorDiffuse)
        {
            var c = amat.ColorDiffuse; // System.Numerics.Vector4 (rgba)
            rm.AlbedoColor = new colorHDR(c.X, c.Y, c.Z, c.W);
            rm.HasAlbedoColor = true;
        }
        if (amat.HasColorEmissive)
        {
            var e = amat.ColorEmissive;
            rm.EmissiveColor = new colorHDR(e.X, e.Y, e.Z, e.W);
            rm.HasEmissiveColor = true;
        }

        rm.AlbedoUri = await ResolveTextureUriAsync(scene, amat, Assimp.TextureType.BaseColor, modelDir, localDB).ConfigureAwait(false)
                    ?? await ResolveTextureUriAsync(scene, amat, Assimp.TextureType.Diffuse, modelDir, localDB).ConfigureAwait(false);
        rm.NormalUri = await ResolveTextureUriAsync(scene, amat, Assimp.TextureType.Normals, modelDir, localDB).ConfigureAwait(false);
        rm.EmissiveUri = await ResolveTextureUriAsync(scene, amat, Assimp.TextureType.Emissive, modelDir, localDB).ConfigureAwait(false);
        // Metallic/roughness map. glTF packs this combined (B=metallic, G=roughness); Assimp surfaces it as the
        // Metalness slot (and DiffuseRoughness for the FBX-style split). We assign the texture straight through -
        // repacking to a metallic-in-RGB + smoothness-in-alpha layout would need an image encoder we don't ship,
        // so this is the texture without the channel remap. Linear, no mips ideally (handled by the texture hook). -xlinka
        rm.MetallicUri = await ResolveTextureUriAsync(scene, amat, Assimp.TextureType.Metalness, modelDir, localDB).ConfigureAwait(false)
                      ?? await ResolveTextureUriAsync(scene, amat, Assimp.TextureType.Roughness, modelDir, localDB).ConfigureAwait(false);
        rm.OcclusionUri = await ResolveTextureUriAsync(scene, amat, Assimp.TextureType.AmbientOcclusion, modelDir, localDB).ConfigureAwait(false)
                       ?? await ResolveTextureUriAsync(scene, amat, Assimp.TextureType.Lightmap, modelDir, localDB).ConfigureAwait(false);
        return rm;
    }

    // Attach a PBS_Metallic + its texture providers from a pre-resolved material (WORLD thread - call from inside
    // OnWorldAsync). No IO, no awaits - just data-model writes. Returns null when there's nothing to build. -xlinka
    private static MaterialProvider? BuildMaterialSync(Slot parentSlot, ResolvedMaterial? rm)
    {
        if (rm == null)
            return null;
        var matSlot = parentSlot.AddSlot("Material");
        var pbs = matSlot.AttachComponent<PBS_Metallic>();

        // Diagnostic: which maps did this material actually resolve? A stylized model with ONLY albedo should NOT
        // get metallic/occlusion maps - if it does (Assimp handing back a spurious slot), they drive bogus
        // roughness/AO and show as soft specular/occlusion blobs on the surface. -xlinka
        Logger.Log($"BuildMaterialSync[maps] on '{parentSlot.SlotName.Value}': albedo={rm.AlbedoUri} normal={rm.NormalUri != null} emissive={rm.EmissiveUri != null} metallic={rm.MetallicUri != null} occlusion={rm.OcclusionUri != null}");

        if (rm.HasAlbedoColor) pbs.AlbedoColor.Value = rm.AlbedoColor;
        if (rm.HasEmissiveColor) pbs.EmissiveColor.Value = rm.EmissiveColor;

        if (rm.AlbedoUri != null)
        {
            var tex = matSlot.AttachComponent<ImageProvider>();
            tex.URL.Value = rm.AlbedoUri;
            pbs.AlbedoTexture.Target = tex;
            // A base-color FACTOR of ~0 alongside a base-color TEXTURE is legal in glTF, but the shader multiplies
            // texture * factor -> a fully black mesh. Force the tint to white so the texture actually shows. -xlinka
            var ac = pbs.AlbedoColor.Value;
            if (ac.r <= 0.001f && ac.g <= 0.001f && ac.b <= 0.001f)
                pbs.AlbedoColor.Value = new colorHDR(1f, 1f, 1f, ac.a <= 0.001f ? 1f : ac.a);
        }

        if (rm.NormalUri != null)
        {
            var ntex = matSlot.AttachComponent<ImageProvider>();
            ntex.URL.Value = rm.NormalUri;
            ntex.IsNormalMap.Value = true;
            pbs.NormalMap.Target = ntex;
        }

        if (rm.EmissiveUri != null)
        {
            var etex = matSlot.AttachComponent<ImageProvider>();
            etex.URL.Value = rm.EmissiveUri;
            pbs.EmissiveMap.Target = etex;
            // Same trap as albedo: an emissive map with a ~0 emissive factor multiplies to nothing. Force white. -xlinka
            var ec = pbs.EmissiveColor.Value;
            if (ec.r <= 0.001f && ec.g <= 0.001f && ec.b <= 0.001f)
                pbs.EmissiveColor.Value = new colorHDR(1f, 1f, 1f, 1f);
        }

        if (rm.MetallicUri != null)
        {
            var mtex = matSlot.AttachComponent<ImageProvider>();
            mtex.URL.Value = rm.MetallicUri;
            pbs.MetallicMap.Target = mtex;
        }

        if (rm.OcclusionUri != null)
        {
            var aotex = matSlot.AttachComponent<ImageProvider>();
            aotex.URL.Value = rm.OcclusionUri;
            pbs.OcclusionMap.Target = aotex;
        }

        return pbs;
    }

    // Resolve a material texture slot to a loadable URI. Embedded textures (path "*N") are saved into the local DB
    // by content hash; external paths resolve relative to the model dir and are imported into the local DB too.
    // Both yield a local:// URI (replicates to joiners; the loader resolves the extension back through LocalDB).
    // Falls back to a file:// URI when there's no local DB. -xlinka
    private static async Task<Uri?> ResolveTextureUriAsync(Assimp.Scene scene, Assimp.Material amat, Assimp.TextureType type, string modelDir, LocalDB? localDB)
    {
        if (amat.GetMaterialTextureCount(type) == 0)
            return null;
        if (!amat.GetMaterialTexture(type, 0, out var slot) || string.IsNullOrEmpty(slot.FilePath))
            return null;
        string path = slot.FilePath;

        var embedded = scene.GetEmbeddedTexture(path);
        if (embedded != null)
        {
            try
            {
                if (!embedded.HasCompressedData || embedded.CompressedData == null || embedded.CompressedData.Length == 0)
                    return null; // uncompressed embedded textures would need re-encoding; skip for now
                string fmt = string.IsNullOrEmpty(embedded.CompressedFormatHint) ? "png" : embedded.CompressedFormatHint;
                if (localDB != null)
                {
                    var uri = await localDB.SaveAssetAsync(embedded.CompressedData, fmt).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(uri)) return new Uri(uri);
                }
                // No local DB - fall back to a temp file so the texture at least loads on this machine.
                string dir = Path.Combine(Path.GetTempPath(), "lumora_textures");
                Directory.CreateDirectory(dir);
                string tmp = Path.Combine(dir, $"tex_{(uint)path.GetHashCode():x8}.{fmt}");
                if (!File.Exists(tmp))
                    File.WriteAllBytes(tmp, embedded.CompressedData);
                return new Uri(tmp);
            }
            catch
            {
                return null;
            }
        }

        string full = Path.IsPathRooted(path) ? path : Path.Combine(modelDir, path);
        if (!File.Exists(full)) return null;
        if (localDB != null)
        {
            var uri = await localDB.ImportLocalAssetAsync(full, LocalDB.ImportLocation.Copy).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(uri)) return new Uri(uri);
        }
        return new Uri(full);
    }
}
