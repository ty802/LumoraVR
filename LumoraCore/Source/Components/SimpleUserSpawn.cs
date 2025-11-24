using System;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components;

/// <summary>
/// Spawns users at this slot's position when they join the world.
/// Creates UserRoot hierarchy and avatar for each user.
/// </summary>
[ComponentCategory("Users")]
public class SimpleUserSpawn : Component, IWorldEventReceiver
{
	/// <summary>
	/// Called when the component is first created.
	/// Register for world events to receive user join/leave notifications.
	/// </summary>
	public override void OnAwake()
	{
		base.OnAwake();
		World?.RegisterEventReceiver(this);
		AquaLogger.Log($"SimpleUserSpawn: Registered for world events on slot '{Slot.SlotName.Value}'");
	}

	/// <summary>
	/// Called when the component is destroyed.
	/// Unregister from world events.
	/// </summary>
	public override void OnDestroy()
	{
		World?.UnregisterEventReceiver(this);
		base.OnDestroy();
	}

	// ===== IWorldEventReceiver Implementation =====

	/// <summary>
	/// Check if this component handles a specific world event type.
	/// </summary>
	public bool HasEventHandler(World.WorldEvent eventType)
	{
		return eventType == World.WorldEvent.OnUserJoined ||
		       eventType == World.WorldEvent.OnUserLeft;
	}

