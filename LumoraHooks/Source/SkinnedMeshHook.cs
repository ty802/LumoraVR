// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Phos;
using Lumora.Godot.Extensions;
using System.Collections.Generic;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

/// <summary>
/// Hook for SkinnedMeshRenderer component -> Godot MeshInstance3D + Skeleton3D.
/// Creates a deformable mesh that follows a skeleton's bone transforms.
/// Uses bone slot references for proper bone index mapping.
/// </summary>
[ImplementableHook(typeof(SkinnedMeshRenderer))]
public class SkinnedMeshHook : ComponentHook<SkinnedMeshRenderer>
{
    private MeshInstance3D _meshInstance = null!;
    private ArrayMesh _arrayMesh = null!;
    private StandardMaterial3D _material = null!;
    private SkeletonHook _skeletonHook = null!;
    private Skeleton3D _skeleton = null!;
    private bool _meshApplied;
    private bool _skeletonBound;
    // True once the real (component) material asset has been put on the surface. Stays false while we're showing
    // the neutral fallback, so the update loop keeps retrying until the PBS asset finishes loading. -xlinka
    private bool _realMaterialApplied;

    // Async mesh-build state: a worker-thread build is in flight (don't double-dispatch), plus the asset + vertex
    // count we last successfully applied so a lingering dirty flag can't trigger an endless rebuild loop. -xlinka
    private bool _meshBuildInFlight;
    private MeshDataAsset _appliedAsset = null!;
    private int _appliedMeshVcount = -1;

    // Maps mesh bone index -> Godot skeleton bone index
    private Dictionary<int, int> _boneIndexMap = new Dictionary<int, int>();

    public override void Initialize()
    {
        base.Initialize();

        // Create MeshInstance3D for rendering
        _meshInstance = new MeshInstance3D();
        _meshInstance.Name = "SkinnedMeshInstance";

        // Add to scene immediately - will reparent to skeleton later when ready
        attachedNode.AddChild(_meshInstance);
        LumoraLogger.Log($"SkinnedMeshHook: Initialized and added mesh to '{Owner.Slot.SlotName.Value}'");

        // Try to bind to skeleton immediately
        TryBindToSkeleton();

        // Apply mesh if data is available (inline lists or a Phos asset).
        if (Owner.Vertices.Count > 0 || Owner.MeshAsset.Target != null)
        {
            ApplyMesh();
        }
    }

    public override void ApplyChanges()
    {
        if (_meshInstance == null)
            return;

        // Try to bind to skeleton if not yet bound OR if we bound but have no bone mappings
        // (skeleton may have been empty when we first tried)
        bool needsBinding = !_skeletonBound || (_skeletonBound && _boneIndexMap.Count == 0 && Owner.BoneNames.Count > 0);
        if (needsBinding)
        {
            TryBindToSkeleton();
        }

        // Rebuild mesh if data changed or not yet applied (from inline lists or a Phos asset).
        bool shouldApplyMesh = Owner.MeshDataChanged || !_meshApplied;
        if (shouldApplyMesh && (Owner.Vertices.Count > 0 || Owner.MeshAsset.Target != null))
        {
            ApplyMesh();
        }

        // Update skeleton reference if changed
        if (Owner.SkeletonChanged)
        {
            _skeletonBound = false;
            _boneIndexMap.Clear();
            TryBindToSkeleton();
        }

        // Update enabled state
        if (GodotObject.IsInstanceValid(_meshInstance))
        {
            _meshInstance.Visible = Owner.Enabled;
        }

        // Update material if changed (target reassigned), or keep retrying while the real asset is still loading
        // so the neutral fallback gets replaced the moment the PBS material is valid (MAT-3 race fix). -xlinka
        if (Owner.Material.GetWasChangedAndClear())
        {
            _realMaterialApplied = false;
            ApplyMaterial();
        }
        else if (_meshApplied && !_realMaterialApplied)
        {
            ApplyMaterial();
        }

        // Cheap path: blendshape weights changed (blink/viseme/expression) - reapply, no rebuild.
        if (Owner.BlendWeightsChanged)
        {
            ApplyBlendShapeWeights();
            Owner.BlendWeightsChanged = false;
        }
    }

    /// <summary>Push the component's blendshape weights onto the Godot mesh instance (cheap).</summary>
    private void ApplyBlendShapeWeights()
    {
        if (_meshInstance == null || !GodotObject.IsInstanceValid(_meshInstance) || _arrayMesh == null)
            return;

        // Bound by the count the RENDERING SERVER actually allocated for this mesh (mesh_get_blend_shape_count on
        // the RID) - which is exactly what the mesh instance's blend_weights is sized to - NOT the resource-level
        // GetBlendShapeCount(). They disagree when a surface registered blend-shape NAMES but failed to attach the
        // blend-shape DATA: the resource reports 33, the instance has 0. Writing a weight past the real count
        // spams "Index out of bounds" errors, each capturing a managed backtrace - and since animated weights
        // (blink/visemes) re-apply every frame, that flood froze GLB avatar imports for ~10s. -xlinka
        int n = (int)RenderingServer.MeshGetBlendShapeCount(_arrayMesh.GetRid());
        if (n <= 0)
            return;

        int weights = Owner.BlendShapeNames.Count;
        for (int i = 0; i < n; i++)
            _meshInstance.SetBlendShapeValue(i, i < weights ? Owner.GetEffectiveBlendShapeWeight(i) : 0f);
    }

