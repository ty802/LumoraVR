using System.Collections.Generic;
using System.Linq;
using Aquamarine.Source.Helpers;
using Aquamarine.Source.Input;
using Aquamarine.Source.Scene.RootObjects;
using RuntimeEngine = Aquamarine.Source.Core.Engine;
using Aquamarine.Source.Core;
using Aquamarine.Source.Logging;
using Aquamarine.Kinetix.Components;
using Godot;
using Aquamarine.Source.Management;
using Logger = Aquamarine.Source.Logging.Logger;

namespace Aquamarine.Source.Scene.ChildObjects;

public partial class HumanoidAnimator : Node3D, IChildObject
{
	public Armature Armature
	{
		get;
		set
		{
			field = value;
			CallDeferred(MethodName.UpdateBoneIndices);
		}
	}
	public ICharacterController CharacterController;

	public string HeadBone;
	public string HipBone;
	public string LeftHandBone;
	public string RightHandBone;
	public string LeftFootBone;
	public string RightFootBone;

	public Transform3D HeadBoneOffset = Transform3D.Identity;
	public Transform3D HipBoneOffset = Transform3D.Identity;
	public Transform3D LeftHandBoneOffset = Transform3D.Identity;
	public Transform3D RightHandBoneOffset = Transform3D.Identity;
	public Transform3D LeftFootBoneOffset = Transform3D.Identity;
	public Transform3D RightFootBoneOffset = Transform3D.Identity;

	public bool Digitigrade;
	public bool Valid;

	private int[] _spineBones;
	private float[] _spineBoneLengths;
	private Vector3[] _spineBoneVectors;
	private float[] _spineBonePosition;

	private KinetixTwoBoneIK _leftArm;
	private KinetixTwoBoneIK _rightArm;

	private Node3D _leftLeg;  // Can be KinetixTwoBoneIK or KinetixDigitigradeIK
	private Node3D _rightLeg;

	// Debug mode to enable visual debugging of bone placements
	[Export] public bool DebugBones = false;