	/// <summary>
	/// Called when a user joins the world.
	/// Creates UserRoot hierarchy and spawns avatar.
	/// </summary>
	public void OnUserJoined(User user)
	{
		if (user == null)
		{
			AquaLogger.Warn("SimpleUserSpawn: User is null in OnUserJoined");
			return;
		}

		AquaLogger.Log($"SimpleUserSpawn: Spawning user '{user.UserName.Value}' (RefID: {user.ReferenceID})");

		try
		{
			// Create user slot under world root
			var userSlot = World.RootSlot.AddSlot($"User_{user.UserName.Value}_{user.ReferenceID}");
			userSlot.Persistent.Value = false; // Don't save user slots

			// Position at spawn point (this slot's transform)
			userSlot.LocalPosition.Value = Slot.LocalPosition.Value;
			userSlot.LocalRotation.Value = Slot.LocalRotation.Value;
			userSlot.LocalScale.Value = float3.One;

			// Attach UserRoot component
			var userRoot = userSlot.AttachComponent<UserRoot>();
			userRoot.Initialize(user);

			// Attach CapsuleCollider for character collision
			var collider = userSlot.AttachComponent<CapsuleCollider>();
			collider.Type.Value = ColliderType.CharacterController; // CRITICAL: Mark as character controller collider
			collider.Height.Value = 1.8f;
			collider.Radius.Value = 0.3f;
			collider.Offset.Value = new float3(0, 0.9f, 0); // Offset to center
			collider.Mass.Value = 10f; // Character mass

			// Attach CharacterController (physics-based movement + IColliderOwner)
			var characterController = userSlot.AttachComponent<CharacterController>();

			// Attach LocomotionController (input handling + movement logic)
			var locomotionController = userSlot.AttachComponent<LocomotionController>();

			// Attach HeadOutput (camera follow)
			var headOutput = userSlot.AttachComponent<HeadOutput>();

			// Link UserRoot to User
			user.UserRootSlot = userSlot;

			// Build avatar (simple capsule for now)
			BuildDefaultAvatar(userRoot);

			AquaLogger.Log($"SimpleUserSpawn: Successfully spawned user '{user.UserName.Value}' at {Slot.LocalPosition.Value}");
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"SimpleUserSpawn: Failed to spawn user '{user.UserName.Value}': {ex.Message}");
		}
	}

	/// <summary>
	/// Called when a user leaves the world.
	/// Destroys the user's UserRoot slot and all its children.
	/// </summary>
	public void OnUserLeft(User user)
	{
		if (user == null || user.UserRootSlot == null)
			return;

		AquaLogger.Log($"SimpleUserSpawn: User '{user.UserName.Value}' left - destroying UserRoot");

		try
		{
			// Destroy the user's slot hierarchy
			user.UserRootSlot.Destroy();
			user.UserRootSlot = null;
		}
		catch (Exception ex)
		{
			AquaLogger.Error($"SimpleUserSpawn: Failed to destroy UserRoot for '{user.UserName.Value}': {ex.Message}");
		}
	}

	/// <summary>
	/// Build a default avatar with proper anatomical hierarchy and skinned mesh.
	/// Uses SkeletonBuilder and SkinnedMeshRenderer for deformable character body.
	/// Hierarchy: Hips → Spine → Chest → Neck → Head
	///            Chest → Shoulders → UpperArm → LowerArm → Hand
	///            Hips → UpperLeg → LowerLeg → Foot
	/// </summary>
	private void BuildDefaultAvatar(UserRoot userRoot)
	{
		if (userRoot == null || userRoot.Slot == null)
		{
			AquaLogger.Warn("SimpleUserSpawn: Cannot build avatar - UserRoot or Slot is null");
			return;
		}

		var avatarSlot = userRoot.Slot.AddSlot("Avatar");

		// Create skeleton root slot
		var skeletonRootSlot = avatarSlot.AddSlot("Skeleton");

		// Build anatomically correct skeleton hierarchy (Lumora pattern)
		// Note: Using smaller scale factor to match mesh coordinates

		// 1. Hips (pelvis) - root of body
		var hips = CreateBoneNode(skeletonRootSlot, "Hips", new float3(0, 0.95f, 0));

		// 2. Spine → Chest hierarchy
		var spine = CreateBoneNode(hips, "Spine", new float3(0, 0.15f, 0));
		var chest = CreateBoneNode(spine, "Chest", new float3(0, 0.2f, 0));
		var neck = CreateBoneNode(chest, "Neck", new float3(0, 0.2f, 0));
		var head = CreateBoneNode(neck, "Head", new float3(0, 0.15f, 0));

		// 3. Left arm chain: Chest → Shoulder → UpperArm → LowerArm → Hand
		var leftShoulder = CreateBoneNode(chest, "LeftShoulder", new float3(-0.15f, 0.1f, 0));
		var leftUpperArm = CreateBoneNode(leftShoulder, "LeftUpperArm", new float3(-0.1f, -0.15f, 0));
		var leftLowerArm = CreateBoneNode(leftUpperArm, "LeftLowerArm", new float3(-0.1f, -0.2f, 0));
		var leftHand = CreateBoneNode(leftLowerArm, "LeftHand", new float3(-0.05f, -0.1f, 0));

		// Store elbow reference (mid-point of arm)
		var leftElbow = leftLowerArm;

		// 4. Right arm chain: Chest → Shoulder → UpperArm → LowerArm → Hand
		var rightShoulder = CreateBoneNode(chest, "RightShoulder", new float3(0.15f, 0.1f, 0));
		var rightUpperArm = CreateBoneNode(rightShoulder, "RightUpperArm", new float3(0.1f, -0.15f, 0));
		var rightLowerArm = CreateBoneNode(rightUpperArm, "RightLowerArm", new float3(0.1f, -0.2f, 0));
		var rightHand = CreateBoneNode(rightLowerArm, "RightHand", new float3(0.05f, -0.1f, 0));

		// Store elbow reference (mid-point of arm)
		var rightElbow = rightLowerArm;

		// 5. Left leg chain: Hips → UpperLeg → LowerLeg → Foot
		var leftUpperLeg = CreateBoneNode(hips, "LeftUpperLeg", new float3(-0.1f, -0.05f, 0));
		var leftLowerLeg = CreateBoneNode(leftUpperLeg, "LeftLowerLeg", new float3(-0.02f, -0.4f, 0));
		var leftFoot = CreateBoneNode(leftLowerLeg, "LeftFoot", new float3(-0.03f, -0.35f, 0.05f));

		// Store knee reference (mid-point of leg)
		var leftKnee = leftLowerLeg;

		// 6. Right leg chain: Hips → UpperLeg → LowerLeg → Foot
		var rightUpperLeg = CreateBoneNode(hips, "RightUpperLeg", new float3(0.1f, -0.05f, 0));
		var rightLowerLeg = CreateBoneNode(rightUpperLeg, "RightLowerLeg", new float3(0.02f, -0.4f, 0));
		var rightFoot = CreateBoneNode(rightLowerLeg, "RightFoot", new float3(0.03f, -0.35f, 0.05f));

		// Store knee reference (mid-point of leg)
		var rightKnee = rightLowerLeg;

		// Create SkeletonBuilder component to manage the skeleton
		var skeletonBuilder = skeletonRootSlot.AttachComponent<SkeletonBuilder>();
		skeletonBuilder.BuildFromHierarchy(hips);

		// Create KinetixVRIK component for full-body IK
		var vrik = userRoot.Slot.AttachComponent<KinetixVRIK>();
		vrik.Skeleton.Target = skeletonBuilder;
		vrik.SetupTracking(userRoot);

		// Add sphere meshes to key body parts for visualization
		// This makes it easier to see the avatar and IK working
		AddBodyPartVisuals(head, 0.08f);           // Head
		AddBodyPartVisuals(leftHand, 0.06f);      // Left hand
		AddBodyPartVisuals(rightHand, 0.06f);     // Right hand
		AddBodyPartVisuals(leftFoot, 0.07f);      // Left foot
		AddBodyPartVisuals(rightFoot, 0.07f);     // Right foot
		AddBodyPartVisuals(chest, 0.1f);          // Chest
		AddBodyPartVisuals(hips, 0.08f);          // Hips
		AddBodyPartVisuals(leftElbow, 0.05f);     // Left elbow
		AddBodyPartVisuals(rightElbow, 0.05f);    // Right elbow
		AddBodyPartVisuals(leftKnee, 0.05f);      // Left knee
		AddBodyPartVisuals(rightKnee, 0.05f);     // Right knee

		AquaLogger.Log($"SimpleUserSpawn: Built avatar with {skeletonBuilder.BoneCount} bones and Kinetix VR IK for UserRoot at slot '{userRoot.Slot.SlotName.Value}'");
	}

	/// <summary>
	/// Create a bone node without visual representation.
	/// Returns the created slot so it can be used as a parent for child bones.
	/// </summary>
	private Slot CreateBoneNode(Slot parent, string name, float3 position)
	{
		var nodeSlot = parent.AddSlot(name);
		nodeSlot.LocalPosition.Value = position;
		nodeSlot.LocalScale.Value = float3.One;

		return nodeSlot;
	}

	/// <summary>
	/// Add sphere mesh visualization to a body part slot.
	/// This makes the avatar visible and helps debug IK.
	/// </summary>
	private void AddBodyPartVisuals(Slot slot, float radius)
	{
		if (slot == null)
			return;

		slot.LocalScale.Value = new float3(radius * 2f, radius * 2f, radius * 2f);

		// Add sphere mesh renderer
		var meshRenderer = slot.AttachComponent<MeshRenderer>();
		// MeshRenderer will use placeholder sphere mesh from hook
	}

	// Unused IWorldEventReceiver methods (required by interface)
	public void OnFocusChanged(World.WorldFocus focus) { }
	public void OnWorldDestroy() { }
}
