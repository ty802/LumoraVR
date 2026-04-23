// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// VR avatar orchestrator.
///
/// Responsibilities:
/// 1. Create one proxy slot per tracked body node.
/// 2. Equip one AvatarPoseNode per proxy onto the matching user tracking slot.
/// 3. Wire the proxies into GodotIKAvatar so the hook can solve the skeleton.
/// 4. Apply authored reference offsets from the avatar creator flow.
/// 5. Run procedural feet when foot tracking is absent.
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
[DefaultUpdateOrder(-5000)]
public class VRIKAvatar : ImplementableComponent, IAvatarObjectComponent, IInputUpdateReceiver
{
    private struct ReferenceOffset
    {
        public bool Valid;
        public float3 LocalPosition;
        public floatQ LocalRotation;
    }

    public readonly SyncRef<SkeletonBuilder> Skeleton;
    public readonly SyncRef<BipedRig> Rig;
    public readonly SyncRef<UserRoot> UserRoot;
    public readonly SyncRef<AvatarDescriptor> Descriptor;

    public readonly Sync<float> HeightCompensation;
    public readonly Sync<float> AvatarHeight;
    public readonly Sync<float> UserResizeThreshold;
    public readonly Sync<bool> UseProceduralFeet;
    public readonly Sync<bool> IKEnabled;

    protected readonly SyncRef<Slot> _headProxy;
    protected readonly SyncRef<Slot> _pelvisProxy;
    protected readonly SyncRef<Slot> _chestProxy;
    protected readonly SyncRef<Slot> _leftHandProxy;
    protected readonly SyncRef<Slot> _rightHandProxy;
    protected readonly SyncRef<Slot> _leftElbowProxy;
    protected readonly SyncRef<Slot> _rightElbowProxy;
    protected readonly SyncRef<Slot> _leftFootProxy;
    protected readonly SyncRef<Slot> _rightFootProxy;
    protected readonly SyncRef<Slot> _leftKneeProxy;
    protected readonly SyncRef<Slot> _rightKneeProxy;

    protected readonly SyncRef<AvatarPoseNode> _headNode;
    protected readonly SyncRef<AvatarPoseNode> _pelvisNode;
    protected readonly SyncRef<AvatarPoseNode> _chestNode;
    protected readonly SyncRef<AvatarPoseNode> _leftHandNode;
    protected readonly SyncRef<AvatarPoseNode> _rightHandNode;
    protected readonly SyncRef<AvatarPoseNode> _leftElbowNode;
    protected readonly SyncRef<AvatarPoseNode> _rightElbowNode;
    protected readonly SyncRef<AvatarPoseNode> _leftFootNode;
    protected readonly SyncRef<AvatarPoseNode> _rightFootNode;
    protected readonly SyncRef<AvatarPoseNode> _leftKneeNode;
    protected readonly SyncRef<AvatarPoseNode> _rightKneeNode;

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
            RecomputeReferenceOffsets();
            LumoraLogger.Log("VRIKAvatar: Late-start with skeleton");
        }

        RunApplyChanges();
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
        WireToGodotIK();
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

        WireToGodotIK();
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

    private void WireToGodotIK()
    {
        var godotIK = Slot.GetComponent<GodotIKAvatar>();
        if (godotIK == null)
        {
            var current = Slot.Parent;
            while (current != null && godotIK == null)
            {
                godotIK = current.GetComponent<GodotIKAvatar>();
                current = current.Parent;
            }
        }

        if (godotIK == null)
        {
            LumoraLogger.Warn("VRIKAvatar: No GodotIKAvatar found in hierarchy");
            return;
        }

        if (_headProxy.Target != null)
            godotIK.HeadTarget.Target = _headProxy.Target;
        if (_leftHandProxy.Target != null)
            godotIK.LeftHandTarget.Target = _leftHandProxy.Target;
        if (_rightHandProxy.Target != null)
            godotIK.RightHandTarget.Target = _rightHandProxy.Target;
        if (_leftFootProxy.Target != null)
            godotIK.LeftFootTarget.Target = _leftFootProxy.Target;
        if (_rightFootProxy.Target != null)
            godotIK.RightFootTarget.Target = _rightFootProxy.Target;

        LumoraLogger.Log($"VRIKAvatar: Wired proxy slots to GodotIKAvatar on '{godotIK.Slot.SlotName.Value}'");
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
