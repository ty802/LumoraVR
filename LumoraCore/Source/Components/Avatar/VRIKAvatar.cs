using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// VR Inverse Kinematics Avatar component.
/// Creates proxy slots for body nodes, attaches AvatarPoseNode components
/// to receive tracking data, and uses FieldDrives to drive skeleton bones.
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
[DefaultUpdateOrder(-5000)]
public class VRIKAvatar : ImplementableComponent, IAvatarObjectComponent, IInputUpdateReceiver
{
    // ===== REFERENCES =====

    /// <summary>
    /// Reference to the SkeletonBuilder managing the avatar bones.
    /// </summary>
    public SyncRef<SkeletonBuilder> Skeleton { get; private set; }

    /// <summary>
    /// Reference to the BipedRig that maps body nodes to skeleton bones.
    /// </summary>
    public SyncRef<BipedRig> Rig { get; private set; }

    /// <summary>
    /// UserRoot that owns this avatar.
    /// </summary>
    public SyncRef<UserRoot> UserRoot { get; private set; }

    // ===== SETTINGS =====

    /// <summary>
    /// Height compensation factor (0.75-1.25).
    /// </summary>
    public Sync<float> HeightCompensation { get; private set; }

    /// <summary>
    /// Avatar height in local units.
    /// </summary>
    public Sync<float> AvatarHeight { get; private set; }

    /// <summary>
    /// User resize threshold.
    /// </summary>
    public Sync<float> UserResizeThreshold { get; private set; }

    /// <summary>
    /// Whether to use procedural feet when no trackers are available.
    /// </summary>
    public Sync<bool> UseProceduralFeet { get; private set; }

    /// <summary>
    /// Whether the IK system is enabled.
    /// </summary>
    public Sync<bool> IKEnabled { get; private set; }

    // ===== PROXY SLOTS =====

    protected SyncRef<Slot> _headProxy;
    protected SyncRef<Slot> _pelvisProxy;
    protected SyncRef<Slot> _chestProxy;
    protected SyncRef<Slot> _leftHandProxy;
    protected SyncRef<Slot> _rightHandProxy;
    protected SyncRef<Slot> _leftElbowProxy;
    protected SyncRef<Slot> _rightElbowProxy;
    protected SyncRef<Slot> _leftFootProxy;
    protected SyncRef<Slot> _rightFootProxy;
    protected SyncRef<Slot> _leftKneeProxy;
    protected SyncRef<Slot> _rightKneeProxy;

    // ===== POSE NODES =====

    protected SyncRef<AvatarPoseNode> _headNode;
    protected SyncRef<AvatarPoseNode> _pelvisNode;
    protected SyncRef<AvatarPoseNode> _chestNode;
    protected SyncRef<AvatarPoseNode> _leftHandNode;
    protected SyncRef<AvatarPoseNode> _rightHandNode;
    protected SyncRef<AvatarPoseNode> _leftElbowNode;
    protected SyncRef<AvatarPoseNode> _rightElbowNode;
    protected SyncRef<AvatarPoseNode> _leftFootNode;
    protected SyncRef<AvatarPoseNode> _rightFootNode;
    protected SyncRef<AvatarPoseNode> _leftKneeNode;
    protected SyncRef<AvatarPoseNode> _rightKneeNode;

    // ===== FIELD DRIVES (for driving skeleton bones from proxy positions) =====

    protected FieldDrive<float3> _headTargetPos;
    protected FieldDrive<floatQ> _headTargetRot;
    protected FieldDrive<float3> _pelvisTargetPos;
    protected FieldDrive<floatQ> _pelvisTargetRot;
    protected FieldDrive<float3> _leftHandTargetPos;
    protected FieldDrive<floatQ> _leftHandTargetRot;
    protected FieldDrive<float3> _rightHandTargetPos;
    protected FieldDrive<floatQ> _rightHandTargetRot;
    protected FieldDrive<float3> _leftFootTargetPos;
    protected FieldDrive<floatQ> _leftFootTargetRot;
    protected FieldDrive<float3> _rightFootTargetPos;
    protected FieldDrive<floatQ> _rightFootTargetRot;

