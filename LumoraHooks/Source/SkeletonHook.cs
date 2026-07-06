// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Components;
using Lumora.Core.Math;
using System.Collections.Generic;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Godot.Hooks;

/// <summary>
/// Hook for SkeletonBuilder component -> Godot Skeleton3D.
/// Creates and manages a Skeleton3D node with bone hierarchy.
/// Platform skeleton hook for Godot.
/// </summary>
[ImplementableHook(typeof(SkeletonBuilder))]
public class SkeletonHook : ComponentHook<SkeletonBuilder>
{
    private Skeleton3D _skeleton = null!;
    private Dictionary<string, int> _boneNameToIndex = new Dictionary<string, int>();
    private Dictionary<int, Transform3D> _boneRestPoses = new Dictionary<int, Transform3D>();
    private Lumora.Core.Slot _rootBoneSlot = null!;

    public override void Initialize()
    {
        base.Initialize();

        // Create Skeleton3D node
        _skeleton = new Skeleton3D();
        _skeleton.Name = "Skeleton";
        attachedNode.AddChild(_skeleton);

        // Build initial skeleton if data is available
        if (Owner.IsBuilt.Value)
        {
            RebuildSkeleton();
        }

        LumoraLogger.Log($"SkeletonHook: Initialized for slot '{Owner.Slot.SlotName.Value}'");
    }

    public override void ApplyChanges()
    {
        if (_skeleton == null || !GodotObject.IsInstanceValid(_skeleton))
            return;

        // Rebuild skeleton if:
        // 1. Hierarchy changed flag is set, OR
        // 2. Lumora has bones but Godot skeleton doesn't (late initialization case)
        bool needsRebuild = Owner.BoneHierarchyChanged ||
            (Owner.IsBuilt.Value && Owner.BoneCount > 0 && _skeleton.GetBoneCount() == 0);

        if (needsRebuild && Owner.IsBuilt.Value)
        {
            LumoraLogger.Log($"SkeletonHook: Triggering rebuild - hierarchyChanged={Owner.BoneHierarchyChanged}, lumoraBones={Owner.BoneCount}, godotBones={_skeleton.GetBoneCount()}");
            RebuildSkeleton();
        }

        // Update bone transforms from slots
        UpdateBoneTransforms();

        // Update enabled state
        bool enabled = Owner.Enabled;
        if (_skeleton.Visible != enabled)
        {
            _skeleton.Visible = enabled;
        }
    }