    /// <summary>
    /// Apply the component's material or use default fallback.
    /// </summary>
    private void ApplyMaterial()
    {
        if (_meshInstance == null || _arrayMesh == null) return;

        var materialAsset = Owner.Material.Asset;
        if (materialAsset != null && materialAsset.GodotMaterial is Material godotMaterial)
        {
            _meshInstance.SetSurfaceOverrideMaterial(0, godotMaterial);
            _realMaterialApplied = true;
        }
        else
        {
            // Real material asset isn't loaded yet (it's created lazily once referenced + decoded). Show a neutral
            // fallback but DON'T latch it - leave _realMaterialApplied false so the update loop keeps retrying and
            // swaps in the real material the moment it's valid. Otherwise a textured avatar gets stuck flat-tan. -xlinka
            if (_material == null)
            {
                _material = new StandardMaterial3D();
                _material.AlbedoColor = new Color(0.8f, 0.7f, 0.6f); // Skin-like color
                _material.Roughness = 0.8f;
                _material.Metallic = 0.0f;
                _material.CullMode = BaseMaterial3D.CullModeEnum.Back;
            }
            _meshInstance.SetSurfaceOverrideMaterial(0, _material);
            _realMaterialApplied = false;
        }
    }

    /// <summary>
    /// Try to bind the mesh instance to the skeleton and build bone index map.
    /// </summary>
    private void TryBindToSkeleton()
    {
        // First try to get skeleton from SkeletonBuilder reference
        if (Owner.Skeleton.Target != null)
        {
            _skeletonHook = (Owner.Skeleton.Target.Hook as SkeletonHook)!;
            if (_skeletonHook != null)
            {
                _skeleton = _skeletonHook.GetSkeleton();
            }
        }

        // If no skeleton yet, try to find one via Bones list
        if (_skeleton == null && Owner.Bones.Count > 0 && Owner.Bones[0] != null)
        {
            // Find skeleton by looking for parent Skeleton3D in the scene
            var boneSlot = Owner.Bones[0]!;
            var slotHook = boneSlot.Hook as SlotHook;
            if (slotHook != null)
            {
                var node = slotHook.GeneratedNode3D;
                if (node != null)
                {
                    // Walk up to find Skeleton3D
                    var parent = node.GetParent();
                    while (parent != null)
                    {
                        if (parent is Skeleton3D skel)
                        {
                            _skeleton = skel;
                            break;
                        }
                        parent = parent.GetParent();
                    }
                }
            }
        }

        if (_skeleton == null || !GodotObject.IsInstanceValid(_skeleton))
        {
            if (!_meshInstance.IsInsideTree())
            {
                attachedNode.AddChild(_meshInstance);
                LumoraLogger.Log("SkinnedMeshHook: No skeleton found - added mesh as static");
            }
            return;
        }

        if (_skeleton.GetBoneCount() == 0)
        {
            if (!_meshInstance.IsInsideTree())
            {
                attachedNode.AddChild(_meshInstance);
                LumoraLogger.Log("SkinnedMeshHook: Skeleton has no bones - added mesh as static");
            }
            return;
        }

        // Build bone index mapping from mesh bones to skeleton bones
        BuildBoneIndexMap();

        // Skeleton is ready! Reparent mesh under skeleton if needed
        if (_meshInstance.IsInsideTree())
        {
            if (_meshInstance.GetParent() != _skeleton)
            {
                _meshInstance.GetParent().RemoveChild(_meshInstance);
                _skeleton.AddChild(_meshInstance);
                LumoraLogger.Log("SkinnedMeshHook: Reparented mesh under skeleton");
            }
        }
        else
        {
            _skeleton.AddChild(_meshInstance);
            LumoraLogger.Log("SkinnedMeshHook: Added mesh as child of skeleton");
        }

        // Set the skeleton path - use ".." since mesh is child of skeleton
        _meshInstance.Skeleton = new NodePath("..");

        _skeletonBound = true;
        LumoraLogger.Log($"SkinnedMeshHook: Bound to skeleton '{_skeleton.Name}' with {_skeleton.GetBoneCount()} bones, mapped {_boneIndexMap.Count} mesh bones");

        // Re-apply mesh now that we have skeleton with proper bone mapping / Skin
        if (Owner.Vertices.Count > 0 || Owner.MeshAsset.Target != null)
        {
            _meshApplied = false;
            ApplyMesh();
        }

        // Notify component that binding is complete (stops update polling)
        if (_boneIndexMap.Count > 0)
        {
            Owner.HookBindingComplete = true;
        }
    }