    // ===== INTERNAL STATE =====

    private bool _isInitialized;
    private bool _isRegistered;

    // ===== PUBLIC PROPERTIES =====

    /// <summary>
    /// Whether the avatar is currently equipped.
    /// </summary>
    public bool IsEquipped => _headNode?.Target?.IsEquipped ?? false;

    /// <summary>
    /// Head pose node.
    /// </summary>
    public AvatarPoseNode HeadNode => _headNode?.Target;

    /// <summary>
    /// Pelvis pose node.
    /// </summary>
    public AvatarPoseNode PelvisNode => _pelvisNode?.Target;

    /// <summary>
    /// Left hand pose node.
    /// </summary>
    public AvatarPoseNode LeftHandNode => _leftHandNode?.Target;

    /// <summary>
    /// Right hand pose node.
    /// </summary>
    public AvatarPoseNode RightHandNode => _rightHandNode?.Target;

    /// <summary>
    /// Left foot pose node.
    /// </summary>
    public AvatarPoseNode LeftFootNode => _leftFootNode?.Target;

    /// <summary>
    /// Right foot pose node.
    /// </summary>
    public AvatarPoseNode RightFootNode => _rightFootNode?.Target;

    /// <summary>
    /// Head proxy slot (tracking target position).
    /// </summary>
    public Slot HeadProxy => _headProxy?.Target;

    /// <summary>
    /// Left hand proxy slot.
    /// </summary>
    public Slot LeftHandProxy => _leftHandProxy?.Target;

    /// <summary>
    /// Right hand proxy slot.
    /// </summary>
    public Slot RightHandProxy => _rightHandProxy?.Target;

    // ===== IAvatarObjectComponent =====

    public BodyNode Node => BodyNode.Root;

    public void OnPreEquip(AvatarObjectSlot slot)
    {
        // Called before equipping - can prepare state
    }

    public void OnEquip(AvatarObjectSlot slot)
    {
        AquaLogger.Log($"VRIKAvatar: Equipped to {slot.Node.Value}");
    }

    public void OnDequip(AvatarObjectSlot slot)
    {
        AquaLogger.Log($"VRIKAvatar: Dequipped from {slot.Node.Value}");
    }

    // ===== LIFECYCLE =====

