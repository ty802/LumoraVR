using System;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Input;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Creates a default humanoid avatar with skeleton and IK for testing.
/// This is a simple procedural biped avatar.
/// </summary>
public static class DefaultAVI
{
    // Avatar dimensions
    public const float DEFAULT_HEIGHT = 1.7f;
    public const float HEAD_SIZE = 0.2f;
    public const float TORSO_HEIGHT = 0.5f;
    public const float ARM_LENGTH = 0.55f;
    public const float LEG_LENGTH = 0.85f;
    public const float HAND_SIZE = 0.08f;
    public const float FOOT_SIZE = 0.1f;

    /// <summary>
    /// Create a default avatar on the given slot.
    /// </summary>
    public static void CreateDefaultAvatar(Slot avatarRoot, UserRoot userRoot)
    {
        if (avatarRoot == null)
        {
            AquaLogger.Error("DefaultAVI: Cannot create avatar - avatarRoot is null");
            return;
        }

        AquaLogger.Log($"DefaultAVI: Creating default avatar on '{avatarRoot.SlotName.Value}'");

        // Create skeleton structure
        var skeleton = CreateSkeleton(avatarRoot);

        // Create visual meshes
        CreateVisuals(skeleton);

        // Setup IK
        SetupIK(avatarRoot, skeleton, userRoot);

        AquaLogger.Log("DefaultAVI: Default avatar created successfully");
    }

