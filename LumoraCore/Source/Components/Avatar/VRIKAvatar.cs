// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Components.Avatar.IK;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Full-body VR IK avatar. The single IK component - owns an engine-side
/// FABRIK solver, no platform IK dependency.
///
/// Responsibilities:
/// 1. Create one proxy slot per tracked body node.
/// 2. Equip one AvatarPoseNode per proxy onto the matching user tracking slot.
/// 3. Run the engine FullBodyIKSolver each frame from the proxy poses.
/// 4. Apply authored reference offsets from the avatar creator flow.
/// 5. Run procedural feet when foot tracking is absent.
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
[DefaultUpdateOrder(-5000)]
public class VRIKAvatar : Component, IAvatarObjectComponent, IInputUpdateReceiver
{
    private readonly FullBodyIKSolver _solver = new();
    private struct ReferenceOffset
    {
        public bool Valid;
        public float3 LocalPosition;
        public floatQ LocalRotation;
    }

    public readonly SyncRef<SkeletonBuilder> Skeleton = null!;
    public readonly SyncRef<BipedRig> Rig = null!;
    public readonly SyncRef<UserRoot> UserRoot = null!;
    public readonly SyncRef<AvatarDescriptor> Descriptor = null!;

    public readonly Sync<float> HeightCompensation = null!;
    public readonly Sync<float> AvatarHeight = null!;
    public readonly Sync<float> UserResizeThreshold = null!;
    public readonly Sync<bool> UseProceduralFeet = null!;
    public readonly Sync<bool> IKEnabled = null!;

    protected readonly SyncRef<Slot> _headProxy = null!;
    protected readonly SyncRef<Slot> _pelvisProxy = null!;
    protected readonly SyncRef<Slot> _chestProxy = null!;
    protected readonly SyncRef<Slot> _leftHandProxy = null!;
    protected readonly SyncRef<Slot> _rightHandProxy = null!;
    protected readonly SyncRef<Slot> _leftElbowProxy = null!;
    protected readonly SyncRef<Slot> _rightElbowProxy = null!;
    protected readonly SyncRef<Slot> _leftFootProxy = null!;
    protected readonly SyncRef<Slot> _rightFootProxy = null!;
    protected readonly SyncRef<Slot> _leftKneeProxy = null!;
    protected readonly SyncRef<Slot> _rightKneeProxy = null!;

    protected readonly SyncRef<AvatarPoseNode> _headNode = null!;
    protected readonly SyncRef<AvatarPoseNode> _pelvisNode = null!;
    protected readonly SyncRef<AvatarPoseNode> _chestNode = null!;
    protected readonly SyncRef<AvatarPoseNode> _leftHandNode = null!;
    protected readonly SyncRef<AvatarPoseNode> _rightHandNode = null!;
    protected readonly SyncRef<AvatarPoseNode> _leftElbowNode = null!;
    protected readonly SyncRef<AvatarPoseNode> _rightElbowNode = null!;
    protected readonly SyncRef<AvatarPoseNode> _leftFootNode = null!;
    protected readonly SyncRef<AvatarPoseNode> _rightFootNode = null!;
    protected readonly SyncRef<AvatarPoseNode> _leftKneeNode = null!;
    protected readonly SyncRef<AvatarPoseNode> _rightKneeNode = null!;

    private bool _isInitialized;
    private bool _isRegistered;
    private ReferenceOffset _viewOffset;
    private ReferenceOffset _leftHandOffset;
    private ReferenceOffset _rightHandOffset;
    private ReferenceOffset _leftFootOffset;
    private ReferenceOffset _rightFootOffset;

    public bool IsEquipped => _headNode?.Target?.IsEquipped ?? false;
    public AvatarPoseNode? HeadNode => _headNode?.Target;
    public AvatarPoseNode? PelvisNode => _pelvisNode?.Target;
    public AvatarPoseNode? LeftHandNode => _leftHandNode?.Target;
    public AvatarPoseNode? RightHandNode => _rightHandNode?.Target;
    public AvatarPoseNode? LeftFootNode => _leftFootNode?.Target;
    public AvatarPoseNode? RightFootNode => _rightFootNode?.Target;
    public Slot? HeadProxy => _headProxy?.Target;
    public Slot? LeftHandProxy => _leftHandProxy?.Target;
    public Slot? RightHandProxy => _rightHandProxy?.Target;