    public override void OnAwake()
    {
        base.OnAwake();

        // Initialize sync fields
        Skeleton = new SyncRef<SkeletonBuilder>(this, null);
        Rig = new SyncRef<BipedRig>(this, null);
        UserRoot = new SyncRef<UserRoot>(this, null);

        HeightCompensation = new Sync<float>(this, 0.95f);
        AvatarHeight = new Sync<float>(this, 1.7f);
        UserResizeThreshold = new Sync<float>(this, 0.2f);
        UseProceduralFeet = new Sync<bool>(this, true);
        IKEnabled = new Sync<bool>(this, true);

        // Initialize proxy slot refs
        _headProxy = new SyncRef<Slot>(this, null);
        _pelvisProxy = new SyncRef<Slot>(this, null);
        _chestProxy = new SyncRef<Slot>(this, null);
        _leftHandProxy = new SyncRef<Slot>(this, null);
        _rightHandProxy = new SyncRef<Slot>(this, null);
        _leftElbowProxy = new SyncRef<Slot>(this, null);
        _rightElbowProxy = new SyncRef<Slot>(this, null);
        _leftFootProxy = new SyncRef<Slot>(this, null);
        _rightFootProxy = new SyncRef<Slot>(this, null);
        _leftKneeProxy = new SyncRef<Slot>(this, null);
        _rightKneeProxy = new SyncRef<Slot>(this, null);

        // Initialize pose node refs
        _headNode = new SyncRef<AvatarPoseNode>(this, null);
        _pelvisNode = new SyncRef<AvatarPoseNode>(this, null);
        _chestNode = new SyncRef<AvatarPoseNode>(this, null);
        _leftHandNode = new SyncRef<AvatarPoseNode>(this, null);
        _rightHandNode = new SyncRef<AvatarPoseNode>(this, null);
        _leftElbowNode = new SyncRef<AvatarPoseNode>(this, null);
        _rightElbowNode = new SyncRef<AvatarPoseNode>(this, null);
        _leftFootNode = new SyncRef<AvatarPoseNode>(this, null);
        _rightFootNode = new SyncRef<AvatarPoseNode>(this, null);
        _leftKneeNode = new SyncRef<AvatarPoseNode>(this, null);
        _rightKneeNode = new SyncRef<AvatarPoseNode>(this, null);

        // Initialize field drives
        _headTargetPos = new FieldDrive<float3>(World);
        _headTargetRot = new FieldDrive<floatQ>(World);
        _pelvisTargetPos = new FieldDrive<float3>(World);
        _pelvisTargetRot = new FieldDrive<floatQ>(World);
        _leftHandTargetPos = new FieldDrive<float3>(World);
        _leftHandTargetRot = new FieldDrive<floatQ>(World);
        _rightHandTargetPos = new FieldDrive<float3>(World);
        _rightHandTargetRot = new FieldDrive<floatQ>(World);
        _leftFootTargetPos = new FieldDrive<float3>(World);
        _leftFootTargetRot = new FieldDrive<floatQ>(World);
        _rightFootTargetPos = new FieldDrive<float3>(World);
        _rightFootTargetRot = new FieldDrive<floatQ>(World);

        AquaLogger.Log($"VRIKAvatar: OnAwake on slot '{Slot.SlotName.Value}'");
    }

    public override void OnStart()
    {
        base.OnStart();

        // Register for input updates
        var input = Engine.Current?.InputInterface;
        if (input != null)
        {
            input.RegisterInputEventReceiver(this);
            _isRegistered = true;
        }

        // Try to auto-find skeleton and rig
        if (Skeleton.Target == null)
        {
            Skeleton.Target = Slot.GetComponent<SkeletonBuilder>();
        }

        if (Rig.Target == null)
        {
            Rig.Target = Slot.GetComponent<BipedRig>();
        }

        // Try to auto-find UserRoot
        if (UserRoot.Target == null)
        {
            UserRoot.Target = Slot.GetComponent<UserRoot>();
            if (UserRoot.Target == null)
            {
                UserRoot.Target = Slot.Parent?.GetComponent<UserRoot>();
            }
        }

        _isInitialized = Skeleton.Target != null && Skeleton.Target.IsBuilt.Value;

        if (_isInitialized)
        {
            AquaLogger.Log($"VRIKAvatar: Started on slot '{Slot.SlotName.Value}' with skeleton");
        }
        else
        {
            AquaLogger.Warn($"VRIKAvatar: No valid skeleton found on slot '{Slot.SlotName.Value}'");
        }
    }

    public override void OnDestroy()
    {
        if (_isRegistered)
        {
            var input = Engine.Current?.InputInterface;
            input?.UnregisterInputEventReceiver(this);
            _isRegistered = false;
        }

        ReleaseAllDrives();
        _isInitialized = false;
        base.OnDestroy();
        AquaLogger.Log($"VRIKAvatar: Destroyed on slot '{Slot?.SlotName.Value}'");
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        // Late-init if skeleton was assigned after OnStart
        if (!_isInitialized && Skeleton.Target != null && Skeleton.Target.IsBuilt.Value)
        {
            _isInitialized = true;
            AquaLogger.Log($"VRIKAvatar: Late-start with skeleton");
        }

        // Register for hook update
        RunApplyChanges();
    }

    // ===== IInputUpdateReceiver =====

    public void BeforeInputUpdate()
    {
        if (!IKEnabled.Value || !_isInitialized)
            return;

        // Update proxy positions from tracking before IK solve
        UpdateProxiesFromTracking();
    }