    /// <summary>
    /// Build mapping from mesh bone indices to Godot skeleton bone indices.
    /// This is the key to making skinning work correctly.
    /// </summary>
    private void BuildBoneIndexMap()
    {
        _boneIndexMap.Clear();

        if (_skeleton == null)
            return;

        // Method 1: Use BoneNames list if available
        if (Owner.BoneNames.Count > 0)
        {
            for (int meshBoneIdx = 0; meshBoneIdx < Owner.BoneNames.Count; meshBoneIdx++)
            {
                string boneName = Owner.BoneNames[meshBoneIdx];
                int skelBoneIdx = _skeleton.FindBone(boneName);

                if (skelBoneIdx >= 0)
                {
                    _boneIndexMap[meshBoneIdx] = skelBoneIdx;
                }
                else
                {
                    // Bone not found in skeleton - map to bone 0 as fallback
                    _boneIndexMap[meshBoneIdx] = 0;
                    LumoraLogger.Warn($"SkinnedMeshHook: Bone '{boneName}' not found in skeleton, mapping to bone 0");
                }
            }
            LumoraLogger.Log($"SkinnedMeshHook: Built bone map from names - {_boneIndexMap.Count} mappings");
            return;
        }

        // Method 2: Use Bones slot references if available
        if (Owner.Bones.Count > 0)
        {
            for (int meshBoneIdx = 0; meshBoneIdx < Owner.Bones.Count; meshBoneIdx++)
            {
                var boneSlot = Owner.Bones[meshBoneIdx];
                if (boneSlot != null)
                {
                    string boneName = boneSlot.SlotName.Value;
                    int skelBoneIdx = _skeleton.FindBone(boneName);

                    if (skelBoneIdx >= 0)
                    {
                        _boneIndexMap[meshBoneIdx] = skelBoneIdx;
                    }
                    else
                    {
                        _boneIndexMap[meshBoneIdx] = 0;
                    }
                }
                else
                {
                    _boneIndexMap[meshBoneIdx] = 0;
                }
            }
            LumoraLogger.Log($"SkinnedMeshHook: Built bone map from slots - {_boneIndexMap.Count} mappings");
            return;
        }

        // Method 3: If SkeletonBuilder is available, use its bone order
        if (Owner.Skeleton.Target != null && Owner.Skeleton.Target.BoneNames.Count > 0)
        {
            var skelBuilder = Owner.Skeleton.Target;
            for (int meshBoneIdx = 0; meshBoneIdx < skelBuilder.BoneNames.Count; meshBoneIdx++)
            {
                string boneName = skelBuilder.BoneNames[meshBoneIdx];
                int skelBoneIdx = _skeleton.FindBone(boneName);

                if (skelBoneIdx >= 0)
                {
                    _boneIndexMap[meshBoneIdx] = skelBoneIdx;
                }
            }
            LumoraLogger.Log($"SkinnedMeshHook: Built bone map from SkeletonBuilder - {_boneIndexMap.Count} mappings");
            return;
        }

        // Fallback: Assume direct 1:1 mapping
        for (int i = 0; i < _skeleton.GetBoneCount(); i++)
        {
            _boneIndexMap[i] = i;
        }
        LumoraLogger.Log($"SkinnedMeshHook: Using direct bone index mapping (fallback)");
    }

    /// <summary>
    /// Remap a mesh bone index to the corresponding Godot skeleton bone index.
    /// Returns 0 for invalid indices to prevent Godot errors.
    /// </summary>
    private int RemapBoneIndex(int meshBoneIndex)
    {
        // Handle invalid/unused bone indices (like -1)
        if (meshBoneIndex < 0)
            return 0;

        if (_boneIndexMap.TryGetValue(meshBoneIndex, out int skelBoneIndex))
        {
            // Ensure mapped index is valid for skeleton
            int boneCount = _skeleton?.GetBoneCount() ?? 0;
            if (skelBoneIndex < 0 || skelBoneIndex >= boneCount)
                return 0;
            return skelBoneIndex;
        }

        // Fallback: clamp to valid skeleton bone range
        int maxBone = (_skeleton?.GetBoneCount() ?? 1) - 1;
        return System.Math.Clamp(meshBoneIndex, 0, System.Math.Max(0, maxBone));
    }

    /// <summary>
    /// Build and apply the mesh from component data.
    /// </summary>
    private void ApplyMesh()
    {
        if (_meshInstance == null)
            return;

        // Phos asset path (the universal pipeline): geometry + bone bindings + bind poses come from a
        // content-hashed MeshDataAsset, and skinning is driven by an explicit Skin. Takes precedence over the
        // inline lists (which serve the legacy Godot-glTF import path). -xlinka
        // AssetRef.Asset returns the provider's loaded MeshDataAsset (null until the async decode lands). The
        // AssetRef is what reference-counts the provider so this load actually happens at all. -xlinka
        var phosAsset = Owner.MeshAsset.Asset;
        if (phosAsset?.MeshData is { VertexCount: > 0 } phosMesh)
        {
            ApplyMeshFromAsset(phosMesh, phosAsset);
            return;
        }

        // Check if we have valid mesh data
        if (Owner.Vertices.Count == 0 || Owner.Indices.Count == 0)
        {
            LumoraLogger.Log("SkinnedMeshHook: No mesh data");
            _meshInstance.Mesh = null;
            _meshApplied = false;
            return;
        }

        // Create ArrayMesh
        _arrayMesh?.Dispose();
        _arrayMesh = new ArrayMesh();

        // Build arrays for Godot
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);