    public BodyNode Node => BodyNode.Root;

    public void OnPreEquip(AvatarObjectSlot slot) { }

    public void OnEquip(AvatarObjectSlot slot)
    {
        LumoraLogger.Log($"VRIKAvatar: Equipped to {slot.Node.Value}");
    }

    public void OnDequip(AvatarObjectSlot slot)
    {
        LumoraLogger.Log($"VRIKAvatar: Dequipped from {slot.Node.Value}");
    }

    private readonly UserRootRegistrationTracker _userRootReg;

    public VRIKAvatar()
    {
        _userRootReg = new UserRootRegistrationTracker(this);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        _userRootReg.Attach();
    }

    public override void OnInit()
    {
        base.OnInit();
        HeightCompensation.Value = 0.95f;
        AvatarHeight.Value = 1.7f;
        UserResizeThreshold.Value = 0.2f;
        UseProceduralFeet.Value = true;
        IKEnabled.Value = true;
    }

    public override void OnStart()
    {
        base.OnStart();

        var input = Engine.Current?.InputInterface;
        if (input != null)
        {
            input.RegisterInputEventReceiver(this);
            _isRegistered = true;
        }

        if (Skeleton.Target == null)
            Skeleton.Target = Slot.GetComponent<SkeletonBuilder>() ?? Slot.GetComponentInChildren<SkeletonBuilder>();

        if (Rig.Target == null)
            Rig.Target = Slot.GetComponent<BipedRig>() ?? Slot.GetComponentInChildren<BipedRig>();

        if (Descriptor.Target == null)
            Descriptor.Target = Slot.GetComponent<AvatarDescriptor>();

        if (UserRoot.Target == null)
        {
            var userRoot = Slot.GetComponent<UserRoot>();
            if (userRoot == null && Slot.Parent != null)
                userRoot = Slot.Parent.GetComponent<UserRoot>();
            if (userRoot != null)
                UserRoot.Target = userRoot;
        }

        _isInitialized = Skeleton.Target != null && Skeleton.Target.IsBuilt.Value;
        RecomputeReferenceOffsets();

        if (_isInitialized)
            LumoraLogger.Log($"VRIKAvatar: Started on '{Slot.SlotName.Value}' with skeleton");
        else
            LumoraLogger.Warn($"VRIKAvatar: No valid skeleton found on '{Slot.SlotName.Value}'");
    }

    public override void OnDestroy()
    {
        _userRootReg.Detach();

        if (_isRegistered)
        {
            Engine.Current?.InputInterface?.UnregisterInputEventReceiver(this);
            _isRegistered = false;
        }

        _isInitialized = false;
        base.OnDestroy();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (!_isInitialized && Skeleton.Target != null && Skeleton.Target.IsBuilt.Value)
        {
            _isInitialized = true;
            EnsureSolver();
            RecomputeReferenceOffsets();
            LumoraLogger.Log("VRIKAvatar: Late-start with skeleton");
        }
    }

    public void BeforeInputUpdate()
    {
        if (!IKEnabled.Value || !_isInitialized)
            return;

        if (UseProceduralFeet.Value)
        {
            bool leftTracked = _leftFootNode.Target?.IsEquippedAndActive ?? false;
            bool rightTracked = _rightFootNode.Target?.IsEquippedAndActive ?? false;
            if (!leftTracked || !rightTracked)
                UpdateProceduralFeet();
        }
    }

    public void AfterInputUpdate()
    {
        if (!IKEnabled.Value || !_isInitialized)
            return;

        ApplyReferenceOffsets();
        SolveBody();
    }

    private void EnsureSolver()
    {
        if (Rig.Target != null && !Rig.Target.IsDestroyed)
            _solver.Initialize(Rig.Target);
    }