    public void AfterInputUpdate()
    {
        if (!IKEnabled.Value || !_isInitialized)
            return;

        // Drive skeleton bones from proxy positions after IK solve
        DriveSkeletonFromProxies();
    }

    // ===== SETUP =====

    /// <summary>
    /// Setup the VRIKAvatar with references.
    /// Creates proxy slots and pose nodes for each tracked body part.
    /// </summary>
    public void Setup(SkeletonBuilder skeleton, BipedRig rig, UserRoot userRoot)
    {
        Skeleton.Target = skeleton;
        Rig.Target = rig;
        UserRoot.Target = userRoot;

        // Create proxy slots and pose nodes
        EnsurePoseNodes();

        // Setup field drives to skeleton bones
        AssignDrives();

        _isInitialized = true;
        AquaLogger.Log($"VRIKAvatar: Setup complete with {rig?.Bones.Count ?? 0} bones");
    }

    /// <summary>
    /// Setup tracking references from UserRoot.
    /// Finds the AvatarObjectSlot on each body node tracking slot and equips our
    /// corresponding AvatarPoseNode so it starts receiving tracking data.
    /// </summary>
    public void SetupTracking(UserRoot userRoot)
    {
        if (userRoot == null)
        {
            AquaLogger.Warn("VRIKAvatar: Cannot setup tracking with null UserRoot");
            return;
        }

        UserRoot.Target = userRoot;

        EquipPoseNodeToBodySlot(userRoot.HeadSlot,      _headNode.Target);
        EquipPoseNodeToBodySlot(userRoot.LeftHandSlot,  _leftHandNode.Target);
        EquipPoseNodeToBodySlot(userRoot.RightHandSlot, _rightHandNode.Target);
        EquipPoseNodeToBodySlot(userRoot.LeftFootSlot,  _leftFootNode.Target);
        EquipPoseNodeToBodySlot(userRoot.RightFootSlot, _rightFootNode.Target);

        AquaLogger.Log($"VRIKAvatar: Tracking connected for UserRoot '{userRoot.Slot.SlotName.Value}'");
    }

    /// <summary>
    /// Finds the AvatarObjectSlot on a body tracking slot and equips the given pose node to it.
    /// </summary>
    private static void EquipPoseNodeToBodySlot(Slot bodySlot, AvatarPoseNode poseNode)
    {
        if (bodySlot == null || poseNode == null) return;

        var avatarSlot = bodySlot.GetComponent<AvatarObjectSlot>();
        if (avatarSlot == null)
        {
            AquaLogger.Warn($"VRIKAvatar: No AvatarObjectSlot on body slot '{bodySlot.SlotName.Value}'");
            return;
        }

        avatarSlot.Equip(poseNode);
        AquaLogger.Log($"VRIKAvatar: Equipped {poseNode.Node.Value} pose node to '{bodySlot.SlotName.Value}'");
    }

    // ===== INTERNAL METHODS =====

    /// <summary>
    /// Ensure all pose nodes exist for tracked body parts.
    /// </summary>
    private void EnsurePoseNodes()
    {
        EnsurePoseNode(_headProxy, _headNode, BodyNode.Head, "Head");
        EnsurePoseNode(_pelvisProxy, _pelvisNode, BodyNode.Hips, "Hips");
        EnsurePoseNode(_chestProxy, _chestNode, BodyNode.Chest, "Chest");
        EnsurePoseNode(_leftHandProxy, _leftHandNode, BodyNode.LeftHand, "Left Hand");
        EnsurePoseNode(_rightHandProxy, _rightHandNode, BodyNode.RightHand, "Right Hand");
        EnsurePoseNode(_leftElbowProxy, _leftElbowNode, BodyNode.LeftLowerArm, "Left Elbow");
        EnsurePoseNode(_rightElbowProxy, _rightElbowNode, BodyNode.RightLowerArm, "Right Elbow");
        EnsurePoseNode(_leftFootProxy, _leftFootNode, BodyNode.LeftFoot, "Left Foot");
        EnsurePoseNode(_rightFootProxy, _rightFootNode, BodyNode.RightFoot, "Right Foot");
        EnsurePoseNode(_leftKneeProxy, _leftKneeNode, BodyNode.LeftLowerLeg, "Left Knee");
        EnsurePoseNode(_rightKneeProxy, _rightKneeNode, BodyNode.RightLowerLeg, "Right Knee");
    }