    /// <summary>
    /// Rebuild the entire Skeleton3D from component data.
    /// </summary>
    private void RebuildSkeleton()
    {
        if (_skeleton == null)
            return;

        LumoraLogger.Log($"SkeletonHook: Rebuilding skeleton with {Owner.BoneCount} bones");

        // Clear existing bones (Godot doesn't have RemoveBone, so recreate skeleton)
        if (_skeleton.GetBoneCount() > 0)
        {
            // Remove and recreate skeleton node
            _skeleton.QueueFree();
            _skeleton = new Skeleton3D();
            _skeleton.Name = "Skeleton";
            attachedNode.AddChild(_skeleton);
        }

        _boneNameToIndex.Clear();
        _boneRestPoses.Clear();

        // Build parent hierarchy map
        var parentMap = BuildParentMap();

        // Godot requires a bone's parent to have a LOWER index, and SetBoneParent silently NO-OPS if the parent
        // hasn't been added yet. Assimp's bone list is NOT hierarchically ordered, so adding in raw order orphaned
        // any bone whose parent came later (e.g. Ear2/Ear3) to the skeleton ROOT - its global rest then lost the
        // ancestor translation (originErr ~2.57, basis fine) and its verts flew off as shards. Add bones in
        // TOPOLOGICAL order (every bone AFTER its parent bone) so parenting always resolves. -xlinka
        var slotToBoneIdx = new Dictionary<Lumora.Core.Slot, int>();
        for (int i = 0; i < Owner.BoneCount; i++)
        {
            var s = Owner.BoneSlots[i];
            if (s != null) slotToBoneIdx[s] = i;
        }
        var addOrder = new List<int>(Owner.BoneCount);
        var ordered = new HashSet<int>();
        void AddParentFirst(int i)
        {
            if (i < 0 || !ordered.Add(i)) return;
            var s = Owner.BoneSlots[i];
            if (s != null && parentMap.TryGetValue(s, out var ps) && slotToBoneIdx.TryGetValue(ps, out int pIdx))
                AddParentFirst(pIdx); // ensure the parent bone is added before this one
            addOrder.Add(i);
        }
        for (int i = 0; i < Owner.BoneCount; i++)
            AddParentFirst(i);

        // Add bones to the Godot skeleton in topological (parent-first) order.
        Lumora.Core.Slot rootBoneSlot = null!;
        foreach (int i in addOrder)
        {
            string boneName = Owner.BoneNames[i];
            var boneSlot = Owner.BoneSlots[i];

            if (boneSlot == null)
            {
                LumoraLogger.Warn($"SkeletonHook: Bone '{boneName}' has null slot, skipping");
                continue;
            }

            // Add bone to Godot skeleton
            int boneIndex = _skeleton.AddBone(boneName);
            _boneNameToIndex[boneName] = boneIndex;

            // Set parent bone (parent is guaranteed added already by the topological order above)
            bool parentedToBone = false;
            if (parentMap.TryGetValue(boneSlot, out var parentSlot))
            {
                string parentName = parentSlot.SlotName.Value;
                if (_boneNameToIndex.TryGetValue(parentName, out int parentIndex))
                {
                    _skeleton.SetBoneParent(boneIndex, parentIndex);
                    parentedToBone = true;
                }
                else
                {
                    LumoraLogger.Warn($"SkeletonHook: bone '{boneName}' parent '{parentName}' not added yet - topological order failed.");
                }
            }
            // First skeleton-root bone (no PARENT BONE): remember its slot so we can re-introduce the transform of
            // any non-bone nodes between the model root and it (the orientation offset below).
            if (!parentedToBone)
                rootBoneSlot ??= boneSlot;

            // Set rest pose
            Transform3D restPose;
            if (i < Owner.RestPoseTransforms.Count)
            {
                restPose = ConvertToGodotTransform(Owner.RestPoseTransforms[i]);
            }
            else
            {
                // Use slot's local transform as fallback
                restPose = ConvertSlotLocalTransform(boneSlot);
            }

            _skeleton.SetBoneRest(boneIndex, restPose);
            _boneRestPoses[boneIndex] = restPose;
        }
        // Re-introduce dropped non-bone-node transforms (model-orientation fix). Bone rests are stored slot-local,
        // so any NON-bone node between the model root and the first bone - e.g. an FBX "Armature"/"RootNode" that
        // carries the up-axis (Z-up -> Y-up) rotation - is dropped from the skeleton, leaving the whole rig
        // mis-oriented (it imported lying on its side). The bone SLOTS still carry that transform (so IK + bone
        // visuals stay correct); only the Skeleton3D lost it. Offset the Skeleton3D node by the root bone's
        // slot-parent transform relative to the model root - without touching any bone rest or bind pose
        // (skinning math unchanged). -xlinka
        _rootBoneSlot = rootBoneSlot;
        RefreshOrientationOffset();
        LumoraLogger.Log($"SkeletonHook: rebuilt with {_skeleton.GetBoneCount()} bones, orientation offset origin={_skeleton.Transform.Origin} det={_skeleton.Transform.Basis.Determinant():F2}");
    }

    // The prefix nodes are LIVE transforms, not import-time constants: the forward normalization yaws the
    // Armature after the skeleton is built, and the studio can insert a wrapper above the hips. A baked
    // offset goes stale then and the skin renders somewhere the bones aren't (the engine-side bones,
    // markers and IK all follow the slots). Re-derive it every ApplyChanges; the parent is re-read from
    // the root bone each time so reparenting is picked up too. -xlinka
    private void RefreshOrientationOffset()
    {
        if (_skeleton == null || !GodotObject.IsInstanceValid(_skeleton))
            return;

        var offset = Transform3D.Identity;
        var source = _rootBoneSlot != null && !_rootBoneSlot.IsDestroyed ? _rootBoneSlot.Parent : null;
        if (source != null && !source.IsDestroyed && Owner.Slot != null && source != Owner.Slot)
            offset = SlotGlobalTransform3D(Owner.Slot).AffineInverse() * SlotGlobalTransform3D(source);

        if (!_skeleton.Transform.IsEqualApprox(offset))
            _skeleton.Transform = offset;
    }

    /// <summary>
    /// Push the bone SLOT transforms onto the Godot Skeleton3D so the skinned mesh deforms. One-way: the
    /// engine owns the bones. Our own IK (AvatarIK / FullBodyIKSolver) and animation write the bone slots;
    /// the Skeleton3D is purely a skinning target. We do NOT read poses back from Godot - Godot runs no IK
    /// here, so a read-back would just round-trip our own values (adding drift and wasted work) and let the
    /// platform fight engine authority. -xlinka
    /// </summary>
    private void UpdateBoneTransforms()
    {
        if (_skeleton == null || Owner.BoneCount == 0)
            return;

        RefreshOrientationOffset();
        SyncSlotsToSkeleton();
    }

