// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

﻿using System;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// VR IK avatar orchestrator.
///
/// Responsibilities:
///   1. Create one proxy slot per tracked body node (HeadProxy, LeftHandProxy, …).
///   2. Attach an AvatarPoseNode to each proxy slot and equip it to the matching
///      AvatarObjectSlot on the UserRoot's body-node tracking slots.
///      This makes the proxy slots automatically mirror the tracking positions.
///   3. Wire the proxy slots into GodotIKAvatar's target SyncRefs so the IK hook
///      knows where to aim each limb.
///   4. Run procedural feet when foot tracking is absent.
///
/// What this component does NOT do:
///   - Drive skeleton bones directly (GodotIKAvatar + GodotIKAvatarHook own that).
///   - Run any IK solver (GodotIKAvatarHook does that on the Godot side).
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
[DefaultUpdateOrder(-5000)]
public class VRIKAvatar : ImplementableComponent, IAvatarObjectComponent, IInputUpdateReceiver
{
    // ── References ──────────────────────────────────────────────────────────

    public readonly SyncRef<SkeletonBuilder> Skeleton;
    public readonly SyncRef<BipedRig>        Rig;
    public readonly SyncRef<UserRoot>        UserRoot;

    // ── Settings ─────────────────────────────────────────────────────────────

    public readonly Sync<float> HeightCompensation;
    public readonly Sync<float> AvatarHeight;
    public readonly Sync<float> UserResizeThreshold;
    public readonly Sync<bool>  UseProceduralFeet;
    public readonly Sync<bool>  IKEnabled;

    // ── Proxy slots (one per tracked body node) ──────────────────────────────
    // Each proxy slot's global position is kept in sync with the corresponding
    // tracking slot by the AvatarPoseNode attached to it.

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

    // ── Pose nodes (one per proxy, equips to tracking AvatarObjectSlot) ──────

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

    // ── Internal state ────────────────────────────────────────────────────────

    private bool _isInitialized;
    private bool _isRegistered;

    // ── Public accessors ──────────────────────────────────────────────────────

    public bool            IsEquipped     => _headNode?.Target?.IsEquipped ?? false;
    public AvatarPoseNode? HeadNode       => _headNode?.Target;
    public AvatarPoseNode? PelvisNode     => _pelvisNode?.Target;
    public AvatarPoseNode? LeftHandNode   => _leftHandNode?.Target;
    public AvatarPoseNode? RightHandNode  => _rightHandNode?.Target;
    public AvatarPoseNode? LeftFootNode   => _leftFootNode?.Target;
    public AvatarPoseNode? RightFootNode  => _rightFootNode?.Target;
    public Slot?           HeadProxy      => _headProxy?.Target;
    public Slot?           LeftHandProxy  => _leftHandProxy?.Target;
    public Slot?           RightHandProxy => _rightHandProxy?.Target;

    // ── IAvatarObjectComponent ────────────────────────────────────────────────

    public BodyNode Node => BodyNode.Root;
    public void OnPreEquip(AvatarObjectSlot slot) { }
    public void OnEquip(AvatarObjectSlot slot)    { LumoraLogger.Log($"VRIKAvatar: Equipped to {slot.Node.Value}"); }
    public void OnDequip(AvatarObjectSlot slot)   { LumoraLogger.Log($"VRIKAvatar: Dequipped from {slot.Node.Value}"); }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnInit()
    {
        base.OnInit();
        HeightCompensation.Value  = 0.95f;
        AvatarHeight.Value        = 1.7f;
        UserResizeThreshold.Value = 0.2f;
        UseProceduralFeet.Value   = true;
        IKEnabled.Value           = true;
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
            Skeleton.Target = Slot.GetComponent<SkeletonBuilder>();

        if (Rig.Target == null)
            Rig.Target = Slot.GetComponent<BipedRig>();

        if (UserRoot.Target == null)
        {
            var ur = Slot.GetComponent<UserRoot>();
            if (ur == null && Slot.Parent != null)
                ur = Slot.Parent.GetComponent<UserRoot>();
            if (ur != null)
                UserRoot.Target = ur;
        }

        _isInitialized = Skeleton.Target != null && Skeleton.Target.IsBuilt.Value;

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

        // Late-init if skeleton was assigned after OnStart.
        if (!_isInitialized && Skeleton.Target != null && Skeleton.Target.IsBuilt.Value)
        {
            _isInitialized = true;
            LumoraLogger.Log("VRIKAvatar: Late-start with skeleton");
        }

        RunApplyChanges();
    }

    // ── IInputUpdateReceiver ──────────────────────────────────────────────────