    /// <summary>
    /// Ensure a single pose node exists.
    /// </summary>
    private AvatarPoseNode EnsurePoseNode(SyncRef<Slot> proxy, SyncRef<AvatarPoseNode> poseNode, BodyNode node, string name)
    {
        // Create proxy slot if it doesn't exist
        if (proxy.Target == null)
        {
            var proxyParent = Slot.AddSlot($"{name} Proxy");
            proxy.Target = proxyParent.AddSlot("Target");
        }

        // Create pose node if it doesn't exist
        if (poseNode.Target == null)
        {
            poseNode.Target = proxy.Target.Parent.AttachComponent<AvatarPoseNode>();
        }

        // Set the body node type
        poseNode.Target.Node.Value = node;

        return poseNode.Target;
    }

    /// <summary>
    /// Assign field drives to skeleton bones.
    /// </summary>
    private void AssignDrives()
    {
        if (Rig.Target == null)
            return;

        // Drive head bone
        var headBone = Rig.Target.TryGetBone(BodyNode.Head);
        if (headBone != null)
        {
            _headTargetPos.DriveTarget(headBone.LocalPosition);
            _headTargetRot.DriveTarget(headBone.LocalRotation);
        }

        // Drive hips bone
        var hipsBone = Rig.Target.TryGetBone(BodyNode.Hips);
        if (hipsBone != null)
        {
            _pelvisTargetPos.DriveTarget(hipsBone.LocalPosition);
            _pelvisTargetRot.DriveTarget(hipsBone.LocalRotation);
        }

        // Drive left hand bone
        var leftHandBone = Rig.Target.TryGetBone(BodyNode.LeftHand);
        if (leftHandBone != null)
        {
            _leftHandTargetPos.DriveTarget(leftHandBone.LocalPosition);
            _leftHandTargetRot.DriveTarget(leftHandBone.LocalRotation);
        }

        // Drive right hand bone
        var rightHandBone = Rig.Target.TryGetBone(BodyNode.RightHand);
        if (rightHandBone != null)
        {
            _rightHandTargetPos.DriveTarget(rightHandBone.LocalPosition);
            _rightHandTargetRot.DriveTarget(rightHandBone.LocalRotation);
        }

        // Drive left foot bone
        var leftFootBone = Rig.Target.TryGetBone(BodyNode.LeftFoot);
        if (leftFootBone != null)
        {
            _leftFootTargetPos.DriveTarget(leftFootBone.LocalPosition);
            _leftFootTargetRot.DriveTarget(leftFootBone.LocalRotation);
        }

        // Drive right foot bone
        var rightFootBone = Rig.Target.TryGetBone(BodyNode.RightFoot);
        if (rightFootBone != null)
        {
            _rightFootTargetPos.DriveTarget(rightFootBone.LocalPosition);
            _rightFootTargetRot.DriveTarget(rightFootBone.LocalRotation);
        }

        AquaLogger.Log($"VRIKAvatar: Assigned drives to skeleton bones");
    }