        // Vertices
        var vertices = new Vector3[Owner.Vertices.Count];
        for (int i = 0; i < Owner.Vertices.Count; i++)
        {
            var v = Owner.Vertices[i];
            vertices[i] = new Vector3(v.x, v.y, v.z);
        }
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;

        // Normals
        if (Owner.Normals.Count == Owner.Vertices.Count)
        {
            var normals = new Vector3[Owner.Normals.Count];
            for (int i = 0; i < Owner.Normals.Count; i++)
            {
                var n = Owner.Normals[i];
                normals[i] = new Vector3(n.x, n.y, n.z);
            }
            arrays[(int)Mesh.ArrayType.Normal] = normals;
        }

        // UVs
        if (Owner.UVs.Count == Owner.Vertices.Count)
        {
            var uvs = new Vector2[Owner.UVs.Count];
            for (int i = 0; i < Owner.UVs.Count; i++)
            {
                var uv = Owner.UVs[i];
                uvs[i] = new Vector2(uv.x, uv.y);
            }
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        }

        // Indices
        var indices = new int[Owner.Indices.Count];
        int minIndex = int.MaxValue;
        int maxIndex = -1;
        for (int i = 0; i < Owner.Indices.Count; i++)
        {
            int index = Owner.Indices[i];
            if (index > maxIndex) maxIndex = index;
            if (index < minIndex) minIndex = index;
            indices[i] = index;
        }

        // An index outside the vertex buffer is an out-of-bounds read at draw time -
        // that's a Vulkan device-lost, not a visual glitch. Same guard as MeshHook. - xlinka
        if (Owner.Indices.Count > 0 && (maxIndex >= Owner.Vertices.Count || minIndex < 0))
        {
            LumoraLogger.Error($"SkinnedMeshHook: Index range [{minIndex}, {maxIndex}] invalid for vertex count {Owner.Vertices.Count} on slot '{Owner.Slot?.SlotName?.Value}' - surface skipped");
            return;
        }
        arrays[(int)Mesh.ArrayType.Index] = indices;

        // Bone weights and indices for skinning
        // IMPORTANT: Only add bone data if we have valid bone mappings AND skeleton is ready
        // Otherwise Godot will error with invalid bone indices
        bool hasBoneData = Owner.BoneIndices.Count == Owner.Vertices.Count &&
                           Owner.BoneWeights.Count == Owner.Vertices.Count;
        bool hasValidMappings = _boneIndexMap.Count > 0 && _skeleton != null && _skeleton.GetBoneCount() > 0;

        if (hasBoneData && hasValidMappings)
        {
            // Godot expects bone indices as int array (4 per vertex)
            // We need to remap mesh bone indices to Godot skeleton bone indices
            var boneIndices = new int[Owner.Vertices.Count * 4];
            for (int i = 0; i < Owner.BoneIndices.Count; i++)
            {
                var bi = Owner.BoneIndices[i];
                // Remap each bone index to the Godot skeleton's bone index
                boneIndices[i * 4 + 0] = RemapBoneIndex(bi.x);
                boneIndices[i * 4 + 1] = RemapBoneIndex(bi.y);
                boneIndices[i * 4 + 2] = RemapBoneIndex(bi.z);
                boneIndices[i * 4 + 3] = RemapBoneIndex(bi.w);
            }
            arrays[(int)Mesh.ArrayType.Bones] = boneIndices;

            // Godot expects bone weights as float array (4 per vertex)
            var boneWeights = new float[Owner.Vertices.Count * 4];
            for (int i = 0; i < Owner.BoneWeights.Count; i++)
            {
                var bw = Owner.BoneWeights[i];
                boneWeights[i * 4 + 0] = bw.x;
                boneWeights[i * 4 + 1] = bw.y;
                boneWeights[i * 4 + 2] = bw.z;
                boneWeights[i * 4 + 3] = bw.w;
            }
            arrays[(int)Mesh.ArrayType.Weights] = boneWeights;

            LumoraLogger.Log($"SkinnedMeshHook: Added bone data for {Owner.Vertices.Count} vertices with {_boneIndexMap.Count} bone mappings");
        }
        else if (hasBoneData)
        {
            LumoraLogger.Log($"SkinnedMeshHook: Skipping bone data - skeleton not ready (mappings={_boneIndexMap.Count}, skelBones={_skeleton?.GetBoneCount() ?? 0})");
        }
        else
        {
            LumoraLogger.Warn($"SkinnedMeshHook: Missing bone data - indices:{Owner.BoneIndices.Count} weights:{Owner.BoneWeights.Count} verts:{Owner.Vertices.Count}");
        }