    /// <summary>
    /// Create skeleton hierarchy.
    /// </summary>
    private static SkeletonBuilder CreateSkeleton(Slot avatarRoot)
    {
        var skeletonSlot = avatarRoot.AddSlot("Skeleton");
        var skeleton = skeletonSlot.AttachComponent<SkeletonBuilder>();

        // Root bone
        var rootBone = skeletonSlot.AddSlot("Root");

        // Hips (pelvis)
        var hips = rootBone.AddSlot("Hips");
        hips.LocalPosition.Value = new float3(0, LEG_LENGTH, 0);

        // Spine chain
        var spine = hips.AddSlot("Spine");
        spine.LocalPosition.Value = new float3(0, 0.15f, 0);

        var chest = spine.AddSlot("Chest");
        chest.LocalPosition.Value = new float3(0, 0.15f, 0);

        var upperChest = chest.AddSlot("UpperChest");
        upperChest.LocalPosition.Value = new float3(0, 0.1f, 0);

        var neck = upperChest.AddSlot("Neck");
        neck.LocalPosition.Value = new float3(0, 0.1f, 0);

        var head = neck.AddSlot("Head");
        head.LocalPosition.Value = new float3(0, 0.1f, 0);

        // Left arm
        var leftShoulder = upperChest.AddSlot("LeftShoulder");
        leftShoulder.LocalPosition.Value = new float3(-0.1f, 0.05f, 0);

        var leftUpperArm = leftShoulder.AddSlot("LeftUpperArm");
        leftUpperArm.LocalPosition.Value = new float3(-0.1f, 0, 0);

        var leftLowerArm = leftUpperArm.AddSlot("LeftLowerArm");
        leftLowerArm.LocalPosition.Value = new float3(-0.25f, 0, 0);

        var leftHand = leftLowerArm.AddSlot("LeftHand");
        leftHand.LocalPosition.Value = new float3(-0.25f, 0, 0);

        // Right arm
        var rightShoulder = upperChest.AddSlot("RightShoulder");
        rightShoulder.LocalPosition.Value = new float3(0.1f, 0.05f, 0);

        var rightUpperArm = rightShoulder.AddSlot("RightUpperArm");
        rightUpperArm.LocalPosition.Value = new float3(0.1f, 0, 0);

        var rightLowerArm = rightUpperArm.AddSlot("RightLowerArm");
        rightLowerArm.LocalPosition.Value = new float3(0.25f, 0, 0);

        var rightHand = rightLowerArm.AddSlot("RightHand");
        rightHand.LocalPosition.Value = new float3(0.25f, 0, 0);

        // Left leg
        var leftUpperLeg = hips.AddSlot("LeftUpperLeg");
        leftUpperLeg.LocalPosition.Value = new float3(-0.1f, 0, 0);

        var leftLowerLeg = leftUpperLeg.AddSlot("LeftLowerLeg");
        leftLowerLeg.LocalPosition.Value = new float3(0, -0.45f, 0);

        var leftFoot = leftLowerLeg.AddSlot("LeftFoot");
        leftFoot.LocalPosition.Value = new float3(0, -0.4f, 0);

        var leftToes = leftFoot.AddSlot("LeftToes");
        leftToes.LocalPosition.Value = new float3(0, 0, 0.1f);

        // Right leg
        var rightUpperLeg = hips.AddSlot("RightUpperLeg");
        rightUpperLeg.LocalPosition.Value = new float3(0.1f, 0, 0);

        var rightLowerLeg = rightUpperLeg.AddSlot("RightLowerLeg");
        rightLowerLeg.LocalPosition.Value = new float3(0, -0.45f, 0);

        var rightFoot = rightLowerLeg.AddSlot("RightFoot");
        rightFoot.LocalPosition.Value = new float3(0, -0.4f, 0);

        var rightToes = rightFoot.AddSlot("RightToes");
        rightToes.LocalPosition.Value = new float3(0, 0, 0.1f);

        // Register bones with skeleton
        skeleton.RootBone.Target = rootBone;

        // Add all bones to the skeleton
        AddBoneToSkeleton(skeleton, "Root", rootBone);
        AddBoneToSkeleton(skeleton, "Hips", hips);
        AddBoneToSkeleton(skeleton, "Spine", spine);
        AddBoneToSkeleton(skeleton, "Chest", chest);
        AddBoneToSkeleton(skeleton, "UpperChest", upperChest);
        AddBoneToSkeleton(skeleton, "Neck", neck);
        AddBoneToSkeleton(skeleton, "Head", head);

        AddBoneToSkeleton(skeleton, "LeftShoulder", leftShoulder);
        AddBoneToSkeleton(skeleton, "LeftUpperArm", leftUpperArm);
        AddBoneToSkeleton(skeleton, "LeftLowerArm", leftLowerArm);
        AddBoneToSkeleton(skeleton, "LeftHand", leftHand);

        AddBoneToSkeleton(skeleton, "RightShoulder", rightShoulder);
        AddBoneToSkeleton(skeleton, "RightUpperArm", rightUpperArm);
        AddBoneToSkeleton(skeleton, "RightLowerArm", rightLowerArm);
        AddBoneToSkeleton(skeleton, "RightHand", rightHand);

        AddBoneToSkeleton(skeleton, "LeftUpperLeg", leftUpperLeg);
        AddBoneToSkeleton(skeleton, "LeftLowerLeg", leftLowerLeg);
        AddBoneToSkeleton(skeleton, "LeftFoot", leftFoot);
        AddBoneToSkeleton(skeleton, "LeftToes", leftToes);

        AddBoneToSkeleton(skeleton, "RightUpperLeg", rightUpperLeg);
        AddBoneToSkeleton(skeleton, "RightLowerLeg", rightLowerLeg);
        AddBoneToSkeleton(skeleton, "RightFoot", rightFoot);
        AddBoneToSkeleton(skeleton, "RightToes", rightToes);

        // Mark skeleton as built
        skeleton.IsBuilt.Value = true;

        AquaLogger.Log($"DefaultAVI: Created skeleton with {skeleton.BoneSlots.Count} bones");
        return skeleton;
    }

    private static void AddBoneToSkeleton(SkeletonBuilder skeleton, string boneName, Slot boneSlot)
    {
        skeleton.BoneNames.Add(boneName);
        skeleton.BoneSlots.Add(boneSlot);
    }

    /// <summary>
    /// Create visual mesh for the avatar using a SkinnedMeshRenderer.
    /// The mesh is skinned to the skeleton bones for proper deformation.
    /// </summary>
    private static void CreateVisuals(SkeletonBuilder skeleton)
    {
        // Create a slot for the skinned mesh on the skeleton slot
        var meshSlot = skeleton.Slot.AddSlot("SkinnedMesh");

        // Create the SkinnedMeshRenderer component
        var skinnedRenderer = meshSlot.AttachComponent<SkinnedMeshRenderer>();

        // Link to the skeleton
        skinnedRenderer.Skeleton.Target = skeleton;

        // Generate humanoid mesh with proper bone weights
        HumanoidMeshGenerator.GenerateDefaultHumanoidMesh(
            out float3[] vertices,
            out float3[] normals,
            out float2[] uvs,
            out int[] indices,
            out int4[] boneIndices,
            out float4[] boneWeights);

        // Set the mesh data on the renderer
        skinnedRenderer.SetMeshData(vertices, normals, uvs, indices, boneIndices, boneWeights);

        AquaLogger.Log($"DefaultAVI: Created skinned mesh with {vertices.Length} vertices bound to skeleton");
    }