    /// <summary>
    /// Release all field drives.
    /// </summary>
    private void ReleaseAllDrives()
    {
        _headTargetPos?.ReleaseLink();
        _headTargetRot?.ReleaseLink();
        _pelvisTargetPos?.ReleaseLink();
        _pelvisTargetRot?.ReleaseLink();
        _leftHandTargetPos?.ReleaseLink();
        _leftHandTargetRot?.ReleaseLink();
        _rightHandTargetPos?.ReleaseLink();
        _rightHandTargetRot?.ReleaseLink();
        _leftFootTargetPos?.ReleaseLink();
        _leftFootTargetRot?.ReleaseLink();
        _rightFootTargetPos?.ReleaseLink();
        _rightFootTargetRot?.ReleaseLink();
    }

    /// <summary>
    /// Update proxy slot positions from tracking data.
    /// Head/hand/foot proxies are driven by AvatarPoseNode.BeforeInputUpdate/AfterInputUpdate
    /// automatically once SetupTracking has called Equip(). We only need to handle the
    /// procedural-feet fallback here for when foot tracking is absent.
    /// </summary>
    private void UpdateProxiesFromTracking()
    {
        if (UseProceduralFeet.Value)
        {
            bool leftTracked  = _leftFootNode.Target?.IsEquippedAndActive  ?? false;
            bool rightTracked = _rightFootNode.Target?.IsEquippedAndActive ?? false;
            if (!leftTracked || !rightTracked)
                UpdateProceduralFeet();
        }
    }

    /// <summary>
    /// Drive skeleton bones from proxy positions.
    /// </summary>
    private void DriveSkeletonFromProxies()
    {
        // The FieldDrives automatically push values from proxy to skeleton
        // This method is where additional IK solving would happen

        // For now, directly copy proxy transforms to skeleton bones via drives
        if (_headProxy.Target != null && _headTargetPos.IsActive)
        {
            var headBone = Rig.Target?.TryGetBone(BodyNode.Head);
            if (headBone != null)
            {
                // Convert proxy global position to bone local position
                var globalPos = _headProxy.Target.GlobalPosition;
                var localPos = headBone.Parent?.GlobalPointToLocal(globalPos) ?? globalPos;
                _headTargetPos.SetValue(localPos);

                var globalRot = _headProxy.Target.GlobalRotation;
                var localRot = headBone.Parent?.GlobalRotationToLocal(globalRot) ?? globalRot;
                _headTargetRot.SetValue(localRot);
            }
        }

        // Left hand
        if (_leftHandProxy.Target != null && _leftHandTargetPos.IsActive)
        {
            var bone = Rig.Target?.TryGetBone(BodyNode.LeftHand);
            if (bone != null)
            {
                var globalPos = _leftHandProxy.Target.GlobalPosition;
                var localPos = bone.Parent?.GlobalPointToLocal(globalPos) ?? globalPos;
                _leftHandTargetPos.SetValue(localPos);

                var globalRot = _leftHandProxy.Target.GlobalRotation;
                var localRot = bone.Parent?.GlobalRotationToLocal(globalRot) ?? globalRot;
                _leftHandTargetRot.SetValue(localRot);
            }
        }

        // Right hand
        if (_rightHandProxy.Target != null && _rightHandTargetPos.IsActive)
        {
            var bone = Rig.Target?.TryGetBone(BodyNode.RightHand);
            if (bone != null)
            {
                var globalPos = _rightHandProxy.Target.GlobalPosition;
                var localPos = bone.Parent?.GlobalPointToLocal(globalPos) ?? globalPos;
                _rightHandTargetPos.SetValue(localPos);

                var globalRot = _rightHandProxy.Target.GlobalRotation;
                var localRot = bone.Parent?.GlobalRotationToLocal(globalRot) ?? globalRot;
                _rightHandTargetRot.SetValue(localRot);
            }
        }

        // Feet
        if (_leftFootProxy.Target != null && _leftFootTargetPos.IsActive)
        {
            var bone = Rig.Target?.TryGetBone(BodyNode.LeftFoot);
            if (bone != null)
            {
                var globalPos = _leftFootProxy.Target.GlobalPosition;
                var localPos = bone.Parent?.GlobalPointToLocal(globalPos) ?? globalPos;
                _leftFootTargetPos.SetValue(localPos);

                var globalRot = _leftFootProxy.Target.GlobalRotation;
                var localRot = bone.Parent?.GlobalRotationToLocal(globalRot) ?? globalRot;
                _leftFootTargetRot.SetValue(localRot);
            }
        }

        if (_rightFootProxy.Target != null && _rightFootTargetPos.IsActive)
        {
            var bone = Rig.Target?.TryGetBone(BodyNode.RightFoot);
            if (bone != null)
            {
                var globalPos = _rightFootProxy.Target.GlobalPosition;
                var localPos = bone.Parent?.GlobalPointToLocal(globalPos) ?? globalPos;
                _rightFootTargetPos.SetValue(localPos);

                var globalRot = _rightFootProxy.Target.GlobalRotation;
                var localRot = bone.Parent?.GlobalRotationToLocal(globalRot) ?? globalRot;
                _rightFootTargetRot.SetValue(localRot);
            }
        }
    }