    // Read final proxy poses and run the engine FABRIK solve onto the rig bones.
    private void SolveBody()
    {
        if (!_solver.IsReady)
        {
            EnsureSolver();
            if (!_solver.IsReady)
                return;
        }

        _solver.Solve(
            ProxyTarget(_headProxy.Target),
            ProxyTarget(_leftHandProxy.Target),
            ProxyTarget(_rightHandProxy.Target),
            ProxyTarget(_leftFootProxy.Target),
            ProxyTarget(_rightFootProxy.Target));
    }

    private static FullBodyIKSolver.Target ProxyTarget(Slot proxy)
    {
        if (proxy == null || proxy.IsDestroyed)
            return default;
        return new FullBodyIKSolver.Target
        {
            Valid = true,
            Position = proxy.GlobalPosition,
            Rotation = proxy.GlobalRotation,
        };
    }

    /// <summary>
    /// Full avatar setup using explicit skeleton and rig references.
    /// </summary>
    public void Setup(SkeletonBuilder skeleton, BipedRig rig, UserRoot userRoot)
    {
        Skeleton.Target = skeleton;
        Rig.Target = rig;
        UserRoot.Target = userRoot;

        EnsurePoseNodes();
        EnsureSolver();
        RecomputeReferenceOffsets();

        _isInitialized = true;
        LumoraLogger.Log($"VRIKAvatar: Setup complete ({rig?.Bones.Count ?? 0} bones)");
    }

    /// <summary>
    /// Full avatar setup using a finalized avatar descriptor.
    /// </summary>
    public bool SetupFromDescriptor(AvatarDescriptor descriptor, UserRoot userRoot)
    {
        if (descriptor == null)
        {
            LumoraLogger.Warn("VRIKAvatar: Cannot setup from a null descriptor");
            return false;
        }

        Descriptor.Target = descriptor;
        descriptor.ResolveAvatarData(Slot);

        var skeleton = descriptor.Skeleton.Target ?? Slot.GetComponentInChildren<SkeletonBuilder>();
        var rig = descriptor.Rig.Target ?? Slot.GetComponentInChildren<BipedRig>();
        if (skeleton == null || rig == null)
        {
            LumoraLogger.Warn("VRIKAvatar: Descriptor is missing skeleton or rig references");
            return false;
        }

        descriptor.Skeleton.Target = skeleton;
        descriptor.Rig.Target = rig;
        Setup(skeleton, rig, userRoot);
        SetupTracking(userRoot);
        return true;
    }

    /// <summary>
    /// Connect tracking by equipping our pose nodes to the local user's tracking slots.
    /// </summary>
    public void SetupTracking(UserRoot userRoot)
    {
        if (userRoot == null)
        {
            LumoraLogger.Warn("VRIKAvatar: Cannot setup tracking - null UserRoot");
            return;
        }

        UserRoot.Target = userRoot;
        EnsurePoseNodes();

        EquipPoseNodeToBodySlot(userRoot.HeadSlot, _headNode.Target);
        EquipPoseNodeToBodySlot(userRoot.LeftHandSlot, _leftHandNode.Target);
        EquipPoseNodeToBodySlot(userRoot.RightHandSlot, _rightHandNode.Target);
        EquipPoseNodeToBodySlot(userRoot.LeftFootSlot, _leftFootNode.Target);
        EquipPoseNodeToBodySlot(userRoot.RightFootSlot, _rightFootNode.Target);

        EnsureSolver();
        RecomputeReferenceOffsets();
        LumoraLogger.Log($"VRIKAvatar: Tracking connected for UserRoot '{userRoot.Slot.SlotName.Value}'");
    }

