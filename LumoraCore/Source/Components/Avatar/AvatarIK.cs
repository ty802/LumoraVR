// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
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
public class AvatarIK : Component, IAvatarObjectComponent, IInputUpdateReceiver
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

    public readonly Sync<float> HeightCompensation = null!;
    public readonly Sync<float> AvatarHeight = null!;
    public readonly Sync<float> UserResizeThreshold = null!;
    public readonly Sync<bool> UseProceduralFeet = null!;
    public readonly Sync<bool> IKEnabled = null!;

    // IK tunables - exposed so feel can be dialed in live in-world (no rebuild).
    public readonly Sync<float> SpineStiffness = null!;
    public readonly Sync<float> PelvisDamping = null!;
    public readonly Sync<float> ShoulderReach = null!;
    public readonly Sync<float> ArmStretch = null!;
    public readonly Sync<float> BendGoalWeight = null!;
    public readonly Sync<float> TwistRelax = null!;
    public readonly Sync<float> HandIKWeight = null!;   // per-effector IK weight for both hands
    public readonly Sync<float> FootIKWeight = null!;   // per-effector IK weight for both feet
    public readonly Sync<float> FootStanceWidth = null!;
    public readonly Sync<float> StepThreshold = null!;
    public readonly Sync<float> StepDuration = null!;
    public readonly Sync<float> StepHeight = null!;
    public readonly Sync<float> StepPrediction = null!; // seconds of velocity lookahead per step
    public readonly Sync<float> BodyBob = null!;        // vertical hip dip while a foot is airborne

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
    private bool _suspendSolve;
    private ReferenceOffset _viewOffset;
    private ReferenceOffset _leftHandOffset;
    private ReferenceOffset _rightHandOffset;
    private ReferenceOffset _leftFootOffset;
    private ReferenceOffset _rightFootOffset;

    // Procedural locomotion (stepping gait) state for untracked feet.
    private struct FootGait
    {
        public bool Init;
        public bool Stepping;
        public float Progress;
        public float3 Planted;
        public float3 StepStart;
    }
    private FootGait _leftGait;
    private FootGait _rightGait;
    private long _lastGaitTick;
    private const float GroundProbeUp = 0.5f;      // foot ground ray starts this far above the plane
    private const float GroundProbeDown = 1.0f;    // ...and reaches this far below it
    private readonly List<Slot> _footRayExclude = new(1);
    private float3 _leftFootGroundNormal = float3.Up;
    private float3 _rightFootGroundNormal = float3.Up;
    private float3 _prevHeadXZ;          // for step-prediction velocity
    private bool _hasPrevHead;
    private float _leftStepLift;         // 0..1 current step arc height per foot
    private float _rightStepLift;
    private float3 _locomotionOffset;    // hips bob fed to the solver

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
        // Equip reparents and rescales the avatar, so the solver's captured
        // rest pose - world-space bone lengths and rest directions - is stale.
        // Arms solved against pre-equip lengths can't reach the real targets
        // (hands lag/never lift). Suspend solving, then re-capture once the
        // transforms have settled.
        _suspendSolve = true;
        Slot.RunInUpdates(1, () =>
        {
            if (IsDestroyed) return;
            EnsureSolver();
            RecomputeReferenceOffsets();
            _suspendSolve = false;
        });
        LumoraLogger.Log($"AvatarIK: Equipped to {slot.Node.Value}");
    }

    public void OnDequip(AvatarObjectSlot slot)
    {
        LumoraLogger.Log($"AvatarIK: Dequipped from {slot.Node.Value}");
    }

    private readonly UserRootRegistrationTracker _userRootReg;

    public AvatarIK()
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

        SpineStiffness.Value = 0.2f;
        PelvisDamping.Value = 0.65f;
        ShoulderReach.Value = 0.45f;
        ArmStretch.Value = 1.08f;
        BendGoalWeight.Value = 0.5f;
        TwistRelax.Value = 0.5f;
        HandIKWeight.Value = 1f;
        FootIKWeight.Value = 1f;
        FootStanceWidth.Value = 0.12f;
        StepThreshold.Value = 0.18f;
        StepDuration.Value = 0.22f;
        StepHeight.Value = 0.08f;
        StepPrediction.Value = 0.08f;
        BodyBob.Value = 0.02f;
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
            LumoraLogger.Log($"AvatarIK: Started on '{Slot.SlotName.Value}' with skeleton");
        else
            LumoraLogger.Warn($"AvatarIK: No valid skeleton found on '{Slot.SlotName.Value}'");
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
            LumoraLogger.Log("AvatarIK: Late-start with skeleton");
        }
    }

    public void BeforeInputUpdate()
    {
        if (!IKEnabled.Value || !_isInitialized)
            return;

        // Run every frame so the gait clock stays continuous; per-foot tracked checks live inside.
        if (UseProceduralFeet.Value)
            UpdateProceduralFeet();
    }

    public void AfterInputUpdate()
    {
        if (!IKEnabled.Value || !_isInitialized || _suspendSolve)
            return;

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

        // Push live tunables into the solver.
        _solver.SpineStiffness = SpineStiffness.Value;
        _solver.PelvisDamp = PelvisDamping.Value;
        _solver.ShoulderWeight = ShoulderReach.Value;
        _solver.MaxStretch = ArmStretch.Value;
        _solver.BendGoalWeight = BendGoalWeight.Value;
        _solver.TwistRelax = TwistRelax.Value;
        _solver.LocomotionOffset = _locomotionOffset;

        var head = ProxyTarget(_headProxy.Target, _viewOffset);

        // Pelvis target only counts when a waist/hip tracker is actually equipped; otherwise the
        // solver estimates the hips from the head (no calibration offset on the pelvis).
        var pelvis = (_pelvisNode.Target?.IsEquippedAndActive ?? false)
            ? ProxyTarget(_pelvisProxy.Target, default)
            : default;

        float handW = System.Math.Clamp(HandIKWeight.Value, 0f, 1f);
        var leftHandT = ProxyTarget(_leftHandProxy.Target, _leftHandOffset);
        var rightHandT = ProxyTarget(_rightHandProxy.Target, _rightHandOffset);
        leftHandT.PositionWeight = leftHandT.RotationWeight = handW;
        rightHandT.PositionWeight = rightHandT.RotationWeight = handW;
        // Equipped elbow trackers steer the arm bend.
        ApplyBendGoal(ref leftHandT, _leftElbowProxy, _leftElbowNode);
        ApplyBendGoal(ref rightHandT, _rightElbowProxy, _rightElbowNode);

        float footW = System.Math.Clamp(FootIKWeight.Value, 0f, 1f);
        var leftFootT = ProxyTarget(_leftFootProxy.Target, _leftFootOffset);
        var rightFootT = ProxyTarget(_rightFootProxy.Target, _rightFootOffset);
        leftFootT.PositionWeight = leftFootT.RotationWeight = footW;
        rightFootT.PositionWeight = rightFootT.RotationWeight = footW;
        // Equipped knee trackers steer the leg bend.
        ApplyBendGoal(ref leftFootT, _leftKneeProxy, _leftKneeNode);
        ApplyBendGoal(ref rightFootT, _rightKneeProxy, _rightKneeNode);

        // Untracked feet are ground-aligned (rest pose tilted to the surface normal the gait probed)
        // and carry the step lift so the solver can curl the toe; tracked feet keep tracker rotation.
        if (UseProceduralFeet.Value)
        {
            if (!(_leftFootNode.Target?.IsEquippedAndActive ?? false))
            {
                leftFootT.GroundAlign = true;
                leftFootT.GroundNormal = _leftFootGroundNormal;
                leftFootT.StepLift = _leftStepLift;
            }
            if (!(_rightFootNode.Target?.IsEquippedAndActive ?? false))
            {
                rightFootT.GroundAlign = true;
                rightFootT.GroundNormal = _rightFootGroundNormal;
                rightFootT.StepLift = _rightStepLift;
            }
        }

        _solver.Solve(head, pelvis, leftHandT, rightHandT, leftFootT, rightFootT);
    }

    // If the bend-goal's pose node is tracked (e.g. an elbow/knee tracker), feed its proxy position
    // to the solver so the limb bends toward it.
    private static void ApplyBendGoal(ref FullBodyIKSolver.Target target, SyncRef<Slot> proxy, SyncRef<AvatarPoseNode> node)
    {
        if ((node.Target?.IsEquippedAndActive ?? false) && proxy.Target != null && !proxy.Target.IsDestroyed)
        {
            target.HasBendGoal = true;
            target.BendGoal = proxy.Target.GlobalPosition;
        }
    }

    // Calibration offsets are folded into the TARGET, never written back to
    // the proxy: the proxies are driven by pose nodes every frame, and a
    // second writer would both fight the drives and generate sync churn.
    private static FullBodyIKSolver.Target ProxyTarget(Slot proxy, in ReferenceOffset offset)
    {
        if (proxy == null || proxy.IsDestroyed)
            return default;

        var position = proxy.GlobalPosition;
        var rotation = proxy.GlobalRotation;

        if (offset.Valid)
        {
            var adjustedRotation = rotation * offset.LocalRotation.Inverse;
            position -= adjustedRotation * offset.LocalPosition;
            rotation = adjustedRotation;
        }

        return new FullBodyIKSolver.Target
        {
            Valid = true,
            Position = position,
            Rotation = rotation,
            PositionWeight = 1f,
            RotationWeight = 1f,
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
        LumoraLogger.Log($"AvatarIK: Setup complete ({rig?.Bones.Count ?? 0} bones)");
    }

    /// <summary>
    /// Full avatar setup discovering skeleton and rig from the avatar tree.
    /// An avatar is self-describing: its components ARE the metadata.
    /// </summary>
    public bool SetupFromAvatar(UserRoot userRoot)
    {
        var skeleton = Skeleton.Target ?? Slot.GetComponent<SkeletonBuilder>() ?? Slot.GetComponentInChildren<SkeletonBuilder>();
        var rig = Rig.Target ?? Slot.GetComponent<BipedRig>() ?? Slot.GetComponentInChildren<BipedRig>();
        if (skeleton == null || rig == null)
        {
            LumoraLogger.Warn("AvatarIK: Avatar tree has no skeleton or rig");
            return false;
        }

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
            LumoraLogger.Warn("AvatarIK: Cannot setup tracking - null UserRoot");
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
        LumoraLogger.Log($"AvatarIK: Tracking connected for UserRoot '{userRoot.Slot.SlotName.Value}'");
    }

    /// <summary>
    /// Build coarse body colliders for a worn avatar: a sphere on the head and a capsule along each limb segment
    /// + the torso, so grab/interaction raycasts have something to hit. Idempotent-ish - skips a segment if a
    /// "BodyCollider" child already exists on the proximal bone. Each capsule lives on a child slot oriented so
    /// its local Y runs down the bone (the capsule shape is Y-aligned). -xlinka
    /// </summary>
    public void GenerateBodyColliders()
    {
        var rig = Rig.Target;
        if (rig == null)
            return;

        // Head sphere, sized from the neck-to-head length when available.
        var head = rig.TryGetBone(BodyNode.Head);
        if (head != null && head.GetComponentInChildren<SphereCollider>() == null)
        {
            float r = 0.1f;
            var neck = rig.TryGetBone(BodyNode.Neck) ?? rig.TryGetBone(BodyNode.Chest);
            if (neck != null)
            {
                var d = head.GlobalPosition - neck.GlobalPosition;
                float len = (float)System.Math.Sqrt(d.LengthSquared);
                if (len > 1e-4f) r = System.Math.Clamp(len * 0.6f, 0.05f, 0.2f);
            }
            var s = head.AddSlot("BodyCollider");
            s.AttachComponent<SphereCollider>().Radius.Value = r;
        }

        // Limb capsules (ratio = radius / segment length).
        AddLimbCapsule(rig, BodyNode.LeftUpperArm, BodyNode.LeftLowerArm, 0.12f);
        AddLimbCapsule(rig, BodyNode.LeftLowerArm, BodyNode.LeftHand, 0.10f);
        AddLimbCapsule(rig, BodyNode.RightUpperArm, BodyNode.RightLowerArm, 0.12f);
        AddLimbCapsule(rig, BodyNode.RightLowerArm, BodyNode.RightHand, 0.10f);
        AddLimbCapsule(rig, BodyNode.LeftUpperLeg, BodyNode.LeftLowerLeg, 0.14f);
        AddLimbCapsule(rig, BodyNode.LeftLowerLeg, BodyNode.LeftFoot, 0.12f);
        AddLimbCapsule(rig, BodyNode.RightUpperLeg, BodyNode.RightLowerLeg, 0.14f);
        AddLimbCapsule(rig, BodyNode.RightLowerLeg, BodyNode.RightFoot, 0.12f);

        // Torso capsule from the hips up to the highest available spine/neck bone.
        var torsoTop = rig.TryGetBone(BodyNode.Neck) ?? rig.TryGetBone(BodyNode.UpperChest)
            ?? rig.TryGetBone(BodyNode.Chest) ?? rig.TryGetBone(BodyNode.Spine);
        var hips = rig.TryGetBone(BodyNode.Hips);
        if (hips != null && torsoTop != null)
            AddCapsuleBetween(hips, torsoTop, 0.16f);

        LumoraLogger.Log("AvatarIK: Generated body colliders.");
    }

    private static void AddLimbCapsule(BipedRig rig, BodyNode proximal, BodyNode distal, float radiusRatio)
    {
        var p = rig.TryGetBone(proximal);
        var c = rig.TryGetBone(distal);
        if (p != null && c != null)
            AddCapsuleBetween(p, c, radiusRatio);
    }

    // Attach a Y-aligned capsule spanning two bone slots, on a child of the proximal slot rotated so its local Y
    // runs from proximal to distal. Skips if a "BodyCollider" child already sits on the proximal bone.
    private static void AddCapsuleBetween(Slot proximal, Slot distal, float radiusRatio)
    {
        foreach (var existing in proximal.Children)
            if (existing.SlotName.Value == "BodyCollider")
                return;

        var pg = proximal.GlobalPosition;
        var cg = distal.GlobalPosition;
        var dir = cg - pg;
        float len = (float)System.Math.Sqrt(dir.LengthSquared);
        if (len < 1e-4f)
            return;

        var mid = new float3((pg.x + cg.x) * 0.5f, (pg.y + cg.y) * 0.5f, (pg.z + cg.z) * 0.5f);
        var s = proximal.AddSlot("BodyCollider");
        s.GlobalPosition = mid;
        s.GlobalRotation = RotateUpTo(dir.Normalized);

        var cap = s.AttachComponent<CapsuleCollider>();
        cap.Radius.Value = System.Math.Clamp(len * radiusRatio, 0.02f, 0.5f);
        cap.Height.Value = len;
    }

    // Rotation taking the capsule's default up (+Y) onto a target unit direction.
    private static floatQ RotateUpTo(float3 to)
    {
        float3 from = float3.Up;
        float d = float3.Dot(from, to);
        if (d >= 0.99999f) return floatQ.Identity;
        if (d <= -0.99999f) return floatQ.AxisAngle(float3.Right, MathF.PI); // AxisAngle is RADIANS, not degrees
        float3 axis = float3.Cross(from, to).Normalized;
        float ang = (float)System.Math.Acos(System.Math.Clamp(d, -1f, 1f));
        return floatQ.AxisAngleRad(axis, ang);
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
            LumoraLogger.Warn($"AvatarIK: No AvatarObjectSlot found for body slot '{bodySlot.SlotName.Value}'");
            return;
        }

        var dequippedObjects = new HashSet<IAvatarObject>();
        if (!avatarSlot.PreEquip(poseNode, dequippedObjects))
        {
            LumoraLogger.Warn($"AvatarIK: Failed to prepare {poseNode.Node.Value} for '{bodySlot.SlotName.Value}'");
            return;
        }

        avatarSlot.Equip(poseNode);
        LumoraLogger.Log($"AvatarIK: Equipped {poseNode.Node.Value} pose node -> '{bodySlot.SlotName.Value}'");
    }

    // Calibration is read straight from AvatarReferencePoint components in
    // the avatar tree (placed by the creator flow) - the data IS the
    // components, no metadata sidecar.
    private void RecomputeReferenceOffsets()
    {
        _viewOffset = default;
        _leftHandOffset = default;
        _rightHandOffset = default;
        _leftFootOffset = default;
        _rightFootOffset = default;

        var rig = Rig.Target;
        if (rig == null)
            return;

        Slot? view = null, leftHand = null, rightHand = null, leftFoot = null, rightFoot = null;
        foreach (var point in Slot.GetComponentsInChildren<AvatarReferencePoint>())
        {
            switch (point.Kind.Value)
            {
                case AvatarReferenceKind.View: view ??= point.Slot; break;
                case AvatarReferenceKind.LeftHandGrip: leftHand ??= point.Slot; break;
                case AvatarReferenceKind.RightHandGrip: rightHand ??= point.Slot; break;
                case AvatarReferenceKind.LeftFoot: leftFoot ??= point.Slot; break;
                case AvatarReferenceKind.RightFoot: rightFoot ??= point.Slot; break;
            }
        }

        TryComputeReferenceOffset(rig.TryGetBone(BodyNode.Head), view!, ref _viewOffset);
        TryComputeReferenceOffset(rig.TryGetBone(BodyNode.LeftHand), leftHand!, ref _leftHandOffset);
        TryComputeReferenceOffset(rig.TryGetBone(BodyNode.RightHand), rightHand!, ref _rightHandOffset);
        TryComputeReferenceOffset(rig.TryGetBone(BodyNode.LeftFoot), leftFoot!, ref _leftFootOffset);
        TryComputeReferenceOffset(rig.TryGetBone(BodyNode.RightFoot), rightFoot!, ref _rightFootOffset);
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

    // Procedural locomotion: an untracked foot stays PLANTED, then STEPS (lerp with a
    // lift arc) to a new stance position when the body strays past a threshold - one foot at a time
    // so it reads as alternating steps, not sliding or teleporting. Feet anchor to the user-root
    // ground plane (crouch/head-bob must not lift them). Every peer derives this from the replicated
    // head/root, so the writes are local - broadcasting would just be churn.
    private void UpdateProceduralFeet()
    {
        var headSlot = _headProxy.Target;
        if (headSlot == null)
            return;

        long now = Environment.TickCount64;
        float dt = _lastGaitTick == 0 ? 0f : (now - _lastGaitTick) / 1000f;
        _lastGaitTick = now;
        dt = MathF.Min(MathF.Max(dt, 0f), 0.1f);

        float3 headPos = headSlot.GlobalPosition;
        float3 forward = headSlot.GlobalRotation * float3.Backward;
        forward.y = 0f;
        forward = forward.LengthSquared < 1e-4f ? float3.Backward : forward.Normalized;

        // Right via cross, not floatQ.LookRotation (which returns an inverse - see FaceLocalUser).
        float3 right = float3.Cross(float3.Up, forward);
        right = right.LengthSquared < 1e-6f ? float3.Right : right.Normalized;

        float groundY = UserRoot.Target?.Slot != null
            ? UserRoot.Target.Slot.GlobalPosition.y
            : headPos.y - 1.6f;
        float3 center = new float3(headPos.x, groundY, headPos.z);

        // Step prediction: shift the stance toward where the body is heading so feet land ahead of
        // movement instead of dragging behind it.
        float3 headXZ = new float3(headPos.x, 0f, headPos.z);
        float3 velocity = (_hasPrevHead && dt > 1e-4f) ? (headXZ - _prevHeadXZ) / dt : default;
        _prevHeadXZ = headXZ;
        _hasPrevHead = true;
        float3 predict = velocity * MathF.Max(StepPrediction.Value, 0f);

        // Refine each foot's plant height against the surface under it (terrain/stairs), probing past
        // this avatar's own colliders. Falls back to the flat user-root plane on a miss.
        float half = FootStanceWidth.Value;
        float3 leftDesired = center - right * half + predict;
        float3 rightDesired = center + right * half + predict;
        leftDesired.y = GroundHeight(leftDesired.x, leftDesired.z, groundY, out _leftFootGroundNormal);
        rightDesired.y = GroundHeight(rightDesired.x, rightDesired.z, groundY, out _rightFootGroundNormal);

        bool leftTracked = _leftFootNode.Target?.IsEquippedAndActive ?? false;
        bool rightTracked = _rightFootNode.Target?.IsEquippedAndActive ?? false;
        _leftStepLift = leftTracked ? 0f : StepFoot(ref _leftGait, leftDesired, _rightGait.Stepping, dt, _leftFootProxy.Target);
        _rightStepLift = rightTracked ? 0f : StepFoot(ref _rightGait, rightDesired, _leftGait.Stepping, dt, _rightFootProxy.Target);

        // Weight-shift bob: dip the hips slightly while a foot is airborne.
        float lift = MathF.Max(_leftStepLift, _rightStepLift);
        _locomotionOffset = new float3(0f, -BodyBob.Value * lift, 0f);
    }

    // Downward physics probe for the floor under a foot, ignoring this avatar's own colliders so the
    // ray doesn't catch its capsule. Returns the surface height, or fallbackY on a miss (ledge / gap
    // / no query hook).
    private float GroundHeight(float x, float z, float fallbackY, out float3 normal)
    {
        normal = float3.Up;
        var world = World;
        var userSlot = UserRoot.Target?.Slot;
        if (world == null || userSlot == null)
            return fallbackY;

        _footRayExclude.Clear();
        _footRayExclude.Add(userSlot);

        var origin = new float3(x, fallbackY + GroundProbeUp, z);
        var hit = world.Physics.Raycast(origin, new float3(0f, -1f, 0f), _footRayExclude, GroundProbeUp + GroundProbeDown);
        if (!hit.HasValue)
            return fallbackY;

        normal = hit.Value.Normal;
        return hit.Value.Point.y;
    }

    // Advances one foot's gait and returns its current step lift (0 planted .. 1 peak swing).
    private float StepFoot(ref FootGait gait, float3 desired, bool otherStepping, float dt, Slot proxy)
    {
        if (proxy == null)
            return 0f;

        if (!gait.Init)
        {
            gait.Planted = desired;
            gait.Init = true;
        }

        float lift = 0f;
        float3 pos;
        if (gait.Stepping)
        {
            gait.Progress += dt / MathF.Max(StepDuration.Value, 0.01f);
            if (gait.Progress >= 1f)
            {
                gait.Progress = 1f;
                gait.Planted = desired;
                gait.Stepping = false;
            }
            float t = gait.Progress;
            float smooth = t * t * (3f - 2f * t);                 // ease in/out
            pos = float3.Lerp(gait.StepStart, desired, smooth);
            lift = MathF.Sin(t * MathF.PI);
            pos.y += lift * StepHeight.Value;                     // lift arc
        }
        else
        {
            // Plant; only start a step once we've strayed too far AND the other foot is grounded.
            if (!otherStepping && float3.Distance(gait.Planted, desired) > StepThreshold.Value)
            {
                gait.Stepping = true;
                gait.Progress = 0f;
                gait.StepStart = gait.Planted;
            }
            pos = gait.Planted;
        }

        WriteFoot(proxy, pos);
        return lift;
    }

    private static void WriteFoot(Slot proxy, float3 worldPos)
    {
        var parent = proxy.Parent;
        var local = parent != null ? parent.GlobalPointToLocal(worldPos) : worldPos;
        if ((proxy.LocalPosition.Value - local).LengthSquared > 1e-10f)
            proxy.LocalPosition.SetValueSilently(local, change: true);
    }

    public void LogDiagnosticInfo()
    {
        LumoraLogger.Log("AvatarIK Diagnostic:");
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