    /// <summary>
    /// Copy each bone slot's local transform onto its Skeleton3D bone pose, so the GPU skinning follows the
    /// engine-driven bones (IK, animation, manual posing alike).
    /// </summary>
    private void SyncSlotsToSkeleton()
    {
        for (int i = 0; i < Owner.BoneCount; i++)
        {
            var boneSlot = Owner.BoneSlots[i];
            if (boneSlot == null)
                continue;

            string boneName = Owner.BoneNames[i];
            if (!_boneNameToIndex.TryGetValue(boneName, out int boneIndex))
                continue;

            // Skip bones that are being driven by IK
            // TODO: Add a way to mark bones as IK-driven to skip this step

            // Convert slot local transform to Godot Transform3D
            Transform3D boneTransform = ConvertSlotLocalTransform(boneSlot);

            // Set bone pose (relative to parent)
            _skeleton.SetBonePose(boneIndex, boneTransform);
        }
    }

    /// <summary>
    /// Build a map of child slots to their parents.
    /// </summary>
    private Dictionary<Lumora.Core.Slot, Lumora.Core.Slot> BuildParentMap()
    {
        var parentMap = new Dictionary<Lumora.Core.Slot, Lumora.Core.Slot>();

        for (int i = 0; i < Owner.BoneCount; i++)
        {
            var boneSlot = Owner.BoneSlots[i];
            if (boneSlot == null)
                continue;

            // Find parent slot that is also a bone
            var parentSlot = boneSlot.Parent;
            while (parentSlot != null)
            {
                if (Owner.BoneSlots.Contains(parentSlot))
                {
                    parentMap[boneSlot] = parentSlot;
                    break;
                }
                parentSlot = parentSlot.Parent;
            }
        }

        return parentMap;
    }

    /// <summary>
    /// Convert Lumora float4x4 to Godot Transform3D.
    /// </summary>
    private Transform3D ConvertToGodotTransform(float4x4 matrix)
    {
        // float4x4 uses column-major storage: c0, c1, c2, c3
        // Each column is a float4 (x, y, z, w)

        // Extract translation from last column (c3)
        Vector3 origin = new Vector3(matrix.c3.x, matrix.c3.y, matrix.c3.z);

        // Extract basis (rotation + scale) from first 3 columns
        Basis basis = new Basis(
            new Vector3(matrix.c0.x, matrix.c0.y, matrix.c0.z), // Column 0
            new Vector3(matrix.c1.x, matrix.c1.y, matrix.c1.z), // Column 1
            new Vector3(matrix.c2.x, matrix.c2.y, matrix.c2.z)  // Column 2
        );

        return new Transform3D(basis, origin);
    }

    /// <summary>
    /// Convert Lumora slot local transform to Godot Transform3D.
    /// </summary>
    private Transform3D ConvertSlotLocalTransform(Lumora.Core.Slot slot)
    {
        if (slot == null)
            return Transform3D.Identity;

        // Get local transform components
        var position = slot.LocalPosition.Value;
        var rotation = slot.LocalRotation.Value;
        var scale = slot.LocalScale.Value;

        // Convert to Godot types
        Vector3 godotPosition = new Vector3(position.x, position.y, position.z);
        Quaternion godotRotation = new Quaternion(rotation.x, rotation.y, rotation.z, rotation.w);
        Vector3 godotScale = new Vector3(scale.x, scale.y, scale.z);

        // Build transform
        Transform3D transform = Transform3D.Identity;
        transform.Basis = new Basis(godotRotation).Scaled(godotScale);
        transform.Origin = godotPosition;

        return transform;
    }

    /// <summary>
    /// A slot's global transform as a Godot Transform3D (position + rotation + scale). Used to compute the
    /// skeleton's orientation offset relative to the model root. -xlinka
    /// </summary>
    private static Transform3D SlotGlobalTransform3D(Lumora.Core.Slot slot)
    {
        var p = slot.GlobalPosition;
        var r = slot.GlobalRotation;
        var s = slot.GlobalScale;
        var basis = new Basis(new Quaternion(r.x, r.y, r.z, r.w)).Scaled(new Vector3(s.x, s.y, s.z));
        return new Transform3D(basis, new Vector3(p.x, p.y, p.z));
    }

    /// <summary>
    /// Get the Godot Skeleton3D instance.
    /// Used by SkinnedMeshHook to attach meshes.
    /// </summary>
    public Skeleton3D GetSkeleton()
    {
        return _skeleton;
    }

    /// <summary>
    /// Get the bone index for a bone name.
    /// Returns -1 if bone not found.
    /// </summary>
    public int GetBoneIndex(string boneName)
    {
        if (_boneNameToIndex.TryGetValue(boneName, out int index))
            return index;
        return -1;
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (!destroyingWorld && _skeleton != null && GodotObject.IsInstanceValid(_skeleton))
        {
            _skeleton.QueueFree();
        }

        _rootBoneSlot = null!;
        _skeleton = null!;
        _boneNameToIndex.Clear();
        _boneRestPoses.Clear();

        base.Destroy(destroyingWorld);
    }
}