	public Node Self => this;
	public void SetPlayerAuthority(int id)
	{

	}
	public void Initialize(Godot.Collections.Dictionary<string, Variant> data)
	{
		if (data.TryGetValue("digitigrade", out var d) && d.TryGetBool(out var isDigi)) Digitigrade = isDigi;

		if (data.TryGetValue("headBone", out var bone)) HeadBone = bone.AsString();
		if (data.TryGetValue("hipBone", out var hbone)) HipBone = hbone.AsString();
		if (data.TryGetValue("leftHandBone", out var lbone)) LeftHandBone = lbone.AsString();
		if (data.TryGetValue("rightHandBone", out var rbone)) RightHandBone = rbone.AsString();
		if (data.TryGetValue("leftFootBone", out var lfbone)) LeftFootBone = lfbone.AsString();
		if (data.TryGetValue("rightFootBone", out var rfbone)) RightFootBone = rfbone.AsString();

		if (data.TryGetValue("headOffset", out var offset) && offset.TryGetTransform3D(out var offsetArray)) HeadBoneOffset = offsetArray;
		if (data.TryGetValue("hipOffset", out var hoffset) && hoffset.TryGetTransform3D(out var hoffsetArray)) HipBoneOffset = hoffsetArray;
		if (data.TryGetValue("leftHandOffset", out var loffset) && loffset.TryGetTransform3D(out var loffsetArray)) LeftHandBoneOffset = loffsetArray;
		if (data.TryGetValue("rightHandOffset", out var roffset) && roffset.TryGetTransform3D(out var roffsetArray)) RightHandBoneOffset = roffsetArray;
		if (data.TryGetValue("leftFootOffset", out var lfoffset) && lfoffset.TryGetTransform3D(out var lfoffsetArray)) LeftFootBoneOffset = lfoffsetArray;
		if (data.TryGetValue("rightFootOffset", out var rfoffset) && rfoffset.TryGetTransform3D(out var rfoffsetArray)) RightFootBoneOffset = rfoffsetArray;

		if (Root is not Avatar avi || avi.Parent is not ICharacterController charController) return;
		CharacterController = charController;

		if (data.TryGetValue("armature", out var index) && index.TryGetInt32(out var skeletonIndex))
		{
			if (skeletonIndex >= 0 && Root.ChildObjects.TryGetValue((ushort)skeletonIndex, out var v) && v is Armature armature) Armature = armature;
		}

		ProcessPriority = 1;

		// Initialize IK for arms and legs
		(_leftArm, _, _) = InitChain<KinetixTwoBoneIK>("LeftArmIK");
		(_rightArm, _, _) = InitChain<KinetixTwoBoneIK>("RightArmIK");

		if (Digitigrade)
		{
			(_leftLeg, _, _) = InitChain<KinetixDigitigradeIK>("LeftLegIK");
			(_rightLeg, _, _) = InitChain<KinetixDigitigradeIK>("RightLegIK");
		}
		else
		{
			(_leftLeg, _, _) = InitChain<KinetixTwoBoneIK>("LeftLegIK");
			(_rightLeg, _, _) = InitChain<KinetixTwoBoneIK>("RightLegIK");
		}

		// Log bone offsets for debugging
		Logger.Log($"HumanoidAnimator initialized with bone offsets:");
		Logger.Log($"  - Head: {HeadBoneOffset}");
		Logger.Log($"  - Hip: {HipBoneOffset}");
		Logger.Log($"  - Left Hand: {LeftHandBoneOffset}");
		Logger.Log($"  - Right Hand: {RightHandBoneOffset}");
		Logger.Log($"  - Left Foot: {LeftFootBoneOffset}");
		Logger.Log($"  - Right Foot: {RightFootBoneOffset}");

		return;

		(T node, Node3D target, Node3D poleTarget) InitChain<T>(string name = "") where T : Node3D, new()
		{
			var obj = new T
			{
				Name = name
			};

			// Set properties using reflection/dynamic to work with both KinetixTwoBoneIK and KinetixDigitigradeIK
			var type = typeof(T);
			type.GetProperty("Skeleton")?.SetValue(obj, Armature);
			type.GetProperty("ProcessPriority")?.SetValue(obj, 2);

			var tar = new Node3D { Name = $"{name}Target" };
			AddChild(tar);
			type.GetProperty("Target")?.SetValue(obj, tar);

			var poleTar = new Node3D { Name = $"{name}PoleTarget" };
			AddChild(poleTar);
			type.GetProperty("PoleTarget")?.SetValue(obj, poleTar);

			AddChild(obj);

			return (obj, tar, poleTar);
		}
	}
	public void AddChildObject(ISceneObject obj)
	{
		AddChild(obj.Self);
	}
	private void UpdateBoneIndices()
	{
		Valid = false;

		_leftArm.TipBoneName = LeftHandBone;
		_rightArm.TipBoneName = RightHandBone;
		SetIKTipBone(_leftLeg, LeftFootBone);
		SetIKTipBone(_rightLeg, RightFootBone);

		var headBone = Armature.FindBone(HeadBone);
		if (headBone < 0) 
		{
			Logger.Error($"Failed to find head bone '{HeadBone}'");
			return;
		}
		
		var hipBone = Armature.FindBone(HipBone);
		if (hipBone < 0) 
		{
			Logger.Error($"Failed to find hip bone '{HipBone}'");
			return;
		}

		var boneList = new List<int>();
		var currentBone = headBone;
		while (currentBone >= 0 && currentBone != hipBone)
		{
			boneList.Add(currentBone);
			currentBone = Armature.GetBoneParent(currentBone);
		}
		if (currentBone < 0 || currentBone != hipBone) 
		{
			Logger.Error("Failed to find spine bone chain from head to hip");
			return;
		}
		
		boneList.Add(hipBone);

		var vectorList = new List<Vector3> { Vector3.Zero };
		vectorList.AddRange(boneList.SkipLast(1).Select(b => Armature.GetBoneRest(b).Origin));

		var lengthList = vectorList.Select(i => i.Length()).ToArray();

		var overallLength = lengthList.Sum();

		var positionList = new List<float>();
		var currentLength = 0f;
		foreach (var length in lengthList)
		{
			currentLength += length;
			positionList.Add(currentLength / overallLength);
		}

		_spineBones = boneList.ToArray();
		_spineBoneLengths = lengthList;
		_spineBonePosition = positionList.ToArray();

		Logger.Log($"Spine bone chain established with {_spineBones.Length} bones");
		Valid = true;
	}
	