        // Blendshapes: declare them on the mesh first, then hand the per-shape vertex arrays to the
        // surface. Stored exactly as the source reported them, with the source's BlendShapeMode, so
        // the morph round-trips correctly. (Only vertex deltas are carried; normals aren't morphed.)
        int vertexCount = Owner.Vertices.Count;
        int shapeCount = Owner.BlendShapeNames.Count;
        bool hasBlendShapes = shapeCount > 0 && Owner.BlendShapeVertices.Count == shapeCount * vertexCount;
        global::Godot.Collections.Array<global::Godot.Collections.Array> blendShapes = null!;
        if (hasBlendShapes)
        {
            _arrayMesh.ClearBlendShapes();
            _arrayMesh.BlendShapeMode = (Mesh.BlendShapeMode)Owner.BlendShapeMode.Value;
            for (int s = 0; s < shapeCount; s++)
                _arrayMesh.AddBlendShape(Owner.BlendShapeNames[s]);

            // Godot 4 requires every blend shape to carry the SAME morphable attributes as the base surface for
            // VERTEX/NORMAL/TANGENT. The base here has NORMAL, so each shape must include a NORMAL array too or
            // the whole surface is rejected (the mesh vanishes). We don't morph normals, so feed values that mean
            // "no change": a zero delta in Relative mode (glTF morph targets are deltas), or the base normal in
            // Normalized mode. -xlinka
            bool baseHasNormals = Owner.Normals.Count == vertexCount;
            bool relativeMode = (Mesh.BlendShapeMode)Owner.BlendShapeMode.Value == Mesh.BlendShapeMode.Relative;
            // Use the source's real NORMAL morph deltas when the import carried them (so lighting follows the
            // expression); otherwise fall back to "no normal morph" values just to satisfy Godot's format
            // requirement. -xlinka
            bool hasMorphNormals = Owner.HasBlendShapeNormals;

            blendShapes = new global::Godot.Collections.Array<global::Godot.Collections.Array>();
            for (int s = 0; s < shapeCount; s++)
            {
                var shapeArrays = new global::Godot.Collections.Array();
                shapeArrays.Resize((int)Mesh.ArrayType.Max);
                var sv = new Vector3[vertexCount];
                int baseIdx = s * vertexCount;
                for (int i = 0; i < vertexCount; i++)
                {
                    var v = Owner.BlendShapeVertices[baseIdx + i];
                    sv[i] = new Vector3(v.x, v.y, v.z);
                }
                shapeArrays[(int)Mesh.ArrayType.Vertex] = sv;

                if (baseHasNormals)
                {
                    var sn = new Vector3[vertexCount];
                    for (int i = 0; i < vertexCount; i++)
                    {
                        if (hasMorphNormals)
                        {
                            var nd = Owner.BlendShapeNormals[baseIdx + i];
                            sn[i] = new Vector3(nd.x, nd.y, nd.z);
                        }
                        else if (relativeMode)
                        {
                            sn[i] = Vector3.Zero;
                        }
                        else
                        {
                            var nrm = Owner.Normals[i];
                            sn[i] = new Vector3(nrm.x, nrm.y, nrm.z);
                        }
                    }
                    shapeArrays[(int)Mesh.ArrayType.Normal] = sn;
                }

                blendShapes.Add(shapeArrays);
            }
        }