    public void BeforeInputUpdate()
    {
        if (!IKEnabled.Value || !_isInitialized) return;

        // AvatarPoseNodes self-update via their own IInputUpdateReceiver registration.
        // We only need to handle procedural feet for untracked foot nodes.
        if (UseProceduralFeet.Value)
        {
            bool leftTracked  = _leftFootNode.Target?.IsEquippedAndActive  ?? false;
            bool rightTracked = _rightFootNode.Target?.IsEquippedAndActive ?? false;
            if (!leftTracked || !rightTracked)
                UpdateProceduralFeet();
        }
    }

    public void AfterInputUpdate()
    {
        // Bone driving is handled by GodotIKAvatar + GodotIKAvatarHook.
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full avatar setup — creates proxy slots, wires them to GodotIKAvatar,
    /// and stores skeleton/rig/userRoot references.
    /// </summary>
    public void Setup(SkeletonBuilder skeleton, BipedRig rig, UserRoot userRoot)
    {
        Skeleton.Target  = skeleton;
        Rig.Target       = rig;
        UserRoot.Target  = userRoot;

        EnsurePoseNodes();
        WireToGodotIK();

        _isInitialized = true;
        LumoraLogger.Log($"VRIKAvatar: Setup complete ({rig?.Bones.Count ?? 0} bones)");
    }

    /// <summary>
    /// Connect tracking: find the AvatarObjectSlot on each UserRoot body slot and
    /// equip our AvatarPoseNode to it so the proxy slots start receiving tracking data.
    /// </summary>
    public void SetupTracking(UserRoot userRoot)
    {
        if (userRoot == null)
        {
            LumoraLogger.Warn("VRIKAvatar: Cannot setup tracking — null UserRoot");
            return;
        }

        UserRoot.Target = userRoot;
        EnsurePoseNodes();

        EquipPoseNodeToBodySlot(userRoot.HeadSlot,      _headNode.Target);
        EquipPoseNodeToBodySlot(userRoot.LeftHandSlot,  _leftHandNode.Target);
        EquipPoseNodeToBodySlot(userRoot.RightHandSlot, _rightHandNode.Target);
        EquipPoseNodeToBodySlot(userRoot.LeftFootSlot,  _leftFootNode.Target);
        EquipPoseNodeToBodySlot(userRoot.RightFootSlot, _rightFootNode.Target);

        WireToGodotIK();
        LumoraLogger.Log($"VRIKAvatar: Tracking connected for UserRoot '{userRoot.Slot.SlotName.Value}'");
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Create proxy slots and AvatarPoseNodes for every tracked body node.
    /// Each proxy is a single flat slot; the AvatarPoseNode sits on it and updates
    /// the slot's local transform each frame once equipped.
    /// </summary>
    private void EnsurePoseNodes()
    {
        EnsurePoseNode(_headProxy,       _headNode,       BodyNode.Head,         "Head");
        EnsurePoseNode(_pelvisProxy,     _pelvisNode,     BodyNode.Hips,         "Hips");
        EnsurePoseNode(_chestProxy,      _chestNode,      BodyNode.Chest,        "Chest");
        EnsurePoseNode(_leftHandProxy,   _leftHandNode,   BodyNode.LeftHand,     "LeftHand");
        EnsurePoseNode(_rightHandProxy,  _rightHandNode,  BodyNode.RightHand,    "RightHand");
        EnsurePoseNode(_leftElbowProxy,  _leftElbowNode,  BodyNode.LeftLowerArm, "LeftElbow");
        EnsurePoseNode(_rightElbowProxy, _rightElbowNode, BodyNode.RightLowerArm,"RightElbow");
        EnsurePoseNode(_leftFootProxy,   _leftFootNode,   BodyNode.LeftFoot,     "LeftFoot");
        EnsurePoseNode(_rightFootProxy,  _rightFootNode,  BodyNode.RightFoot,    "RightFoot");
        EnsurePoseNode(_leftKneeProxy,   _leftKneeNode,   BodyNode.LeftLowerLeg, "LeftKnee");
        EnsurePoseNode(_rightKneeProxy,  _rightKneeNode,  BodyNode.RightLowerLeg,"RightKnee");
    }

    /// <summary>
    /// Ensure one proxy slot + AvatarPoseNode exist for a body node.
    /// The proxy slot IS the IK target — AvatarPoseNode lives on it and moves it.
    /// </summary>
    private AvatarPoseNode EnsurePoseNode(SyncRef<Slot> proxy, SyncRef<AvatarPoseNode> poseNode,
                                          BodyNode node, string name)
    {
        if (proxy.Target == null)
            proxy.Target = Slot.AddSlot($"{name}Proxy");

        if (poseNode.Target == null)
            poseNode.Target = proxy.Target.AttachComponent<AvatarPoseNode>();

        poseNode.Target.Node.Value = node;
        return poseNode.Target;
    }

    /// <summary>
    /// Find the AvatarObjectSlot belonging to a body-node tracking slot.
    ///
    /// TrackedDevicePositioner creates its AvatarObjectSlot on a child "BodyNode" slot,
    /// so we check via the positioner's ObjectSlot reference first before falling back
    /// to a component search.
    /// </summary>
    private static void EquipPoseNodeToBodySlot(Slot bodySlot, AvatarPoseNode poseNode)
    {
        if (bodySlot == null || poseNode == null) return;

        // 1. Direct — slot may have AvatarObjectSlot on itself.
        var avatarSlot = bodySlot.GetComponent<AvatarObjectSlot>();

        // 2. Via TrackedDevicePositioner — the positioner stores a reference to the
        //    AvatarObjectSlot it created on its "BodyNode" child slot.
        if (avatarSlot == null)
            avatarSlot = bodySlot.GetComponent<TrackedDevicePositioner>()?.ObjectSlot.Target;

        // 3. Last resort — scan children.
        if (avatarSlot == null)
            avatarSlot = bodySlot.GetComponentInChildren<AvatarObjectSlot>();

        if (avatarSlot == null)
        {
            LumoraLogger.Warn($"VRIKAvatar: No AvatarObjectSlot found for body slot '{bodySlot.SlotName.Value}'");
            return;
        }

        avatarSlot.Equip(poseNode);
        LumoraLogger.Log($"VRIKAvatar: Equipped {poseNode.Node.Value} pose node → '{bodySlot.SlotName.Value}'");
    }

    /// <summary>
    /// Set GodotIKAvatar's IK target SyncRefs to our proxy slots so the Godot-side
    /// hook knows exactly where to aim each limb.
    /// </summary>
    private void WireToGodotIK()
    {
        // Search up the hierarchy for a GodotIKAvatar.
        var godotIK = Slot.GetComponent<GodotIKAvatar>();
        if (godotIK == null)
        {
            var p = Slot.Parent;
            while (p != null && godotIK == null)
            {
                godotIK = p.GetComponent<GodotIKAvatar>();
                p = p.Parent;
            }
        }

        if (godotIK == null)
        {
            LumoraLogger.Warn("VRIKAvatar: No GodotIKAvatar found in hierarchy — IK targets will not be set");
            return;
        }

        if (_headProxy.Target != null)       godotIK.HeadTarget.Target      = _headProxy.Target;
        if (_leftHandProxy.Target != null)   godotIK.LeftHandTarget.Target  = _leftHandProxy.Target;
        if (_rightHandProxy.Target != null)  godotIK.RightHandTarget.Target = _rightHandProxy.Target;
        if (_leftFootProxy.Target != null)   godotIK.LeftFootTarget.Target  = _leftFootProxy.Target;
        if (_rightFootProxy.Target != null)  godotIK.RightFootTarget.Target = _rightFootProxy.Target;

        LumoraLogger.Log($"VRIKAvatar: Wired proxy slots → GodotIKAvatar on '{godotIK.Slot.SlotName.Value}'");
    }

    /// <summary>
    /// Compute procedural foot positions from head transform when foot tracking is absent.
    /// Uses body-rotation-aware positioning so feet stay under the body when turning.
    /// </summary>
    private void UpdateProceduralFeet()
    {
        var headSlot = _headProxy.Target;
        if (headSlot == null) return;

        float3 headPos = headSlot.GlobalPosition;
        float3 forward = headSlot.GlobalRotation * float3.Backward; // Godot: -Z forward
        forward.y = 0f;
        if (forward.LengthSquared < 0.001f) forward = float3.Backward;
        forward = forward.Normalized;

        floatQ bodyRot = floatQ.LookRotation(forward, float3.Up);
        float3 right   = bodyRot * float3.Right;

        const float heightOffset   = -1.6f;
        const float footSeparation = 0.15f;

        SetProceduralFoot(_leftFootProxy.Target,  _leftFootNode.Target,
            headPos + new float3(0f, heightOffset, 0f) - right * footSeparation);

        SetProceduralFoot(_rightFootProxy.Target, _rightFootNode.Target,
            headPos + new float3(0f, heightOffset, 0f) + right * footSeparation);
    }

    private static void SetProceduralFoot(Slot proxy, AvatarPoseNode node, float3 worldPos)
    {
        if (proxy == null || (node?.IsEquippedAndActive ?? false)) return;
        var parent = proxy.Parent;
        proxy.LocalPosition.Value = parent != null
            ? parent.GlobalPointToLocal(worldPos)
            : worldPos;
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

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