    /// <summary>
    /// Setup IK on the avatar.
    /// </summary>
    private static void SetupIK(Slot avatarRoot, SkeletonBuilder skeleton, UserRoot userRoot)
    {
        var ikSlot = avatarRoot.AddSlot("IK");

        // Use GodotIKAvatar - uses libik addon for IK
        var ikAvatar = ikSlot.AttachComponent<GodotIKAvatar>();

        // Setup skeleton reference
        ikAvatar.Skeleton.Target = skeleton;

        // Setup tracking targets
        if (userRoot != null)
        {
            ikAvatar.SetupTracking(userRoot);
        }

        // Add procedural legs for walking animation
        var proceduralLegs = ikSlot.AttachComponent<ProceduralLegs>();

        AquaLogger.Log("DefaultAVI: IK setup complete with GodotIKAvatar");
    }

    /// <summary>
    /// Create a complete user with default avatar.
    /// Called by SimpleUserSpawn or other spawn systems.
    /// </summary>
    public static void SpawnWithDefaultAvatar(Slot userSlot, User user)
    {
        if (userSlot == null || user == null)
        {
            AquaLogger.Error("DefaultAVI: Cannot spawn - userSlot or user is null");
            return;
        }

        // Create UserRoot
        var userRoot = userSlot.AttachComponent<UserRoot>();
        userRoot.Initialize(user);

        // Create body tracking slots
        var bodyNodes = userSlot.AddSlot("Body Nodes");

        // Create Head slot - UserRoot auto-resolves from "Body Nodes/Head"
        var headSlot = bodyNodes.AddSlot("Head");
        headSlot.LocalPosition.Value = new float3(0, DEFAULT_HEIGHT - 0.1f, 0);
        var headPositioner = headSlot.AttachComponent<TrackedDevicePositioner>();
        headPositioner.AutoBodyNode.Value = BodyNode.Head;

        // Create LeftHand slot - UserRoot auto-resolves from "Body Nodes/LeftHand"
        // Default position: at rest by the side (not T-pose)
        var leftHandSlot = bodyNodes.AddSlot("LeftHand");
        leftHandSlot.LocalPosition.Value = new float3(-0.2f, 0.9f, 0.1f); // By left hip, slightly forward
        var leftHandPositioner = leftHandSlot.AttachComponent<TrackedDevicePositioner>();
        leftHandPositioner.AutoBodyNode.Value = BodyNode.LeftController;

        // Create RightHand slot - UserRoot auto-resolves from "Body Nodes/RightHand"
        // Default position: at rest by the side (not T-pose)
        var rightHandSlot = bodyNodes.AddSlot("RightHand");
        rightHandSlot.LocalPosition.Value = new float3(0.2f, 0.9f, 0.1f); // By right hip, slightly forward
        var rightHandPositioner = rightHandSlot.AttachComponent<TrackedDevicePositioner>();
        rightHandPositioner.AutoBodyNode.Value = BodyNode.RightController;

        // Create foot tracking slots for procedural walking
        var leftFootSlot = bodyNodes.AddSlot("LeftFoot");
        leftFootSlot.LocalPosition.Value = new float3(-0.15f, 0, 0);

        var rightFootSlot = bodyNodes.AddSlot("RightFoot");
        rightFootSlot.LocalPosition.Value = new float3(0.15f, 0, 0);

        // Physics
        var collider = userSlot.AttachComponent<CapsuleCollider>();
        collider.Type.Value = Physics.ColliderType.CharacterController;
        collider.Height.Value = 1.8f;
        collider.Radius.Value = 0.3f;
        collider.Offset.Value = new float3(0, 0.9f, 0);

        userSlot.AttachComponent<CharacterController>();
        userSlot.AttachComponent<LocomotionController>();

        // Head output
        userSlot.AttachComponent<HeadOutput>();

        // Create avatar
        var avatarSlot = userSlot.AddSlot("Avatar");
        CreateDefaultAvatar(avatarSlot, userRoot);

        // Create nameplate
        var nameplateSlot = headSlot.AddSlot("Nameplate");
        nameplateSlot.LocalPosition.Value = new float3(0, 0.25f, 0);
        var nameplate = nameplateSlot.AttachComponent<Nameplate>();
        nameplate.Initialize(user);

        AquaLogger.Log($"DefaultAVI: Spawned user '{user.UserName.Value}' with default avatar");
    }
}