        // Add surface to mesh. CRITICAL: if the blend-shape surface add is rejected (Godot 4 wants each blend
        // shape's arrays to match the base surface's VERTEX/NORMAL/TANGENT set, but we only carry vertex deltas),
        // it adds NO surface at all - the whole mesh vanishes (the "main mesh missing" import bug). So detect the
        // drop via the surface count and fall back to a plain surface, so the body always renders even if the
        // morphs can't be attached. -xlinka
        if (hasBlendShapes)
        {
            int beforeSurfaces = _arrayMesh.GetSurfaceCount();
            _arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, blendShapes);
            if (_arrayMesh.GetSurfaceCount() == beforeSurfaces)
            {
                LumoraLogger.Warn($"SkinnedMeshHook: blend-shape surface rejected on slot '{Owner.Slot?.SlotName?.Value}' ({shapeCount} shapes) - rendering without morphs so the mesh still shows.");
                _arrayMesh.ClearBlendShapes();
                _arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            }
        }
        else
        {
            _arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }

        // Set mesh on instance
        _meshInstance.Mesh = _arrayMesh;
        ApplyBlendShapeWeights();

        // Apply material (real asset if loaded, else a non-latching fallback the update loop will replace).
        ApplyMaterial();

        _meshApplied = true;
        LumoraLogger.Log($"SkinnedMeshHook: Applied mesh with {Owner.Vertices.Count} vertices, {Owner.Indices.Count / 3} triangles");
    }

    // Build the Godot skinned mesh straight from the Phos asset: positions/normals/uvs/indices + the raw
    // per-vertex bone indices (kept in MESH-bone space) + weights, then an explicit Skin that maps each mesh
    // bone to its skeleton bone with the asset's bind pose. The Skin is why the mesh holds its shape when the
    // rig poses - without it Godot skins from rest poses and the mesh collapses. Needs the skeleton built first;
    // returns quietly (leaving _meshApplied false) until then so the update loop re-enters. -xlinka
    private void ApplyMeshFromAsset(PhosMesh mesh, MeshDataAsset asset)
    {
        if (_skeleton == null || _skeleton.GetBoneCount() == 0)
        {
            _meshApplied = false;
            return;
        }
        if (mesh.Submeshes.Count == 0 || mesh.Submeshes[0].IndexCount == 0)
        {
            _meshApplied = false;
            return;
        }
        if (_meshBuildInFlight)
            return; // a worker build is already running for this hook
        if (_meshApplied && ReferenceEquals(_appliedAsset, asset) && _appliedMeshVcount == mesh.VertexCount)
            return; // already built this exact mesh - don't rebuild on a lingering dirty flag

        // Build the heavy Godot ArrayMesh (geometry + per-blendshape arrays + AddSurfaceFromArrays) on a WORKER
        // thread so a dense blendshape mesh doesn't freeze the render thread. Godot 4's RenderingServer is
        // thread-safe, so mesh-resource creation off the main thread is supported; only the scene-tree assignment
        // and the Skin (which needs the Skeleton3D node) run back on the main thread via CallDeferred. We stay a
        // single process - this is just a worker thread - so it's fine on Quest/Pico. -xlinka
        _meshBuildInFlight = true;
        System.Threading.Tasks.Task.Run(() =>
        {
            ArrayMesh built = null!;
            int sc = 0;
            try
            {
                built = BuildArrayMeshCore(mesh, out sc);
            }
            catch (System.Exception ex)
            {
                LumoraLogger.Error($"SkinnedMeshHook: off-thread mesh build failed ({ex.Message}); falling back to main thread.");
                built = null!;
            }
            int shapes = sc;
            global::Godot.Callable.From(() => FinalizeMeshFromAsset(built, shapes, mesh, asset)).CallDeferred();
        });
    }

    // PURE mesh-resource build - no scene tree, no Owner/data-model writes - so it is safe on a worker thread.
    // Produces the Godot ArrayMesh from the Phos asset. shapeCount returns the blendshape count actually applied
    // (0 if Godot rejected the morph surface). -xlinka
    private ArrayMesh BuildArrayMeshCore(PhosMesh mesh, out int shapeCount)
    {
        var submesh = mesh.Submeshes[0];
        int vcount = mesh.VertexCount;

        var arrayMesh = new ArrayMesh();

        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);

        var rp = mesh.RawPositions;
        var positions = new Vector3[vcount];
        for (int i = 0; i < vcount; i++)
            positions[i] = new Vector3(rp[i].x, rp[i].y, rp[i].z);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;

        var rn = mesh.RawNormals;
        if (rn != null && rn.Length >= vcount)
        {
            var normals = new Vector3[vcount];
            for (int i = 0; i < vcount; i++)
                normals[i] = new Vector3(rn[i].x, rn[i].y, rn[i].z);
            arrays[(int)Mesh.ArrayType.Normal] = normals;
        }

        var ruv = mesh.RawUV0s;
        if (ruv != null && ruv.Length >= vcount)
        {
            var uvs = new Vector2[vcount];
            for (int i = 0; i < vcount; i++)
                uvs[i] = new Vector2(ruv[i].x, ruv[i].y);
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        }

        // UV1 (lightmap/detail second channel) so it survives the skinned path, not just the static one. -xlinka
        var ruv1 = mesh.RawUV1s;
        if (ruv1 != null && ruv1.Length >= vcount)
        {
            var uvs2 = new Vector2[vcount];
            for (int i = 0; i < vcount; i++)
                uvs2[i] = new Vector2(ruv1[i].x, ruv1[i].y);
            arrays[(int)Mesh.ArrayType.TexUV2] = uvs2;
        }

        // Tangents (4 floats/vertex: xyz + handedness w) for normal maps, and vertex colors. -xlinka
        var rtan = mesh.RawTangents;
        if (mesh.HasTangents && rtan != null && rtan.Length >= vcount)
        {
            var tangents = new float[vcount * 4];
            for (int i = 0; i < vcount; i++)
            {
                var t = rtan[i];
                tangents[i * 4 + 0] = t.x;
                tangents[i * 4 + 1] = t.y;
                tangents[i * 4 + 2] = t.z;
                tangents[i * 4 + 3] = t.w;
            }
            arrays[(int)Mesh.ArrayType.Tangent] = tangents;
        }

        var rcol = mesh.RawColors;
        if (mesh.HasColors && rcol != null && rcol.Length >= vcount)
        {
            var colors = new Color[vcount];
            for (int i = 0; i < vcount; i++)
            {
                var c = rcol[i];
                colors[i] = new Color(c.r, c.g, c.b, c.a);
            }
            arrays[(int)Mesh.ArrayType.Color] = colors;
        }

        if (mesh.HasBoneBindings)
        {
            var bind = mesh.RawBoneBindings;
            var bones = new int[vcount * 4];
            var weights = new float[vcount * 4];
            for (int i = 0; i < vcount; i++)
            {
                var b = bind[i];
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

        var ri = submesh.RawIndices;
        var indices = new int[submesh.IndexCount];
        System.Array.Copy(ri, indices, submesh.IndexCount);
        arrays[(int)Mesh.ArrayType.Index] = indices;

        // Blendshapes (morph targets) from the asset: declare them, then hand per-shape delta arrays to the
        // surface. glTF morphs are deltas -> Relative mode; Godot rejects the surface unless each shape carries
        // the SAME morphable attrs as the base (which has NORMAL), so feed zero normal deltas when absent. -xlinka
        shapeCount = mesh.BlendShapeCount;
        global::Godot.Collections.Array<global::Godot.Collections.Array> blendShapes = null!;
        if (shapeCount > 0)
        {
            arrayMesh.ClearBlendShapes();
            arrayMesh.BlendShapeMode = Mesh.BlendShapeMode.Relative;
            for (int s = 0; s < shapeCount; s++)
                arrayMesh.AddBlendShape(mesh.BlendShapes[s].Name);

            blendShapes = new global::Godot.Collections.Array<global::Godot.Collections.Array>();
            for (int s = 0; s < shapeCount; s++)
            {
                var frame = mesh.BlendShapes[s].Frames.Length > 0 ? mesh.BlendShapes[s].Frames[0] : null;
                var shapeArrays = new global::Godot.Collections.Array();
                shapeArrays.Resize((int)Mesh.ArrayType.Max);

                var sv = new Vector3[vcount];
                var sp = frame?.positions;
                for (int i = 0; i < vcount; i++)
                    sv[i] = (sp != null && i < sp.Length) ? new Vector3(sp[i].x, sp[i].y, sp[i].z) : Vector3.Zero;
                shapeArrays[(int)Mesh.ArrayType.Vertex] = sv;

                var sn = new Vector3[vcount];
                var snd = frame?.normals;
                for (int i = 0; i < vcount; i++)
                    sn[i] = (snd != null && i < snd.Length) ? new Vector3(snd[i].x, snd[i].y, snd[i].z) : Vector3.Zero;
                shapeArrays[(int)Mesh.ArrayType.Normal] = sn;

                // When the base surface carries tangents, Godot requires EVERY blend shape to carry a matching
                // tangent array (4 floats/vert: xyz delta + w) or it rejects the whole morph surface. Feed the
                // morph's tangent deltas (w delta stays 0 - handedness doesn't morph), zero when absent. -xlinka
                if (mesh.HasTangents)
                {
                    var st = new float[vcount * 4];
                    var std = frame?.tangents;
                    for (int i = 0; i < vcount; i++)
                    {
                        if (std != null && i < std.Length)
                        {
                            st[i * 4 + 0] = std[i].x;
                            st[i * 4 + 1] = std[i].y;
                            st[i * 4 + 2] = std[i].z;
                        }
                    }
                    shapeArrays[(int)Mesh.ArrayType.Tangent] = st;
                }

                blendShapes.Add(shapeArrays);
            }
        }

        if (shapeCount > 0)
        {
            int before = arrayMesh.GetSurfaceCount();
            arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, blendShapes);
            if (arrayMesh.GetSurfaceCount() == before)
            {
                LumoraLogger.Warn($"SkinnedMeshHook: Phos blend-shape surface rejected ({shapeCount} shapes) - rendering without morphs so the mesh still shows.");
                arrayMesh.ClearBlendShapes();
                arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                shapeCount = 0;
            }
        }
        else
        {
            arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }

        return arrayMesh;
    }

    // Main-thread finalize: assign the built mesh to the scene node, build the Skin (needs the Skeleton3D), mirror
    // blendshape names, apply material. Falls back to a synchronous on-main build if the worker build threw. -xlinka
    private void FinalizeMeshFromAsset(ArrayMesh built, int shapeCount, PhosMesh mesh, MeshDataAsset asset)
    {
        _meshBuildInFlight = false;

        if (_meshInstance == null || !GodotObject.IsInstanceValid(_meshInstance))
            return; // hook/instance was torn down while the worker built

        // Diagnostic: did the WORKER build succeed, or are we rebuilding on the main thread (= the freeze)? -xlinka
        bool offThreadOk = built != null;
        long _finalizeStart = System.Diagnostics.Stopwatch.GetTimestamp();

        if (built == null)
        {
            try
            {
                built = BuildArrayMeshCore(mesh, out shapeCount);
            }
            catch (System.Exception ex)
            {
                LumoraLogger.Error($"SkinnedMeshHook: main-thread fallback mesh build failed: {ex.Message}");
                return;
            }
        }

        var old = _arrayMesh;
        _arrayMesh = built;
        _meshInstance.Mesh = _arrayMesh;
        old?.Dispose();

        // Mirror the asset's blendshape names onto the component (names only - the deltas stay in the asset) so
        // expression/viseme/blink drivers can find + weight them. One-time. -xlinka
        if (shapeCount > 0 && Owner.BlendShapeNames.Count != shapeCount)
        {
            Owner.BlendShapeNames.Clear();
            Owner.BlendShapeWeights.Clear();
            for (int s = 0; s < shapeCount; s++)
            {
                Owner.BlendShapeNames.Add(mesh.BlendShapes[s].Name);
                Owner.BlendShapeWeights.Add(0f);
            }
        }

        BuildAndAssignSkinFromAsset(asset);
        ApplyBlendShapeWeights();

        // Material: real asset if loaded, else a non-latching fallback the update loop will replace once the PBS
        // asset finishes loading.
        ApplyMaterial();

        _appliedAsset = asset;
        _appliedMeshVcount = mesh.VertexCount;
        _meshApplied = true;
        Owner.HookBindingComplete = true;
        double _finalizeMs = (System.Diagnostics.Stopwatch.GetTimestamp() - _finalizeStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        LumoraLogger.Log($"SkinnedMeshHook: Applied skinned mesh - {mesh.VertexCount} verts, {mesh.BlendShapeCount} shapes, {asset.BoneCount} bones | offThreadBuild={offThreadOk}, mainThreadFinalize={_finalizeMs:F0}ms");
    }

    // Map every mesh bone (in the asset's bone table) to its skeleton bone BY NAME and stamp the asset's bind
    // pose, so the surface's mesh-space bone indices resolve to the right skeleton bone on the GPU. -xlinka
    private void BuildAndAssignSkinFromAsset(MeshDataAsset asset)
    {
        if (_skeleton == null || asset.BoneCount == 0)
            return;

        var skin = new Skin();
        skin.SetBindCount(asset.BoneCount);
        int unresolved = 0;
        for (int i = 0; i < asset.BoneCount; i++)
        {
            string boneName = asset.GetBoneName(i) ?? string.Empty;
            int skelBone = _skeleton.FindBone(boneName);
            if (skelBone < 0)
            {
                // Bone name not in the Godot skeleton -> collapsing to bone 0 yanks its verts to the root (spikes).
                // Usually an FBX pivot ("_$AssimpFbx$") or suffixed-name mismatch. Log instead of failing silently. -xlinka
                unresolved++;
                if (unresolved <= 8)
                    LumoraLogger.Warn($"SkinnedMeshHook: bind bone '{boneName}' not in skeleton - collapsing to bone 0 (will deform wrong).");
                skelBone = 0;
            }
            skin.SetBindBone(i, skelBone);
            skin.SetBindPose(i, asset.GetBoneBindPose(i).ToGodot());
        }
        if (unresolved > 0)
            LumoraLogger.Warn($"SkinnedMeshHook: {unresolved}/{asset.BoneCount} bind bones unresolved against skeleton '{_skeleton.Name}' ({_skeleton.GetBoneCount()} bones).");

        // Skinning canary (one-time per build): at rest, GlobalBoneRest * BindPose must be ~identity (origin ~0,
        // basis det ~1) or that bone's verts deform. Scan ALL bones and flag the bad ones BY NAME - this catches a
        // SUBSET deforming (e.g. ears) while the body is fine, which a first-3-bones spot-check would miss. -xlinka
        int badBones = 0;
        for (int i = 0; i < asset.BoneCount; i++)
        {
            int skelBone = _skeleton.FindBone(asset.GetBoneName(i) ?? string.Empty);
            if (skelBone < 0) continue;
            var check = _skeleton.GetBoneGlobalRest(skelBone) * asset.GetBoneBindPose(i).ToGodot();
            float originErr = check.Origin.Length();
            float detErr = System.Math.Abs(check.Basis.Determinant() - 1f);
            if (originErr > 0.02f || detErr > 0.05f)
            {
                badBones++;
                if (badBones <= 16)
                    LumoraLogger.Warn($"SkinnedMeshHook[skin-check]: BONE '{asset.GetBoneName(i)}' rest*bind NOT identity - originErr={originErr:F3} detErr={detErr:F3} (its verts deform).");
            }
        }
        if (badBones > 0)
            LumoraLogger.Warn($"SkinnedMeshHook[skin-check]: {badBones}/{asset.BoneCount} bones FAIL rest*bind==identity on mesh '{Owner.Slot?.SlotName.Value}'.");
        else
            LumoraLogger.Log($"SkinnedMeshHook[skin-check]: all {asset.BoneCount} bones OK at rest on mesh '{Owner.Slot?.SlotName.Value}'.");

        _meshInstance.Skin = skin;
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (!destroyingWorld && _meshInstance != null && GodotObject.IsInstanceValid(_meshInstance))
        {
            _meshInstance.QueueFree();
        }

        _arrayMesh?.Dispose();
        _material?.Dispose();

        _meshInstance = null!;
        _arrayMesh = null!;
        _material = null!;
        _skeletonHook = null!;
        _skeleton = null!;
        _boneIndexMap.Clear();

        base.Destroy(destroyingWorld);
    }
}