    private void EnsurePoseNodes()
    {
        EnsurePoseNode(_headProxy, _headNode, BodyNode.Head, "Head");
        EnsurePoseNode(_pelvisProxy, _pelvisNode, BodyNode.Hips, "Hips");
        EnsurePoseNode(_chestProxy, _chestNode, BodyNode.Chest, "Chest");
        EnsurePoseNode(_leftHandProxy, _leftHandNode, BodyNode.LeftHand, "LeftHand");
        EnsurePoseNode(_rightHandProxy, _rightHandNode, BodyNode.RightHand, "RightHand");
        EnsurePoseNode(_leftElbowProxy, _leftElbowNode, BodyNode.LeftLowerArm, "LeftElbow");
        EnsurePoseNode(_rightElbowProxy, _rightElbowNode, BodyNode.RightLowerArm, "RightElbow");
        EnsurePoseNode(_leftFootProxy, _leftFootNode, BodyNode.LeftFoot, "LeftFoot");
        EnsurePoseNode(_rightFootProxy, _rightFootNode, BodyNode.RightFoot, "RightFoot");
        EnsurePoseNode(_leftKneeProxy, _leftKneeNode, BodyNode.LeftLowerLeg, "LeftKnee");
        EnsurePoseNode(_rightKneeProxy, _rightKneeNode, BodyNode.RightLowerLeg, "RightKnee");
    }

    private AvatarPoseNode EnsurePoseNode(
        SyncRef<Slot> proxy,
        SyncRef<AvatarPoseNode> poseNode,
        BodyNode node,
        string name)
    {
        if (proxy.Target == null)
            proxy.Target = Slot.AddSlot($"{name}Proxy");

        if (poseNode.Target == null)
            poseNode.Target = proxy.Target.AttachComponent<AvatarPoseNode>();

        poseNode.Target.Node.Value = node;
        return poseNode.Target;
    }

    private static void EquipPoseNodeToBodySlot(Slot bodySlot, AvatarPoseNode poseNode)
    {
        if (bodySlot == null || poseNode == null)
            return;

        var avatarSlot = bodySlot.GetComponent<AvatarObjectSlot>();
        if (avatarSlot == null)
            avatarSlot = bodySlot.GetComponent<TrackedDevicePositioner>()?.ObjectSlot.Target;
        if (avatarSlot == null)
            avatarSlot = bodySlot.GetComponentInChildren<AvatarObjectSlot>();

        if (avatarSlot == null)
        {
            LumoraLogger.Warn($"VRIKAvatar: No AvatarObjectSlot found for body slot '{bodySlot.SlotName.Value}'");
            return;
        }

        var dequippedObjects = new HashSet<IAvatarObject>();
        if (!avatarSlot.PreEquip(poseNode, dequippedObjects))
        {
            LumoraLogger.Warn($"VRIKAvatar: Failed to prepare {poseNode.Node.Value} for '{bodySlot.SlotName.Value}'");
            return;
        }

        avatarSlot.Equip(poseNode);
        LumoraLogger.Log($"VRIKAvatar: Equipped {poseNode.Node.Value} pose node -> '{bodySlot.SlotName.Value}'");
    }

    private void RecomputeReferenceOffsets()
    {
        _viewOffset = default;
        _leftHandOffset = default;
        _rightHandOffset = default;
        _leftFootOffset = default;
        _rightFootOffset = default;

        var descriptor = Descriptor.Target;
        var rig = Rig.Target;
        if (descriptor == null || rig == null)
            return;

        TryComputeReferenceOffset(rig.TryGetBone(BodyNode.Head), descriptor.ViewReference.Target, ref _viewOffset);
        TryComputeReferenceOffset(rig.TryGetBone(BodyNode.LeftHand), descriptor.LeftHandReference.Target, ref _leftHandOffset);
        TryComputeReferenceOffset(rig.TryGetBone(BodyNode.RightHand), descriptor.RightHandReference.Target, ref _rightHandOffset);
        TryComputeReferenceOffset(rig.TryGetBone(BodyNode.LeftFoot), descriptor.LeftFootReference.Target, ref _leftFootOffset);
        TryComputeReferenceOffset(rig.TryGetBone(BodyNode.RightFoot), descriptor.RightFootReference.Target, ref _rightFootOffset);
    }