	public override void _Process(double delta)
	{
		base._Process(delta);

		if (!Valid) return;

		// Get transforms for all limbs
		var headTransform = CharacterController.GetLimbTransform3D(IInputProvider.InputLimb.Head);
		var hipTransform = CharacterController.GetLimbTransform3D(IInputProvider.InputLimb.Hip);
		var leftHandTransform = CharacterController.GetLimbTransform3D(IInputProvider.InputLimb.LeftHand);
		var rightHandTransform = CharacterController.GetLimbTransform3D(IInputProvider.InputLimb.RightHand);
		var leftFootTransform = CharacterController.GetLimbTransform3D(IInputProvider.InputLimb.LeftFoot);
		var rightFootTransform = CharacterController.GetLimbTransform3D(IInputProvider.InputLimb.RightFoot);

		// Apply bone offsets
		headTransform = headTransform * HeadBoneOffset;
		hipTransform = hipTransform * HipBoneOffset;
		
		// Apply special rotation adjustments for VR mode if needed
		bool isVR = IInputProvider.Instance?.IsVR ?? false;
		if (isVR)
		{
			// VR-specific rotational adjustments could be applied here
			// This helps align the controller orientation with the avatar's hand bones
		}
		
		// Apply IK target transformations
		_leftArm.Target.GlobalTransform = CharacterController.GlobalTransform * leftHandTransform * LeftHandBoneOffset;
		_rightArm.Target.GlobalTransform = CharacterController.GlobalTransform * rightHandTransform * RightHandBoneOffset;
		SetIKTarget(_leftLeg, CharacterController.GlobalTransform * leftFootTransform * LeftFootBoneOffset);
		SetIKTarget(_rightLeg, CharacterController.GlobalTransform * rightFootTransform * RightFootBoneOffset);

		var length = _spineBones.Length - 1;

		// Get rotation quaternions
		var headRotation = headTransform.Basis.GetRotationQuaternion();
		var hipRotation = hipTransform.Basis.GetRotationQuaternion();

		// Set spine bone rotations - interpolate between head and hip rotations
		for (var i = length; i >= 0; i--)
		{
			var interpolatedRot = headRotation.Slerp(hipRotation, _spineBonePosition[i]);
			var currentPose = Armature.GetBoneGlobalPose(_spineBones[i]);
			currentPose.Basis = new Basis(interpolatedRot);
			Armature.SetBoneGlobalPoseOverride(_spineBones[i], currentPose, 1.0f, true);
		}

		// Apply head position offset
		var currentHeadPosition = Armature.GetBoneGlobalPose(_spineBones.First()).Origin;
		var wishHeadPosition = headTransform.Origin;
		var offset = wishHeadPosition - currentHeadPosition;
		var lastBonePose = Armature.GetBoneGlobalPose(_spineBones.Last());
		lastBonePose.Origin = lastBonePose.Origin + offset;
		Armature.SetBoneGlobalPoseOverride(_spineBones.Last(), lastBonePose, 1.0f, true);
		
		// Debug visualization
		if (DebugBones && RuntimeEngine.ShowDebug)
		{
			// Calculate global transforms for visualization
			var globalHead = CharacterController.GlobalTransform * headTransform;
			var globalHip = CharacterController.GlobalTransform * hipTransform;
			var globalLeftHand = CharacterController.GlobalTransform * leftHandTransform * LeftHandBoneOffset;
			var globalRightHand = CharacterController.GlobalTransform * rightHandTransform * RightHandBoneOffset;
			var globalLeftFoot = CharacterController.GlobalTransform * leftFootTransform * LeftFootBoneOffset;
			var globalRightFoot = CharacterController.GlobalTransform * rightFootTransform * RightFootBoneOffset;
			
			// Draw spheres at key points
			DebugDraw3D.DrawSphere(globalHead.Origin, 0.05f, Colors.Cyan);
			DebugDraw3D.DrawSphere(globalHip.Origin, 0.05f, Colors.Green);
			DebugDraw3D.DrawSphere(globalLeftHand.Origin, 0.05f, Colors.Magenta);
			DebugDraw3D.DrawSphere(globalRightHand.Origin, 0.05f, Colors.Yellow);
			DebugDraw3D.DrawSphere(globalLeftFoot.Origin, 0.05f, Colors.Blue);
			DebugDraw3D.DrawSphere(globalRightFoot.Origin, 0.05f, Colors.Red);
			
			// Draw lines connecting key points
			DebugDraw3D.DrawLine(globalHead.Origin, globalHip.Origin, Colors.White);
			DebugDraw3D.DrawLine(globalHip.Origin, globalLeftFoot.Origin, Colors.Blue);
			DebugDraw3D.DrawLine(globalHip.Origin, globalRightFoot.Origin, Colors.Red);
			DebugDraw3D.DrawLine(globalHead.Origin, globalLeftHand.Origin, Colors.Magenta);
			DebugDraw3D.DrawLine(globalHead.Origin, globalRightHand.Origin, Colors.Yellow);
		}
	}

	// Helper methods for polymorphic IK access
	private void SetIKTipBone(Node3D ik, string boneName)
	{
		if (ik is KinetixTwoBoneIK twoBoneIK)
			twoBoneIK.TipBoneName = boneName;
		else if (ik is KinetixDigitigradeIK digitigradeIK)
			digitigradeIK.TipBoneName = boneName;
	}

	private void SetIKTarget(Node3D ik, Transform3D transform)
	{
		if (ik is KinetixTwoBoneIK twoBoneIK)
			twoBoneIK.Target.GlobalTransform = transform;
		else if (ik is KinetixDigitigradeIK digitigradeIK)
			digitigradeIK.Target.GlobalTransform = transform;
	}

	public bool Dirty { get; }
	public IRootObject Root { get; set; }
	public ISceneObject Parent { get; set; }
}