    /// <summary>
    /// Update procedural foot positions when no foot tracking is available.
    /// Uses body-rotation-aware foot zones so feet stay under the body even when turning.
    /// </summary>
    private void UpdateProceduralFeet()
    {
        var headSlot = _headProxy.Target;
        if (headSlot == null) return;

        float3 headPos = headSlot.GlobalPosition;

        // Compute body forward direction (flatten Y, normalize)
        floatQ headRot = headSlot.GlobalRotation;
        float3 forward = headRot * float3.Backward; // Godot: -Z is forward
        forward.y = 0f;
        if (forward.LengthSquared < 0.001f) forward = float3.Backward;
        forward = forward.Normalized;

        floatQ bodyRot = floatQ.LookRotation(forward, float3.Up);
        float3 right = bodyRot * float3.Right;

        const float heightOffset   = -1.6f;
        const float footSeparation = 0.15f;

        if (_leftFootProxy.Target != null && !(_leftFootNode.Target?.IsEquippedAndActive ?? false))
        {
            float3 pos = headPos + new float3(0f, heightOffset, 0f) + (-right * footSeparation);
            // Convert to proxy's parent-local space
            var parent = _leftFootProxy.Target.Parent;
            _leftFootProxy.Target.LocalPosition.Value = parent != null
                ? parent.GlobalPointToLocal(pos)
                : pos;
        }

        if (_rightFootProxy.Target != null && !(_rightFootNode.Target?.IsEquippedAndActive ?? false))
        {
            float3 pos = headPos + new float3(0f, heightOffset, 0f) + (right * footSeparation);
            var parent = _rightFootProxy.Target.Parent;
            _rightFootProxy.Target.LocalPosition.Value = parent != null
                ? parent.GlobalPointToLocal(pos)
                : pos;
        }
    }

    /// <summary>
    /// Get diagnostic information about the avatar.
    /// </summary>
    public void LogDiagnosticInfo()
    {
        AquaLogger.Log($"VRIKAvatar Diagnostic Info:");
        AquaLogger.Log($"  IsInitialized: {_isInitialized}");
        AquaLogger.Log($"  IsEquipped: {IsEquipped}");
        AquaLogger.Log($"  Skeleton: {Skeleton.Target?.Slot.SlotName.Value ?? "null"}");
        AquaLogger.Log($"  Rig: {Rig.Target?.Bones.Count ?? 0} bones");
        AquaLogger.Log($"  UserRoot: {UserRoot.Target?.Slot.SlotName.Value ?? "null"}");
        AquaLogger.Log($"  HeadProxy: {_headProxy.Target?.SlotName.Value ?? "null"}");
        AquaLogger.Log($"  HeadNode Equipped: {_headNode.Target?.IsEquipped ?? false}");
        AquaLogger.Log($"  LeftHandNode Equipped: {_leftHandNode.Target?.IsEquipped ?? false}");
        AquaLogger.Log($"  RightHandNode Equipped: {_rightHandNode.Target?.IsEquipped ?? false}");
    }
}