    private static void TryComputeReferenceOffset(Slot bone, Slot reference, ref ReferenceOffset offset)
    {
        offset = default;
        if (bone == null || reference == null || bone.IsDestroyed || reference.IsDestroyed)
            return;

        var boneRotation = bone.GlobalRotation;
        offset.LocalPosition = boneRotation.Inverse * (reference.GlobalPosition - bone.GlobalPosition);
        offset.LocalRotation = boneRotation.Inverse * reference.GlobalRotation;
        offset.Valid = true;
    }

    private void ApplyReferenceOffsets()
    {
        ApplyReferenceOffset(_headProxy.Target, _viewOffset);
        ApplyReferenceOffset(_leftHandProxy.Target, _leftHandOffset);
        ApplyReferenceOffset(_rightHandProxy.Target, _rightHandOffset);
        ApplyReferenceOffset(_leftFootProxy.Target, _leftFootOffset);
        ApplyReferenceOffset(_rightFootProxy.Target, _rightFootOffset);
    }

    private static void ApplyReferenceOffset(Slot proxy, in ReferenceOffset offset)
    {
        if (proxy == null || proxy.IsDestroyed || !offset.Valid)
            return;

        var referencePosition = proxy.GlobalPosition;
        var referenceRotation = proxy.GlobalRotation;
        var desiredRotation = referenceRotation * offset.LocalRotation.Inverse;
        var desiredPosition = referencePosition - (desiredRotation * offset.LocalPosition);

        proxy.GlobalRotation = desiredRotation;
        proxy.GlobalPosition = desiredPosition;
    }

    private void UpdateProceduralFeet()
    {
        var headSlot = _headProxy.Target;
        if (headSlot == null)
            return;

        float3 headPos = headSlot.GlobalPosition;
        float3 forward = headSlot.GlobalRotation * float3.Backward;
        forward.y = 0f;
        if (forward.LengthSquared < 0.001f)
            forward = float3.Backward;
        forward = forward.Normalized;

        floatQ bodyRot = floatQ.LookRotation(forward, float3.Up);
        float3 right = bodyRot * float3.Right;

        const float heightOffset = -1.6f;
        const float footSeparation = 0.15f;

        SetProceduralFoot(
            _leftFootProxy.Target,
            _leftFootNode.Target,
            headPos + new float3(0f, heightOffset, 0f) - right * footSeparation);

        SetProceduralFoot(
            _rightFootProxy.Target,
            _rightFootNode.Target,
            headPos + new float3(0f, heightOffset, 0f) + right * footSeparation);
    }

    private static void SetProceduralFoot(Slot proxy, AvatarPoseNode node, float3 worldPos)
    {
        if (proxy == null || (node?.IsEquippedAndActive ?? false))
            return;

        var parent = proxy.Parent;
        proxy.LocalPosition.Value = parent != null
            ? parent.GlobalPointToLocal(worldPos)
            : worldPos;
    }

    public void LogDiagnosticInfo()
    {
        LumoraLogger.Log("VRIKAvatar Diagnostic:");
        LumoraLogger.Log($"  IsInitialized:       {_isInitialized}");
        LumoraLogger.Log($"  IsEquipped:          {IsEquipped}");
        LumoraLogger.Log($"  Skeleton:            {Skeleton.Target?.Slot.SlotName.Value ?? "null"}");
        LumoraLogger.Log($"  Rig bones:           {Rig.Target?.Bones.Count ?? 0}");
        LumoraLogger.Log($"  UserRoot:            {UserRoot.Target?.Slot.SlotName.Value ?? "null"}");
        LumoraLogger.Log($"  HeadProxy:           {_headProxy.Target?.SlotName.Value ?? "null"}");
        LumoraLogger.Log($"  HeadNode equipped:   {_headNode.Target?.IsEquipped ?? false}");
        LumoraLogger.Log($"  LHandNode equipped:  {_leftHandNode.Target?.IsEquipped ?? false}");
        LumoraLogger.Log($"  RHandNode equipped:  {_rightHandNode.Target?.IsEquipped ?? false}");
        LumoraLogger.Log($"  LFootNode equipped:  {_leftFootNode.Target?.IsEquipped ?? false}");
        LumoraLogger.Log($"  RFootNode equipped:  {_rightFootNode.Target?.IsEquipped ?? false}");
    }
}
