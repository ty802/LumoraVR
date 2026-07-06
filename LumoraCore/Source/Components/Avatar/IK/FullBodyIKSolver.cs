// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

// Engine-side full-body IK driver. Owns a HumanoidRig, captures rest bone
// lengths + rest directions + rest limb poses once, then each frame:
//   - anchors hips from the head/pelvis target (+ locomotion offset) and solves the spine with
//     FABRIK under a per-segment stiffness ramp,
//   - solves each arm with a SHOULDER swing + analytic two-bone IK + forearm TWIST RELAX,
//   - solves each leg with analytic two-bone IK, ground-aligned feet and TOE articulation,
//   - bends elbows/knees toward a DYNAMIC pole (rest joint swung to follow the limb) that an
//     optional BEND GOAL can steer,
//   - blends every effector by a per-target POSITION/ROTATION WEIGHT (rest hang <-> full IK),
//   - STRETCHES limbs slightly past reach, with an anti-lock so they never fully straighten.
// Tunables are public instance fields so the component can drive them from sync values at runtime.
// No platform IK dependency; the heavy maths stay in FabrikSolver. - xlinka
public sealed class FullBodyIKSolver
{
    public struct Target
    {
        public bool Valid;
        public float3 Position;
        public floatQ Rotation;

        // 0..1 blend from the limb's rest hang to full IK on the target. Set to 1 for full IK.
        public float PositionWeight;
        public float RotationWeight;

        // Optional elbow/knee steer: the bend pole is pulled toward BendGoal by BendGoalWeight.
        public bool HasBendGoal;
        public float3 BendGoal;

        // When set (procedural feet), the foot ignores Rotation and orients to the rest foot pose
        // tilted onto GroundNormal and turned to face GroundForward. Tracked feet leave this false.
        public bool GroundAlign;
        public float3 GroundNormal;
        // Flattened world direction the planted foot should face (the gait's planted facing). When zero the foot
        // falls back to following the body turn. Lets a planted foot hold its facing instead of swiveling with look.
        public float3 GroundForward;
        // Full procedural foot rotation carried by the gait. When present, GroundAlign tilts this rotation onto the
        // surface normal instead of rebuilding orientation from a single forward vector.
        public bool HasGroundRotation;
        public floatQ GroundRotation;

        // 0..1 step swing height (procedural feet), used to curl the toe during the step.
        public float StepLift;
    }

    // --- Tunables (driven from the component's sync values; defaults are sensible fallbacks) ---
    public float ShoulderWeight = 0.45f;   // how far the clavicle swings toward the hand
    // Clavicle yaw/pitch clamp half-ranges (radians) for the stronger shoulder solve. The clavicle swings forward/up
    // (toward a reach) far more than it swings back/down, so the ranges are asymmetric; left vs right are mirrored at
    // the call site. ArmLift is the extra roll-up the shoulder takes on a high/overhead reach. -xlinka
    public float ShoulderYawForward = 0.70f;  // ~40 deg: clavicle swings this far forward/across toward a reach
    public float ShoulderYawBack = 0.30f;     // ~17 deg: and only this far back
    public float ShoulderPitchUp = 0.80f;     // ~46 deg: clavicle lifts this far up on a high reach
    public float ShoulderPitchDown = 0.25f;   // ~14 deg: and only this far down
    public float ArmLift = 0.45f;             // roll-up gain: shoulder rolls up by this (rad) at a fully-overhead reach
    public float MaxStretch = 1.08f;       // max limb over-extension past rest length
    public float PelvisDamp = 0.65f;       // hips smoothing toward the anchor (1 = snap)
    public float SpineStiffness = 0.2f;    // base pull toward the straight hips->head line
    public float BendGoalWeight = 0.5f;    // how strongly a bend goal steers the pole
    public float TwistRelax = 0.5f;        // forearm roll distributed toward the hand
    // Additive cosmetic offset on the hips anchor. Y carries the vertical body bob (dip while a foot is airborne);
    // X/Z carry the HORIZONTAL body settle that eases the torso toward the centroid of the planted feet (biased to
    // the support foot). Both are computed by the component (smoothed there) and pushed in each solve - the gait
    // step-trigger reads the user root, NOT this, so the settle never feeds back into stepping. -xlinka
    public float3 LocomotionOffset;        // added to the hips anchor (body bob + horizontal settle / weight shift)
    public float? GroundY;                 // floor plane: when set, the pelvis is pinned within leg reach of it
    // User-root position this frame. When set, the hips damping runs RELATIVE to it, so root motion
    // (jump, fall, running) passes through instantly and only head/tracker noise gets smoothed. World-
    // space damping made the hips lag every fast root move - rubber-band stretch on jumps. -xlinka
    public float3? RootAnchor;
    // Optional flattened body/locomotion forward. Head target rotation is a calibrated view/bone target and can carry
    // creator offsets, so it must not be the only source for hips, gait and limb body-frame math. -xlinka
    public float3 BodyForward;

    // Never leave an untracked limb frozen at the model's authored bind pose: arms hang from the chest, legs stand
    // on the floor, and the torso follows the head within a max angle before turning. These three drive that. -xlinka
    public float MaxRootAngle = 0.436f;             // rad (~25 deg): head turns this far before the torso follows
    public float HipFollowFraction = 0.25f;         // within MaxRootAngle, torso follows this fraction of head yaw
    public float MoveBodyBackWhenCrouching = 0.35f; // shift hips back as the head dips so the knees don't shoot forward

    // Negate the geometric rest forward. GuessForwardAxis derives forward from the rig's LABELED left->right arm
    // line, so a rig whose left/right arm bones are authored on the swapped physical side (the fox) computes
    // forward BACKWARD. The torso-follow then can't recover 180 deg (it's clamped by MaxRootAngle), so the body
    // renders backward and drags the arms with it. AvatarIK decides this once per equip from the LIVE view
    // direction (a real tracker cue, not an authored bone rotation) and only when forward is clearly opposite the
    // camera, so it is a no-op for correctly-built rigs. -xlinka
    public bool ForwardSignFlip;

    // Write ROTATIONS only and let the rest bone offsets + parent rotation carry the joint positions (the hips root
    // position is always written). Writing per-bone GLOBAL POSITIONS pushed the joints through the model's extreme
    // armature scale (54x) and the avatar fit scale, which translated the skinned vertices and sheared the mesh even
    // though the joints solved correctly. Rotations are scale-invariant and keep bone lengths rigid, so the skinning
    // deforms cleanly. -xlinka
    public bool WriteBonePositions = false;

    // Idle-life inputs, pushed per solve by the component. TimeSeconds drives the rhythms; IdleWeight
    // (1 = standing still, fades with locomotion) gates the standing-only motions; the arm swing pair
    // adds the walking counter-swing to untracked arms. All motion is rest-anchored deltas - nothing
    // integrates, so it can never drift or accumulate. -xlinka
    public float TimeSeconds;
    public float IdleWeight;
    public float BreathingWeight = 1f;   // 0 disables; ~1 subtle chest rhythm
    public float ArmSwingPhase;          // radians, advanced by the component with gait speed
    public float ArmSwingWeight;         // 0..1, speed-faded by the component

    // Wrist roll steering the elbow (0..1): rolling a tracked wrist swings the bend pole around the arm
    // axis, the way a real forearm follows the hand. 0 disables.
    public float WristBendInfluence = 0.5f;
    // Chest yaw toward tracked hands (0..1): a cross-body or two-handed reach carries the upper torso
    // instead of leaving all the stretch to the shoulder. 0 disables; untracked hands contribute nothing.
    public float ChestFollowHands = 0.2f;

    private const float ToeCurlAngle = 0.5f;     // radians the toe curls at peak step lift
    private const float StiffnessTaper = 0.6f;   // upper spine is (1 - this) as stiff as the base
    private const float MaxChestYaw = 0.6f;      // rad (~34 deg) chest wind-up cap vs the hips
    private const float MaxWristBendSwing = 1.0f; // rad cap so an extreme roll can't flip the elbow over
    private const float BreathPeriodSeconds = 4.3f;   // full breath cycle
    private const float BreathPitchRad = 0.021f;      // chest pitch amplitude at BreathingWeight 1 (~1.2 deg)
    private const float ArmSwingReachFraction = 0.30f; // walking hand swing amplitude as a fraction of arm reach
    private const float ArmIdleDriftFraction = 0.035f; // standing hand drift amplitude as a fraction of arm reach

    private HumanoidRig _rig = null!;
    private bool _captured;

    // Hips smoothing state (pelvis follow / anti-snap).
    private float3 _prevHipsPos;
    private bool _hasPrevHips;
    private float3 _prevRootAnchor;
    private bool _hasPrevRootAnchor;

    // Per-frame body frame derived from the body/locomotion forward + the rest-pose standing hip height.
    // Used to hang untracked arms, stand untracked legs on the floor, and yaw the torso toward the head.
    private float _restStandHeight;                    // hips height above the sole plane in the rest pose (live scale)
    private float _restHeadHeight;                     // head height above the sole plane in the rest pose (live scale)
    // Foot BONE height above the sole plane at rest (live scale), per foot. The gait reads these so a
    // planted foot bone sits at its authored height instead of burying the paw in the floor. -xlinka
    public float LeftFootGroundClearance { get; private set; }
    public float RightFootGroundClearance { get; private set; }
    private const float CrouchHipsMinFraction = 0.34f; // deep squat: hips descend toward the heels
    private const float CrouchSpineSlack = 0.92f;      // spine may shorten this much at full crouch (hunch + back-lean)
    private float3 _curBodyForward = float3.Backward;  // flattened visual/body forward this frame
    private float3 _curBodyRight = float3.Right;        // body +X (right) this frame
    private floatQ _bodyYawDelta = floatQ.Identity;    // yaw applied to the torso so it follows the body frame

    // Body facing at capture (flattened) + the turn from it to the current facing, used to yaw
    // ground-aligned procedural feet and to swing rest-hang/rest-rotation blends with the body.
    private float3 _restBodyForward = float3.Backward;
    private floatQ _bodyTurn = floatQ.Identity;

    // Spine chain (filtered to existing bones, hips..head).
    private readonly List<Slot> _spine = new();
    private float[] _spineLengths = null!;
    private float3[] _spineJoints = null!;

    // Limb bone slots.
    private Slot _lShoulder = null!, _lUpper = null!, _lLower = null!, _lHand = null!;
    private Slot _rShoulder = null!, _rUpper = null!, _rLower = null!, _rHand = null!;
    private Slot _lHip = null!, _lKnee = null!, _lFoot = null!, _lToe = null!;
    private Slot _rHip = null!, _rKnee = null!, _rFoot = null!, _rToe = null!;

    private struct LimbRestPose
    {
        public bool Valid;
        public float3 RootToMid;
        public float3 RootToEnd;
        public float3 PlaneNormal;
        public float BendDistance;
    }

    private LimbRestPose _lArmRest, _rArmRest, _lLegRest, _rLegRest;

    // Rest bone lengths.
    private float _lUpperArmLen, _lLowerArmLen, _lShoulderLen;
    private float _rUpperArmLen, _rLowerArmLen, _rShoulderLen;
    private float _lUpperLegLen, _lLowerLegLen;
    private float _rUpperLegLen, _rLowerLegLen;
    private float _spineTotalLength;

    // Bind-pose (default) local transforms of every driven bone, plus the flat bone list. Each frame
    // FixTransforms resets the bones to these before ReadPose measures them, so lengths are always taken
    // from the clean rest pose at the LIVE avatar scale - never from last frame's solved pose, and never
    // from a stale capture-time scale. This is why the torso no longer stretches/offsets when the avatar
    // is rescaled to the user after the solver was set up. -xlinka
    private readonly List<Slot> _bones = new();
    private readonly Dictionary<Slot, float3> _bindLocalPos = new();
    private readonly Dictionary<Slot, floatQ> _bindLocalRot = new();

    // Rest toe orientation relative to the foot.
    private floatQ _lToeRel = floatQ.Identity, _rToeRel = floatQ.Identity;

    // Rest bone directions (bone -> child) in world space at capture time, used to derive swings.
    private readonly Dictionary<Slot, float3> _restDir = new();
    private readonly Dictionary<Slot, floatQ> _restRot = new();

    public bool IsReady => _captured && _rig != null && !_rig.IsDestroyed;

    // Bone writes are LOCAL: every peer runs this solve from the replicated proxy poses, so
    // broadcasting the results would have each peer's float-noise-different skeleton fight every
    // other peer's - visible as constant bone jitter. Change events still fire so transform caches
    // and the Godot hooks update. - xlinka
    private static void WriteGlobalPosition(Slot bone, in float3 position)
    {
        var parent = bone.Parent;
        var local = parent != null ? parent.GlobalPointToLocal(position) : position;
        if ((bone.LocalPosition.Value - local).LengthSquared > 1e-12f)
            bone.LocalPosition.SetValueSilently(local, change: true);
    }

    // Position write gated by WriteBonePositions. Used for every bone EXCEPT the hips root: in the default
    // rotation-first mode it's a no-op (the joint position follows from the parent rotation + rest offset), which is
    // what keeps the skinned mesh from shearing under the armature scale. -xlinka
    private void MaybeWritePosition(Slot bone, in float3 position)
    {
        if (WriteBonePositions)
            WriteGlobalPosition(bone, position);
    }

    private static void WriteGlobalRotation(Slot bone, in floatQ rotation)
    {
        var parent = bone.Parent;
        var local = parent != null ? parent.GlobalRotationToLocal(rotation) : rotation;
        float dot = floatQ.Dot(local, bone.LocalRotation.Value);
        if (1f - (dot < 0 ? -dot : dot) > 1e-9f)
            bone.LocalRotation.SetValueSilently(local, change: true);
    }

    // Aim the head along the look while TRUSTING the authored head pose. Every rig authors the head
    // looking along the body's rest forward, so the look is applied as a yaw (rest body forward ->
    // look, about world up) plus a pitch (about the look's right axis) on top of the captured rest
    // rotation. No bone axis is ever consulted - assuming the snout sits on the bone's local -Z (or
    // its up on +Y) twisted heads sideways on arbitrary-convention rigs, the same trap that rolled
    // the paws. Roll is never generated, so the authored tilt survives. -xlinka
    private void WriteHeadFacing(Slot head, in floatQ lookRotation)
    {
        if (head == null || head.IsDestroyed)
            return;
        if (!_restRot.TryGetValue(head, out var restRot))
        {
            WriteGlobalRotation(head, lookRotation);
            return;
        }

        float3 look = lookRotation * float3.Backward;
        if (look.LengthSquared < 1e-8f)
        {
            WriteGlobalRotation(head, restRot);
            return;
        }
        look = look.Normalized;

        float3 lookFlat = look;
        lookFlat.y = 0f;
        if (lookFlat.LengthSquared < 1e-6f)
            lookFlat = _curBodyForward;   // looking straight up/down: hold the body's yaw
        lookFlat = lookFlat.LengthSquared > 1e-6f ? lookFlat.Normalized : _restBodyForward;

        float yawDelta = WrapPi(
            MathF.Atan2(lookFlat.x, lookFlat.z) - MathF.Atan2(_restBodyForward.x, _restBodyForward.z));
        floatQ aim = floatQ.AxisAngleRad(float3.Up, yawDelta);

        float pitch = MathF.Asin(System.Math.Clamp(look.y, -1f, 1f));
        if (MathF.Abs(pitch) > 1e-4f)
        {
            float3 right = float3.Cross(lookFlat, float3.Up);
            if (right.LengthSquared > 1e-8f)
                aim = floatQ.AxisAngleRad(right.Normalized, pitch) * aim;
        }

        WriteGlobalRotation(head, aim * restRot);
    }

    public void Initialize(HumanoidRig rig)
    {
        _rig = rig;
        _captured = false;
        Capture();
    }

    public void ResetToDefaultPose()
    {
        if (IsReady)
            FixTransforms();
    }

    private void Capture()
    {
        if (_rig == null || _rig.IsDestroyed)
            return;

        _spine.Clear();
        AddSpineBone(BodyNode.Hips);
        AddSpineBone(BodyNode.Spine);
        AddSpineBone(BodyNode.Chest);
        AddSpineBone(BodyNode.UpperChest);
        AddSpineBone(BodyNode.Neck);
        AddSpineBone(BodyNode.Head);

        if (_spine.Count < 2)
            return;

        _spineLengths = new float[_spine.Count - 1];
        _spineJoints = new float3[_spine.Count];

        _lShoulder = _rig.TryGetBone(BodyNode.LeftShoulder);
        _lUpper = _rig.TryGetBone(BodyNode.LeftUpperArm);
        _lLower = _rig.TryGetBone(BodyNode.LeftLowerArm);
        _lHand = _rig.TryGetBone(BodyNode.LeftHand);

        _rShoulder = _rig.TryGetBone(BodyNode.RightShoulder);
        _rUpper = _rig.TryGetBone(BodyNode.RightUpperArm);
        _rLower = _rig.TryGetBone(BodyNode.RightLowerArm);
        _rHand = _rig.TryGetBone(BodyNode.RightHand);

        _lHip = _rig.TryGetBone(BodyNode.LeftUpperLeg);
        _lKnee = _rig.TryGetBone(BodyNode.LeftLowerLeg);
        _lFoot = _rig.TryGetBone(BodyNode.LeftFoot);
        _lToe = _rig.TryGetBone(BodyNode.LeftToes);

        _rHip = _rig.TryGetBone(BodyNode.RightUpperLeg);
        _rKnee = _rig.TryGetBone(BodyNode.RightLowerLeg);
        _rFoot = _rig.TryGetBone(BodyNode.RightFoot);
        _rToe = _rig.TryGetBone(BodyNode.RightToes);

        // Snapshot the default (bind) local transform of every bone we drive, then measure once so the
        // solver has valid lengths/rest before the first solve. From then on the per-frame FixTransforms
        // + ReadPose loop re-derives both from the live skeleton.
        StoreDefaultLocalState();
        ReadPose();

        _captured = true;
    }

    // Snapshot each driven bone's bind-pose LOCAL position/rotation (scale is never solved, so it's left
    // alone). FixTransforms restores these every frame so reads happen from the clean rest pose instead
    // of last frame's solved (possibly stretched) pose. -xlinka
    private void StoreDefaultLocalState()
    {
        _bones.Clear();
        for (int i = 0; i < _spine.Count; i++)
            AddBone(_spine[i]);
        AddBone(_lShoulder); AddBone(_lUpper); AddBone(_lLower); AddBone(_lHand);
        AddBone(_rShoulder); AddBone(_rUpper); AddBone(_rLower); AddBone(_rHand);
        AddBone(_lHip); AddBone(_lKnee); AddBone(_lFoot); AddBone(_lToe);
        AddBone(_rHip); AddBone(_rKnee); AddBone(_rFoot); AddBone(_rToe);

        _bindLocalPos.Clear();
        _bindLocalRot.Clear();
        for (int i = 0; i < _bones.Count; i++)
        {
            var b = _bones[i];
            _bindLocalPos[b] = b.LocalPosition.Value;
            _bindLocalRot[b] = b.LocalRotation.Value;
        }
    }

    private void AddBone(Slot bone)
    {
        if (bone != null && !bone.IsDestroyed && !_bones.Contains(bone))
            _bones.Add(bone);
    }

    // Reset every driven bone to its bind-pose local transform. The bones then sit at the clean rest
    // pose carried by their live parents - so they already include the current avatar fit scale and
    // body orientation. Position/rotation only; the solve never touches scale. -xlinka
    private void FixTransforms()
    {
        for (int i = 0; i < _bones.Count; i++)
        {
            var b = _bones[i];
            if (b == null || b.IsDestroyed)
                continue;
            if (_bindLocalPos.TryGetValue(b, out var p))
                b.LocalPosition.SetValueSilently(p, change: true);
            if (_bindLocalRot.TryGetValue(b, out var r))
                b.LocalRotation.SetValueSilently(r, change: true);
        }
    }

    // Measure bone lengths + rest poses from the CURRENT bones (which FixTransforms just reset to bind
    // pose). Because the bones carry the live avatar scale, every length is correct for the rendered
    // avatar this frame - there is no stored capture scale to drift from. -xlinka
    private void ReadPose()
    {
        _spineTotalLength = 0f;
        for (int i = 0; i < _spine.Count - 1; i++)
        {
            _spineLengths[i] = float3.Distance(_spine[i].GlobalPosition, _spine[i + 1].GlobalPosition);
            _spineTotalLength += _spineLengths[i];
        }

        _lUpperArmLen = BoneLen(_lUpper, _lLower);
        _lLowerArmLen = BoneLen(_lLower, _lHand);
        _rUpperArmLen = BoneLen(_rUpper, _rLower);
        _rLowerArmLen = BoneLen(_rLower, _rHand);
        _lShoulderLen = BoneLen(_lShoulder, _lUpper);
        _rShoulderLen = BoneLen(_rShoulder, _rUpper);
        _lUpperLegLen = BoneLen(_lHip, _lKnee);
        _lLowerLegLen = BoneLen(_lKnee, _lFoot);
        _rUpperLegLen = BoneLen(_rHip, _rKnee);
        _rLowerLegLen = BoneLen(_rKnee, _rFoot);
        _lArmRest = CaptureLimbRest(_lUpper, _lLower, _lHand);
        _rArmRest = CaptureLimbRest(_rUpper, _rLower, _rHand);
        _lLegRest = CaptureLimbRest(_lHip, _lKnee, _lFoot);
        _rLegRest = CaptureLimbRest(_rHip, _rKnee, _rFoot);

        CaptureRest(_spine);
        CaptureRestChain(_lShoulder, _lUpper, _lLower, _lHand);
        CaptureRestChain(_rShoulder, _rUpper, _rLower, _rHand);
        CaptureRestChain(_lHip, _lKnee, _lFoot, null!);
        CaptureRestChain(_rHip, _rKnee, _rFoot, null!);
        // Hands/feet are chain ends, so capture their rest rotation explicitly for the rotation blend.
        CaptureBoneRest(_lHand, null!);
        CaptureBoneRest(_rHand, null!);

        // Rest toe orientation relative to the foot, so the toe can ride the foot + curl on steps.
        _lToeRel = ToeRel(_lFoot, _lToe);
        _rToeRel = ToeRel(_rFoot, _rToe);

        // Rest/body facing comes from GEOMETRY, not the hips bone rotation. The fox-style failure case is exactly a
        // hips/spine authored forward that points opposite, or rolls away from, the visual body. Reading that bone
        // again here reintroduces the backwards hips/legs bug. The shoulder line + hips->head line gives the real
        // visible front every frame after FixTransforms, so body turn, feet and limb poles all share one honest frame.
        // -xlinka
        var geometricFwd = _rig.GuessForwardAxis();
        if (geometricFwd.HasValue && geometricFwd.Value.LengthSquared > 1e-6f)
        {
            _restBodyForward = geometricFwd.Value.Normalized;
            // The geometric forward trusts the rig's left/right arm LABELS; a swapped-label rig reads backward.
            // AvatarIK sets this from the live view once the avatar is equipped on the local user. -xlinka
            if (ForwardSignFlip)
                _restBodyForward = -_restBodyForward;
        }
        else
        {
            float3 restFwd = _spine[0].GlobalRotation * float3.Backward;
            restFwd.y = 0f;
            _restBodyForward = restFwd.LengthSquared > 1e-6f ? restFwd.Normalized : float3.Backward;
        }

        // Natural standing heights, measured from the SOLE PLANE: the lowest foot/toe bone in the rest
        // pose (the bind pose stands the model on the ground, so that IS the sole), at the live scale.
        // The floor-pin uses these so the body stands at its real height (anti-float) and untracked
        // legs reach the ground. Falls back to leg length when the feet aren't mapped. -xlinka
        float hipsRestY = _spine[0].GlobalPosition.y;
        float soleRestY = hipsRestY;
        bool hasFootRest = false;
        void LowerSole(Slot? bone)
        {
            if (bone == null || bone.IsDestroyed)
                return;
            float y = bone.GlobalPosition.y;
            soleRestY = hasFootRest ? System.Math.Min(soleRestY, y) : y;
            hasFootRest = true;
        }
        LowerSole(_lFoot);
        LowerSole(_rFoot);
        LowerSole(_lToe);
        LowerSole(_rToe);
        _restStandHeight = hasFootRest ? System.Math.Max(hipsRestY - soleRestY, 0f) : 0f;
        _restHeadHeight = hasFootRest ? System.Math.Max(_spine[_spine.Count - 1].GlobalPosition.y - soleRestY, 0f) : 0f;

        // How far each foot BONE sits above the sole plane at rest. The gait plants the BONE this much
        // above the floor - planting it AT the floor buried the paws/soles by exactly this amount. -xlinka
        LeftFootGroundClearance = _lFoot != null && !_lFoot.IsDestroyed && hasFootRest
            ? System.Math.Max(_lFoot.GlobalPosition.y - soleRestY, 0f) : 0f;
        RightFootGroundClearance = _rFoot != null && !_rFoot.IsDestroyed && hasFootRest
            ? System.Math.Max(_rFoot.GlobalPosition.y - soleRestY, 0f) : 0f;
    }

    private void AddSpineBone(BodyNode node)
    {
        var bone = _rig.TryGetBone(node);
        if (bone != null && !bone.IsDestroyed)
            _spine.Add(bone);
    }

    private static float BoneLen(Slot a, Slot b)
        => (a != null && b != null) ? float3.Distance(a.GlobalPosition, b.GlobalPosition) : 0f;

    private static floatQ ToeRel(Slot foot, Slot toe)
        => (foot != null && toe != null) ? foot.GlobalRotation.Inverse * toe.GlobalRotation : floatQ.Identity;

    private static LimbRestPose CaptureLimbRest(Slot? root, Slot? mid, Slot? end)
    {
        if (root == null || mid == null || end == null
            || root.IsDestroyed || mid.IsDestroyed || end.IsDestroyed)
            return default;

        float3 rootPos = root.GlobalPosition;
        float3 rootToEnd = end.GlobalPosition - rootPos;
        float3 rootToMid = mid.GlobalPosition - rootPos;
        if (rootToEnd.LengthSquared < 1e-8f || rootToMid.LengthSquared < 1e-8f)
            return default;

        float3 limbDir = rootToEnd.Normalized;
        float3 bend = rootToMid - limbDir * float3.Dot(rootToMid, limbDir);
        float bendDistance = bend.Length;

        float3 normal = float3.Cross(rootToEnd, rootToMid);
        if (normal.LengthSquared < 1e-8f && bendDistance > 1e-6f)
            normal = float3.Cross(rootToEnd, bend);
        if (normal.LengthSquared < 1e-8f)
            normal = float3.Cross(rootToEnd, float3.Up);
        if (normal.LengthSquared < 1e-8f)
            normal = float3.Cross(rootToEnd, float3.Right);

        return new LimbRestPose
        {
            Valid = true,
            RootToMid = rootToMid,
            RootToEnd = rootToEnd,
            PlaneNormal = normal.LengthSquared > 1e-8f ? normal.Normalized : float3.Up,
            BendDistance = bendDistance
        };
    }

    private void CaptureRest(List<Slot> chain)
    {
        for (int i = 0; i < chain.Count; i++)
        {
            var bone = chain[i];
            _restRot[bone] = bone.GlobalRotation;
            if (i < chain.Count - 1)
            {
                var dir = chain[i + 1].GlobalPosition - bone.GlobalPosition;
                _restDir[bone] = dir.LengthSquared > 1e-8f ? dir.Normalized : float3.Up;
            }
        }
    }

    private void CaptureRestChain(Slot a, Slot b, Slot c, Slot d)
    {
        CaptureBoneRest(a, b);
        CaptureBoneRest(b, c);
        CaptureBoneRest(c, d);
    }

    private void CaptureBoneRest(Slot bone, Slot child)
    {
        if (bone == null) return;
        _restRot[bone] = bone.GlobalRotation;
        if (child != null)
        {
            var dir = child.GlobalPosition - bone.GlobalPosition;
            _restDir[bone] = dir.LengthSquared > 1e-8f ? dir.Normalized : float3.Up;
        }
    }

    // Solve the full body for one frame. headTarget is required; the rest are optional (limbs
    // without a valid target are left at rest).
    public void Solve(
        in Target headTarget,
        in Target pelvisTarget,
        in Target leftHand,
        in Target rightHand,
        in Target leftFoot,
        in Target rightFoot)
    {
        if (!IsReady || !headTarget.Valid)
            return;

        // Reset to the bind pose, then measure from the live skeleton, before solving. The bones carry
        // the current avatar fit scale + body orientation, so the lengths/rest poses ReadPose produces
        // are always sized to the rendered avatar this frame - this is what keeps the torso from
        // stretching/offsetting when the avatar was rescaled after the solver was set up. -xlinka
        FixTransforms();
        ReadPose();

        // Turn since capture (flattened), so rest-blends and ground-aligned feet yaw with the body.
        float3 curFwd = BodyForward.LengthSquared > 1e-6f
            ? BodyForward
            : headTarget.Rotation * float3.Backward;
        curFwd.y = 0f;
        curFwd = curFwd.LengthSquared > 1e-6f ? curFwd.Normalized : _restBodyForward;
        _bodyTurn = FabrikSolver.FromToRotation(_restBodyForward, curFwd);

        // Body frame this frame: forward = flattened head forward, right = body +X. Untracked arms hang along this
        // frame instead of the model's authored bind pose (which an anthro rig rarely authors as a clean A-pose). -xlinka
        _curBodyForward = curFwd;
        float3 bodyRight = float3.Cross(curFwd, float3.Up);
        _curBodyRight = bodyRight.LengthSquared > 1e-6f ? bodyRight.Normalized : float3.Right;

        // Body facing: turn the body frame toward the locomotion/body forward. The head bone itself is pinned later
        // to the live view rotation, so creator view offsets cannot rotate the hips or make W walk sideways.
        float restYaw = MathF.Atan2(_restBodyForward.x, _restBodyForward.z);
        float bodyYaw = MathF.Atan2(curFwd.x, curFwd.z);
        _bodyYawDelta = floatQ.AxisAngleRad(float3.Up, WrapPi(bodyYaw - restYaw));

        SolveSpine(headTarget, pelvisTarget);
        TurnChestTowardHands(in leftHand, in rightHand, in headTarget);
        ApplyBreathing(in headTarget);

        // Natural hang side in the solver's body frame: left=-1, right=+1.
        // Per-arm side from GEOMETRY, not the bone label. The 180 import flip that normalizes a back-authored
        // model's facing can leave the rig's named left/right arm bones sitting on the opposite physical side
        // (the "left" bone ends up on the body's right). Hanging by label then drives each hand to the WRONG side,
        // crossing them into the chest. Sign each arm by which side of the shoulder centre its own upper-arm bone
        // actually sits along the body-right axis, so every arm rests on the side it is really on. -xlinka
        float lArmSide = -1f, rArmSide = 1f;
        if (_lUpper != null && !_lUpper.IsDestroyed && _rUpper != null && !_rUpper.IsDestroyed)
        {
            float3 shoulderCentre = (_lUpper.GlobalPosition + _rUpper.GlobalPosition) * 0.5f;
            lArmSide = float3.Dot(_lUpper.GlobalPosition - shoulderCentre, _curBodyRight) >= 0f ? 1f : -1f;
            rArmSide = float3.Dot(_rUpper.GlobalPosition - shoulderCentre, _curBodyRight) >= 0f ? 1f : -1f;
        }
        SolveArm(BodyNode.LeftUpperArm, _lShoulder, _lUpper, _lLower, _lHand, _lUpperArmLen, _lLowerArmLen, _lShoulderLen, in leftHand, lArmSide);
        SolveArm(BodyNode.RightUpperArm, _rShoulder, _rUpper, _rLower, _rHand, _rUpperArmLen, _rLowerArmLen, _rShoulderLen, in rightHand, rArmSide);

        SolveLeg(BodyNode.LeftUpperLeg, _lHip, _lKnee, _lFoot, _lToe, _lToeRel, _lUpperLegLen, _lLowerLegLen, in leftFoot);
        SolveLeg(BodyNode.RightUpperLeg, _rHip, _rKnee, _rFoot, _rToe, _rToeRel, _rUpperLegLen, _rLowerLegLen, in rightFoot);
    }

    // Yaw the chest toward tracked hands so a cross-body or two-handed reach carries the upper torso instead of
    // leaving all the stretch to the shoulder. Each hand's yaw is measured against its own shoulder direction
    // (a hand hanging under its shoulder is neutral), weighted by its position weight, clamped so the chest never
    // winds past the hips. Desktop hands weigh 0, so this is a no-op there. The head is a chest descendant and
    // inherits the yaw, so it gets re-pinned to the view. -xlinka
    private void TurnChestTowardHands(in Target leftHand, in Target rightHand, in Target headTarget)
    {
        if (ChestFollowHands <= 1e-4f || _spine.Count < 3)
            return;

        Slot chest = _spine[_spine.Count - 2];
        float3 chestPos = chest.GlobalPosition;

        float yawSum = 0f, totalW = 0f;
        AccumulateHandYaw(in leftHand, chestPos, _lUpper, ref yawSum, ref totalW);
        AccumulateHandYaw(in rightHand, chestPos, _rUpper, ref yawSum, ref totalW);
        if (totalW < 1e-4f)
            return;

        float yaw = System.Math.Clamp(yawSum / totalW * ChestFollowHands, -MaxChestYaw, MaxChestYaw);
        if (MathF.Abs(yaw) < 1e-4f)
            return;

        WriteGlobalRotation(chest, floatQ.AxisAngleRad(float3.Up, yaw) * chest.GlobalRotation);
        WriteHeadFacing(_spine[_spine.Count - 1], headTarget.Rotation);
    }

    // Subtle rhythmic chest motion so a standing avatar reads as alive. Rotation-only on the chest -
    // the shoulders, arms and head all ride it as descendants - and rest-anchored per frame, so it can
    // never accumulate. Runs during movement too (invisible under locomotion, but the shoulders never
    // read as frozen). Two stacked sines so the inhale reads slightly quicker than the exhale. -xlinka
    private void ApplyBreathing(in Target headTarget)
    {
        if (BreathingWeight <= 1e-4f || _spine.Count < 3)
            return;

        Slot chest = _spine[_spine.Count - 2];
        float t = TimeSeconds * (MathF.PI * 2f / BreathPeriodSeconds);
        float breath = MathF.Sin(t) + 0.35f * MathF.Sin(t * 2f + 0.8f);
        float angle = breath * BreathPitchRad * BreathingWeight;
        if (MathF.Abs(angle) < 1e-5f)
            return;

        WriteGlobalRotation(chest, floatQ.AxisAngleRad(_curBodyRight, angle) * chest.GlobalRotation);
        // The head is a chest descendant and just inherited the pitch; keep it on the view.
        WriteHeadFacing(_spine[_spine.Count - 1], headTarget.Rotation);
    }

    private void AccumulateHandYaw(in Target hand, float3 chestPos, Slot? upper, ref float yawSum, ref float totalW)
    {
        float w = hand.Valid ? System.Math.Clamp(hand.PositionWeight, 0f, 1f) : 0f;
        if (w < 1e-4f || upper == null || upper.IsDestroyed)
            return;

        float3 toHand = hand.Position - chestPos;
        float3 neutral = upper.GlobalPosition - chestPos;
        toHand.y = 0f;
        neutral.y = 0f;
        if (toHand.LengthSquared < 1e-6f || neutral.LengthSquared < 1e-6f)
            return;

        float yaw = WrapPi(MathF.Atan2(toHand.x, toHand.z) - MathF.Atan2(neutral.x, neutral.z));
        yawSum += yaw * w;
        totalW += w;
    }

    private void SolveSpine(in Target headTarget, in Target pelvisTarget)
    {
        int n = _spine.Count;
        float3 headPos = headTarget.Position;

        // Crouch floor for the BODY, derived so the SPINE NEVER COMPRESSES: crouch depth belongs to the
        // KNEES (the hips follow the head down toward the heels), and the head bottoms out a near-full
        // spine above the lowest the hips may go. An independent head floor let the head keep dropping
        // after the hips band stopped - the spine ate the difference and the pose crumpled. -xlinka
        if (GroundY.HasValue && _restHeadHeight > 1e-4f && _restStandHeight > 1e-4f)
        {
            float spineVertical = MathF.Max(_restHeadHeight - _restStandHeight, _restHeadHeight * 0.25f);
            float minHeadY = GroundY.Value + _restStandHeight * CrouchHipsMinFraction + spineVertical * CrouchSpineSlack;
            headPos.y = MathF.Max(headPos.y, minHeadY);
        }

        // Anchor the hips. With a pelvis/waist tracker, drive them from it directly (true 3+ point
        // tracking); otherwise estimate them below the head, pushed back so the chest leans forward.
        float3 hipsPos;
        if (pelvisTarget.Valid)
        {
            hipsPos = pelvisTarget.Position;
        }
        else
        {
            hipsPos = headPos + new float3(0f, -_spineTotalLength, 0f) + _curBodyForward * (_spineTotalLength * 0.12f);
        }

        // Damp toward the anchor so head/tracker noise doesn't snap the hips frame to frame, then
        // add the transient locomotion offset (kept out of the smoothed state so it stays responsive).
        // Damped RELATIVE to the user root when it's known: the root's own motion carries the hips
        // rigidly and only the head-relative part is smoothed.
        if (_hasPrevHips)
        {
            if (RootAnchor.HasValue && _hasPrevRootAnchor)
            {
                float3 anchor = RootAnchor.Value;
                float3 rel = float3.Lerp(_prevHipsPos - _prevRootAnchor, hipsPos - anchor, PelvisDamp);
                hipsPos = anchor + rel;
            }
            else
            {
                hipsPos = float3.Lerp(_prevHipsPos, hipsPos, PelvisDamp);
            }
        }
        _prevHipsPos = hipsPos;
        _prevRootAnchor = RootAnchor ?? float3.Zero;
        _hasPrevRootAnchor = RootAnchor.HasValue;
        _hasPrevHips = true;
        float3 hipsFinal = hipsPos + LocomotionOffset;

        // ForceRootHeight: stand the body at its NATURAL standing height above the floor when the feet aren't
        // tracked, so untracked legs reach the ground (anti-float) and the hips can't crouch through it (anti-sink).
        // The band is the rest-pose hip height (measured at the live scale) rather than a loose leg-length fraction,
        // so a correctly-scaled avatar stands exactly right and a mis-scaled one is still caught instead of floating
        // with curled legs. The MIN must let the hips FOLLOW the head down to near the heels - crouch depth belongs
        // to the knees; a high floor here blocks the hips while the head keeps dropping and the spine crumples. The
        // head's own floor (above) is spine-length-consistent with this minimum. -xlinka
        if (GroundY.HasValue)
        {
            float legLen = System.Math.Max(_lUpperLegLen + _lLowerLegLen, _rUpperLegLen + _rLowerLegLen);
            float stand = _restStandHeight > 1e-4f ? _restStandHeight : legLen * 0.9f;
            if (stand > 1e-4f)
            {
                float maxHipsY = GroundY.Value + stand;
                float minHipsY = GroundY.Value + stand * CrouchHipsMinFraction;
                hipsFinal.y = System.Math.Clamp(hipsFinal.y, minHipsY, maxHipsY);

                // Crouch back-shift: as the head dips below standing, lean the hips back along the body forward so the
                // weight sits over the feet instead of the knees shooting forward through the floor.
                if (MoveBodyBackWhenCrouching > 0f)
                {
                    float drop = maxHipsY - hipsFinal.y;
                    if (drop > 1e-4f)
                    {
                        hipsFinal -= _curBodyForward * (drop * MoveBodyBackWhenCrouching);
                    }
                }
            }
        }

        WriteGlobalPosition(_spine[0], hipsFinal);

        for (int i = 0; i < n; i++)
            _spineJoints[i] = _spine[i].GlobalPosition;
        _spineJoints[0] = hipsFinal;

        FabrikSolver.SolveChain(_spineJoints, _spineLengths, headPos, iterations: 12, tolerance: 0.001f);

        // Stiffness: pull interior joints toward the straight hips->head line so the spine resists
        // over-bending. Stiffer low (lumbar stable), looser high (thoracic mobile).
        if (SpineStiffness > 0f && n > 2 && _spineTotalLength > 1e-5f)
        {
            float cum = 0f;
            for (int i = 1; i < n - 1; i++)
            {
                cum += _spineLengths[i - 1];
                float frac = cum / _spineTotalLength;
                float3 straight = float3.Lerp(hipsFinal, headPos, frac);
                float w = System.Math.Clamp(SpineStiffness * (1f - StiffnessTaper * frac), 0f, 1f);
                _spineJoints[i] = float3.Lerp(_spineJoints[i], straight, w);
            }
        }

        // Both ends pinned: hips exact AND head exact, with the interior joints reconciled to their
        // segment lengths. The old hard head pin after a one-ended solve stretched the neck segment. -xlinka
        FabrikSolver.ReconcilePinnedEnds(_spineJoints, _spineLengths, hipsFinal, headPos);

        // Write spine bone positions + swing rotations (hips..neck; the head is handled separately below).
        for (int i = 0; i < n - 1; i++)
        {
            var bone = _spine[i];
            MaybeWritePosition(bone, _spineJoints[i]);
            ApplySpineSwing(bone, _spineJoints[i + 1] - _spineJoints[i], _curBodyForward);
        }

        // Head bone: SWING its rest rotation so its rendered view-forward (GlobalRotation * Backward, our forward
        // convention in Godot's right-handed -Z-forward space) points along the look, instead of pinning the raw
        // target rotation. Every other bone is written as swing*restRot; the head was the one exception, taking the
        // target directly. That assumed the snout was authored exactly along the head bone's local -Z - but on a
        // back-authored rig the head bone's own forward can sit ~180 from the body front even after the import root
        // flip (the flip corrects the body, not necessarily each bone's local twist), so the raw pin rendered the
        // snout backward while the geometric body forward still agreed with the look. Swinging the captured rest
        // forward onto the look makes the snout follow the camera by construction, whatever the authored twist, and
        // collapses to the authored rest when the look matches it. Roll is carried by the rest (the head doesn't
        // roll on look). -xlinka
        var head = _spine[n - 1];
        MaybeWritePosition(head, _spineJoints[n - 1]);
        WriteHeadFacing(head, headTarget.Rotation);

        // Body yaw-follow: turn the whole torso toward the head facing within MaxRootAngle, so turning the head
        // carries the body instead of leaving it permanently twisted. Apply the yaw ONCE at the hips - every spine,
        // arm and leg bone is a descendant and inherits it, so writing each bone individually would COMPOUND the yaw
        // (Y^2, Y^3, ...) and over-twist the torso. Then RE-PIN the head to its target as the LAST write: the head is
        // a child of the now-yawed neck, so without this it inherits the yaw and stops facing where the user looks
        // (the head-look bug); the neck absorbs the difference. Skipped when a pelvis tracker rules the hips. -xlinka
        // NOTE: on desktop, body-yaw at the hips is DISABLED (HipFollowFraction default 0) because the user root
        // already yaws the whole avatar to the look. The rig's NATIVE orientation (a backward-authored hips bone)
        // is handled at the source now - _restBodyForward is the GEOMETRIC forward (HumanoidRig.GuessForwardAxis),
        // not the hips bone rotation - so _bodyTurn/_bodyYawDelta are correct regardless of authoring. This block
        // is the VR torso-follow path (HipFollowHead > 0): the head can lead the body, so the torso eases after
        // it. -xlinka
        if (!pelvisTarget.Valid && HipFollowFraction > 1e-3f)
        {
            WriteGlobalRotation(_spine[0], _bodyYawDelta * _spine[0].GlobalRotation);
            WriteHeadFacing(head, headTarget.Rotation);
        }

        // With a real pelvis tracker, the hips bone takes its rotation too.
        if (pelvisTarget.Valid)
            WriteGlobalRotation(_spine[0], pelvisTarget.Rotation);
    }

    // Wrap an angle to (-pi, pi].
    private static float WrapPi(float a)
    {
        while (a > MathF.PI) a -= 2f * MathF.PI;
        while (a < -MathF.PI) a += 2f * MathF.PI;
        return a;
    }

    // Natural resting hand position for an untracked arm: hang down the body frame from the shoulder root, outward
    // (side = -1 left / +1 right) and back. Independent of the model's authored bind pose, so any rig
    // rests in a clean by-the-side idle on desktop instead of a frozen splay. -xlinka
    private float3 NaturalArmHang(float3 root, float armReach, float side)
    {
        // Keep the wrist visibly beside and slightly behind the torso. This is intentionally component-based instead
        // of a normalized direction: the arm should keep most of its vertical drop while still getting enough rear
        // clearance for bulky paw meshes.
        // Stay WITHIN the arm's reach so the two-bone solver bends the elbow (the old 0.86 down + 0.36 out + 0.62
        // BACK summed to ~1.12x reach -> arm fully extended, straight, and yanked behind the torso). Down with a
        // little outward and a little FORWARD = a relaxed by-the-side idle with a natural elbow bend.
        return root
            + float3.Down * (armReach * 0.72f)
            + _curBodyRight * (side * armReach * 0.16f)
            + _curBodyForward * (armReach * 0.10f);
    }

    private floatQ NaturalHandRotation(Slot? lower, Slot? hand, float3 forearmDir, float side)
    {
        if (lower != null && hand != null
            && _restRot.TryGetValue(lower, out var lowerRest)
            && _restRot.TryGetValue(hand, out var handRest))
        {
            // Preserve the authored wrist pose relative to the solved forearm. Building a fresh wrist frame assumes
            // a hand bone axis convention that imported avatar hands rarely share, and it was twisting paws into a
            // visible "hands in front" pose even when the arm bones were solved beside the body. -xlinka
            return lower.GlobalRotation * (lowerRest.Inverse * handRest);
        }

        if (forearmDir.LengthSquared < 1e-8f)
            forearmDir = float3.Down;
        forearmDir = forearmDir.Normalized;

        float3 inward = _curBodyRight * -side;
        if (inward.LengthSquared < 1e-8f)
            inward = float3.Right * -side;

        return BuildRotation(forearmDir, inward.Normalized);
    }

    // Rotation whose local -Z axis points along forward and whose local +Y rolls
    // toward up. This avoids floatQ.LookRotation, whose basis convention is not
    // the one used by avatar body/proxy slots.
    private static floatQ BuildRotation(float3 forward, float3 up)
    {
        if (forward.LengthSquared < 1e-8f)
            return floatQ.Identity;
        forward = forward.Normalized;

        floatQ q1 = FabrikSolver.FromToRotation(float3.Backward, forward);
        float3 curUp = q1 * float3.Up;
        float3 upProj = up - forward * float3.Dot(up, forward);
        if (upProj.LengthSquared < 1e-6f)
            return q1;
        floatQ q2 = FabrikSolver.FromToRotation(curUp, upProj.Normalized);
        return q2 * q1;
    }

    private void SolveArm(BodyNode rootNode, Slot? shoulder, Slot? upper, Slot? lower, Slot? hand,
        float upperLen, float lowerLen, float shoulderLen, in Target target, float side)
    {
        if (upper == null || lower == null || hand == null || !target.Valid)
            return;

        float3 root = upper.GlobalPosition;

        // Position weight: blend the goal between a NATURAL hang (down the body frame, not the model's authored bind
        // pose) and the full IK target, so an untracked arm rests by the side instead of frozen wherever the rig was
        // authored. Weight 0 (desktop, no controller) = clean hang; weight 1 = full IK on the tracked hand. -xlinka
        float pw = System.Math.Clamp(target.PositionWeight, 0f, 1f);
        // Untracked rest: use the avatar's AUTHORED arm pose (carried into the current body frame by _bodyTurn)
        // when the rig authors the arms hanging down (relaxed/A-pose) - the two-bone solver then reconstructs the
        // authored elbow bend from the shorter-than-reach target + rest pole, which reads as the model's own idle.
        // For a T-pose-authored rig that pose is a bad idle (arms straight out), so fall back to the computed
        // by-the-side hang. Detect via the rest shoulder->hand offset pointing clearly downward. -xlinka
        LimbRestPose restPose = rootNode == BodyNode.LeftUpperArm ? _lArmRest : _rArmRest;
        bool authoredHangsDown = restPose.Valid && restPose.RootToEnd.LengthSquared > 1e-8f
            && restPose.RootToEnd.Normalized.y < -0.35f;
        float3 restEnd = authoredHangsDown
            ? root + _bodyTurn * restPose.RootToEnd
            : NaturalArmHang(root, upperLen + lowerLen, side);

        // Idle life for untracked arms: the walking counter-swing (opposite phase per side, along the
        // body forward) plus a slow standing drift, so hanging hands are never statues. Amplitudes scale
        // with the arm's reach (avatar-size independent) and fade out as the hand approaches tracked.
        // Deltas on the REST goal only - nothing integrates, tracked hands are untouched. -xlinka
        float armReach = upperLen + lowerLen;
        if (pw < 0.999f && armReach > 1e-4f)
        {
            float life = 1f - pw;
            if (ArmSwingWeight > 1e-3f)
            {
                float swing = MathF.Sin(ArmSwingPhase + (side > 0f ? MathF.PI : 0f))
                            * ArmSwingWeight * ArmSwingReachFraction * armReach;
                restEnd += _curBodyForward * (swing * life);
            }
            if (IdleWeight > 1e-3f)
            {
                float ph = TimeSeconds * 0.45f + (side > 0f ? 2.1f : 0f);
                float fwdDrift = MathF.Sin(ph) + 0.6f * MathF.Sin(ph * 1.7f + 1.3f);
                float sideDrift = MathF.Sin(ph * 0.83f + 0.5f) + 0.5f * MathF.Sin(ph * 1.31f);
                float amp = ArmIdleDriftFraction * armReach * IdleWeight * life;
                restEnd += _curBodyForward * (fwdDrift * amp) + _curBodyRight * (sideDrift * amp * 0.6f * side);
            }
        }

        float3 goalPos = float3.Lerp(restEnd, target.Position, pw);

        // Shoulder solve: swing the clavicle toward the goal so reaching up/forward lifts the whole arm (not just the
        // elbow), then re-anchor the upper-arm root at the swung shoulder tip. The swing is a YAW (around body-up) +
        // PITCH (around body-right) decomposition of the shoulder->goal direction with asymmetric clamps per side
        // (forward/up far, back/down little) plus a small roll-up that grows with reach height. Gated by ShoulderWeight
        // * pw: an UNTRACKED arm (pw=0, desktop) keeps its rest shoulder so both clavicles stay rest-separated instead
        // of collapsing inward toward the hang goal (which stacked the hands at the chest). -xlinka
        if (shoulder != null && !shoulder.IsDestroyed && shoulderLen > 1e-4f
            && _restDir.TryGetValue(shoulder, out var shoulderRestDir)
            && _restRot.TryGetValue(shoulder, out var shoulderRestRot))
        {
            float3 shoulderPos = shoulder.GlobalPosition;
            float gate = System.Math.Clamp(ShoulderWeight * pw, 0f, 1f);
            if (gate > 1e-4f)
            {
                float3 toGoal = goalPos - shoulderPos;
                if (toGoal.LengthSquared > 1e-6f)
                {
                    var shoulderSwing = ComputeShoulderSwing(shoulderRestDir, toGoal.Normalized, side, gate);
                    WriteGlobalRotation(shoulder, shoulderSwing * shoulderRestRot);
                    root = shoulderPos + (shoulderSwing * shoulderRestDir) * shoulderLen;
                }
            }
        }

        // Bend pole starts from the rig's captured bend plane when one exists. If the import gave no usable bend
        // plane, fall back to a conservative behind/down/out elbow so idle arms do not fold into the chest.
        float3 preferredPole = ArmBendPreference(rootNode, upper, lower, hand, side);
        float3 pole = ComputePole(root, goalPos, in restPose, preferredPole, (upperLen + lowerLen) * 0.45f,
            target.HasBendGoal, target.BendGoal, true);

        // Wrist-led elbow: rolling a tracked wrist swings the bend pole around the arm axis (palm-up flips the
        // elbow out, palm-down tucks it in), the way a real forearm follows the hand. The roll is the hand
        // target's twist about the shoulder->goal axis relative to the transported rest wrist, so a neutral
        // wrist leaves the pole untouched. Explicit knee/elbow trackers (bend goals) take precedence. -xlinka
        if (WristBendInfluence > 1e-4f && pw > 1e-4f && !target.HasBendGoal
            && _restRot.TryGetValue(hand, out var wristRest))
        {
            float3 limbAxis = goalPos - root;
            if (limbAxis.LengthSquared > 1e-8f)
            {
                limbAxis = limbAxis.Normalized;
                floatQ delta = target.Rotation * (_bodyTurn * wristRest).Inverse;
                floatQ twist = TwistAround(delta, limbAxis);
                float roll = WrapPi(2f * MathF.Atan2(
                    twist.x * limbAxis.x + twist.y * limbAxis.y + twist.z * limbAxis.z, twist.w));
                float swingAngle = System.Math.Clamp(
                    roll * WristBendInfluence * pw * System.Math.Clamp(target.RotationWeight, 0f, 1f),
                    -MaxWristBendSwing, MaxWristBendSwing);
                if (MathF.Abs(swingAngle) > 1e-3f)
                    pole = root + floatQ.AxisAngleRad(limbAxis, swingAngle) * (pole - root);
            }
        }

        Stretch(root, goalPos, ref upperLen, ref lowerLen);
        FabrikSolver.SolveTwoBone(root, pole, goalPos, upperLen, lowerLen, out var mid, out var end);

        MaybeWritePosition(upper, root);
        ApplySwing(upper, mid - root);
        MaybeWritePosition(lower, mid);
        ApplySwing(lower, end - mid);
        MaybeWritePosition(hand, end);

        // Rotation weight: blend the hand between a natural down/by-side wrist
        // frame and the tracked target. Do not use authored rest rotation for
        // weight 0: many imports store a T/A-pose wrist roll, which leaves
        // head-only avatars with hands visibly sideways even when the arm end
        // is hanging down.
        float rw = System.Math.Clamp(target.RotationWeight, 0f, 1f);
        floatQ restHandRot = NaturalHandRotation(lower, hand, end - mid, side);
        floatQ handRot = floatQ.Slerp(restHandRot, target.Rotation, rw);
        WriteGlobalRotation(hand, handRot);

        // Twist relax: roll the forearm partway toward the hand's twist so it doesn't candy-wrap.
        ApplyTwistRelax(lower, end - mid, handRot);
    }

    // Clavicle swing for the stronger shoulder solve. Decomposes the rest clavicle direction and the shoulder->goal
    // direction into YAW (around body-up) and PITCH (around body-right) in the body frame, takes the deltas, clamps
    // them with asymmetric ranges (a clavicle swings forward/up far more than back/down), rebuilds the clamped target
    // direction, then returns the rest->clamped swing scaled by gate, with a small roll-up that grows with reach
    // height. side = -1 left / +1 right mirrors the ranges. Built from angle decomposition, no LookRotation. -xlinka
    private floatQ ComputeShoulderSwing(float3 restDir, float3 goalDir, float side, float gate)
    {
        // Decompose both directions into yaw (around body-up) + pitch (vertical) in the body frame, with the right
        // component pre-mirrored by side so OUTWARD/abduction is positive yaw on both arms and UP is positive pitch on
        // both. Then clamp the goal's angles to the rest +/- the asymmetric ranges and rebuild the clamped direction
        // from the body basis - the swing is rest->clamped, which is sign-safe (no axis-rotation handedness to get
        // wrong). -xlinka
        DecomposeBodyDir(restDir, side, out float restYaw, out float restPitch);
        DecomposeBodyDir(goalDir, side, out float goalYaw, out float goalPitch);

        // Clamp the goal angles to the rest plus the asymmetric give. Forward/up (positive delta) is generous, back/
        // down (negative delta) is tight - a clavicle swings toward a reach far more than away from one. -xlinka
        float yaw = System.Math.Clamp(goalYaw, restYaw - ShoulderYawBack, restYaw + ShoulderYawForward);
        float pitch = System.Math.Clamp(goalPitch, restPitch - ShoulderPitchDown, restPitch + ShoulderPitchUp);

        // Rebuild the clamped target direction from the (mirrored) body basis. cos(pitch) splits between the
        // horizontal forward/right plane; sin(pitch) is the vertical lift. side un-mirrors the right axis. -xlinka
        float cp = MathF.Cos(pitch);
        float3 clampedDir = _curBodyForward * (cp * MathF.Cos(yaw))
                          + _curBodyRight * (side * cp * MathF.Sin(yaw))
                          + float3.Up * MathF.Sin(pitch);
        if (clampedDir.LengthSquared < 1e-8f)
            return floatQ.Identity;

        floatQ swing = FabrikSolver.FromToRotation(restDir, clampedDir.Normalized);

        // Arm-lift roll: on a high/overhead reach roll the clavicle up about its own (swung) bone axis so the shoulder
        // rolls under the lift instead of only pitching. Grows with how far above the shoulder the goal points; mirror
        // the roll direction by side so both shoulders roll up. -xlinka
        if (ArmLift > 1e-4f)
        {
            float liftFrac = System.Math.Clamp(goalPitch / (MathF.PI * 0.5f), 0f, 1f);
            if (liftFrac > 1e-4f)
            {
                float3 rollAxis = swing * restDir;
                if (rollAxis.LengthSquared > 1e-8f)
                    swing = floatQ.AxisAngleRad(rollAxis.Normalized, ArmLift * liftFrac * side) * swing;
            }
        }

        // Scale the whole swing to the gate (ShoulderWeight * pw) so an untracked arm keeps its rest shoulder.
        return floatQ.Slerp(floatQ.Identity, swing, gate);
    }

    // Split a unit direction into a yaw (around body-up) and pitch (vertical) angle in the body frame, with the right
    // axis pre-mirrored by side so the outward/abduction direction is positive yaw on both arms. -xlinka
    private void DecomposeBodyDir(float3 dir, float side, out float yaw, out float pitch)
    {
        float f = float3.Dot(dir, _curBodyForward);
        float r = float3.Dot(dir, _curBodyRight) * side;
        float u = float3.Dot(dir, float3.Up);
        yaw = MathF.Atan2(r, f);
        float horiz = MathF.Sqrt(MathF.Max(f * f + r * r, 0f));
        pitch = MathF.Atan2(u, horiz);
    }

    private float3 ArmBendPreference(BodyNode rootNode, Slot? upper, Slot? lower, Slot? hand, float side)
    {
        float3 fallback = -_curBodyForward + _curBodyRight * (side * 0.24f) + float3.Down * 0.25f;

        if (TryGuideBend(rootNode, out var guided))
        {
            float f = float3.Dot(guided, _curBodyForward);
            float outward = float3.Dot(guided, _curBodyRight) * side;
            if (f < 0.20f || outward > 0.20f)
                return guided + float3.Down * 0.12f;
        }

        if (TryCurrentBendDirection(upper, lower, hand, out var live))
        {
            float f = float3.Dot(live, _curBodyForward);
            float outward = float3.Dot(live, _curBodyRight) * side;
            if (f < 0.05f || outward > 0.25f)
                return live + float3.Down * 0.12f;
        }

        return fallback;
    }

    private void SolveLeg(BodyNode rootNode, Slot? hip, Slot? knee, Slot? foot, Slot? toe, floatQ toeRel,
        float upperLen, float lowerLen, in Target target)
    {
        if (hip == null || knee == null || foot == null || !target.Valid)
            return;

        float3 root = hip.GlobalPosition;

        // Untracked leg rest = straight DOWN to the floor under the hip (stand), not the authored bind offset. With
        // procedural feet on, the foot is a full-weight ground target so this is only the no-driver fallback. -xlinka
        float pw = System.Math.Clamp(target.PositionWeight, 0f, 1f);
        float3 restEnd = GroundY.HasValue
            ? new float3(root.x, GroundY.Value, root.z)
            : root + float3.Down * ((upperLen + lowerLen) * 0.95f);
        float3 goalPos = float3.Lerp(restEnd, target.Position, pw);

        // Prefer the leg's own visible rest bend when it is clear. Digitigrade avatars can have the lower-leg/hock
        // authored behind the hip-foot line; forcing every rig to human knees-forward makes those legs look reversed.
        // Straight/ambiguous legs still fall back to the normal forward knee. Hardware knee trackers can override
        // through BendGoal. -xlinka
        float3 preferredPole = LegBendPreference(rootNode, hip, knee, foot);
        LimbRestPose restPose = rootNode == BodyNode.LeftUpperLeg ? _lLegRest : _rLegRest;
        float3 pole = ComputePole(root, goalPos, in restPose, preferredPole, (upperLen + lowerLen) * 0.55f,
            target.HasBendGoal, target.BendGoal, false);

        Stretch(root, goalPos, ref upperLen, ref lowerLen);
        FabrikSolver.SolveTwoBone(root, pole, goalPos, upperLen, lowerLen, out var mid, out var end);

        MaybeWritePosition(hip, root);
        ApplySwing(hip, mid - root);
        MaybeWritePosition(knee, mid);
        ApplySwing(knee, end - mid);
        MaybeWritePosition(foot, end);

        bool hasGroundRestRot = _restRot.TryGetValue(foot, out var grFootRot);
        if (target.GroundAlign && (target.HasGroundRotation || hasGroundRestRot))
        {
            WriteGlobalRotation(foot, GroundFootRotation(hasGroundRestRot ? grFootRot : foot.GlobalRotation, in target));
        }
        else
        {
            float rw = System.Math.Clamp(target.RotationWeight, 0f, 1f);
            floatQ restFootRot = _restRot.TryGetValue(foot, out var rfr) ? _bodyTurn * rfr : target.Rotation;
            WriteGlobalRotation(foot, floatQ.Slerp(restFootRot, target.Rotation, rw));
        }

        SolveToe(foot, toe, toeRel, target.StepLift);
    }

    // Plant orientation = the foot's AUTHORED rest pose, yawed (about world up) so its forward - the
    // foot->toe line when the rig has toes - follows the gait facing, then tilted Up->normal for slopes.
    // The authored pitch/roll is never touched: aligning the bone's +Y to the ground normal instead
    // rolled every paw whose Y axis runs along the segment (digitigrade rigs) sole-backward. -xlinka
    private floatQ GroundFootRotation(floatQ restFootRot, in Target target)
    {
        float3 normal = target.GroundNormal.LengthSquared > 1e-6f ? target.GroundNormal.Normalized : float3.Up;

        float3 forward = target.HasGroundRotation
            ? target.GroundRotation * float3.Backward
            : target.GroundForward.LengthSquared > 1e-6f ? target.GroundForward : _curBodyForward;
        forward.y = 0f;
        if (forward.LengthSquared < 1e-6f)
        {
            forward = _curBodyForward;
            forward.y = 0f;
            if (forward.LengthSquared < 1e-6f)
                forward = _restBodyForward;
        }
        forward = forward.LengthSquared > 1e-6f ? forward.Normalized : float3.Backward;

        // Yaw the AUTHORED rest foot pose by the BODY's turn (rest body forward -> gait forward), so each
        // foot keeps its authored splay/toe-out. Aiming every toe exactly down the gait line made both
        // feet identical and robotic. Both vectors flattened = pure yaw; the Up hint keeps the
        // antiparallel case a yaw too.
        float3 restBody = _restBodyForward;
        restBody.y = 0f;
        restBody = restBody.LengthSquared > 1e-6f ? restBody.Normalized : float3.Backward;
        floatQ rot = FabrikSolver.FromToRotation(restBody, forward, float3.Up) * restFootRot;

        // Slope tilt: identity on flat ground, clamped so a bad probe can't fold the foot.
        float upDot = System.Math.Clamp(float3.Dot(float3.Up, normal), -1f, 1f);
        if (upDot < 0.9999f)
        {
            float3 axis = float3.Cross(float3.Up, normal);
            if (axis.LengthSquared > 1e-8f)
            {
                float angle = MathF.Min(MathF.Acos(upDot), MathF.PI * 0.25f);
                rot = floatQ.AxisAngleRad(axis.Normalized, angle) * rot;
            }
        }

        return rot;
    }

    // Toe rides the foot at its captured relative orientation, curling around the foot's right axis
    // as the foot swings through a step (StepLift). Tracked/planted feet leave it at the rest ride.
    private void SolveToe(Slot foot, Slot? toe, floatQ toeRel, float stepLift)
    {
        if (foot == null || toe == null || toe.IsDestroyed)
            return;

        floatQ footRot = foot.GlobalRotation;
        floatQ toeRot = footRot * toeRel;
        if (stepLift > 1e-3f)
        {
            float3 right = footRot * float3.Right;
            toeRot = floatQ.AxisAngle(right, stepLift * ToeCurlAngle) * toeRot;
        }
        WriteGlobalRotation(toe, toeRot);
    }

    // Pole vector for two-bone IK. Primary path is the clean-room version of the stable rest-bend behavior:
    // transport the captured root->mid offset by the rotation from rest root->end to current root->target. That keeps
    // elbows and digitigrade hocks on the side the avatar was authored with. Only if the captured bend is too straight
    // do we fall back to body-frame preferences.
    private float3 ComputePole(float3 root, float3 targetPos, in LimbRestPose restPose, float3 preferredBendDirection,
        float poleDistance, bool hasBendGoal, float3 bendGoal, bool allowOpposedRestPole)
    {
        float3 limb = targetPos - root;
        float dist = MathF.Max(poleDistance, 0.05f);
        float3 pole;

        if (restPose.Valid && limb.LengthSquared > 1e-8f && restPose.RootToEnd.LengthSquared > 1e-8f)
        {
            floatQ swing = FabrikSolver.FromToRotation(restPose.RootToEnd, limb, restPose.PlaneNormal);
            float3 transportedMid = root + swing * restPose.RootToMid;
            float3 limbDir = limb.Normalized;
            float3 bend = transportedMid - root;
            bend -= limbDir * float3.Dot(bend, limbDir);
            float minBend = MathF.Max(restPose.RootToEnd.Length * 0.015f, 0.003f);
            if (bend.LengthSquared > minBend * minBend)
            {
                float3 bendDir = bend.Normalized;
                float3 prefDir = preferredBendDirection.LengthSquared > 1e-8f
                    ? preferredBendDirection.Normalized
                    : bendDir;
                // Digitigrade legs can carry a stale forward rest pole; when the chosen leg bend is clearly rear,
                // let that preference win instead of accepting the captured pole. - xlinka
                bool acceptRestPole = allowOpposedRestPole || float3.Dot(bendDir, prefDir) > -0.15f;
                if (acceptRestPole)
                {
                    float restDistance = MathF.Max(restPose.BendDistance, dist * 0.35f);
                    pole = root + bendDir * MathF.Max(restDistance, 0.05f);
                    if (hasBendGoal)
                        pole = float3.Lerp(pole, bendGoal, System.Math.Clamp(BendGoalWeight, 0f, 1f));
                    return pole;
                }
            }
        }

        float3 limbDirFallback = limb;
        float3 pref = preferredBendDirection.LengthSquared > 1e-8f ? preferredBendDirection.Normalized : _curBodyForward;
        float3 poleDir = pref;

        if (limbDirFallback.LengthSquared > 1e-8f)
        {
            limbDirFallback = limbDirFallback.Normalized;
            poleDir = pref - limbDirFallback * float3.Dot(pref, limbDirFallback);
            if (poleDir.LengthSquared < 1e-8f)
            {
                poleDir = float3.Cross(_curBodyRight, limbDirFallback);
                if (poleDir.LengthSquared < 1e-8f)
                    poleDir = float3.Cross(_curBodyForward, limbDirFallback);
                if (float3.Dot(poleDir, pref) < 0f)
                    poleDir = -poleDir;
            }
        }

        pole = root + (poleDir.LengthSquared > 1e-8f ? poleDir.Normalized : pref) * dist;
        if (hasBendGoal)
            pole = float3.Lerp(pole, bendGoal, System.Math.Clamp(BendGoalWeight, 0f, 1f));
        return pole;
    }

    private float3 LegBendPreference(BodyNode rootNode, Slot? hip, Slot? knee, Slot? foot)
    {
        bool preferRear = PreferRearLegBend();
        float3 rear = -_curBodyForward + float3.Down * 0.20f;
        float3 fallback = preferRear ? rear : _curBodyForward + float3.Down * 0.20f;

        if (TryGuideBend(rootNode, out var guided))
        {
            float f = float3.Dot(guided, _curBodyForward);
            if (!preferRear || f < -0.05f)
                return guided + float3.Down * 0.10f;
        }

        if (TryCurrentBendDirection(hip, knee, foot, out var live))
        {
            // Side-heavy leg offsets are often label noise. A clear front/back component is a real knee/hock signal.
            float f = float3.Dot(live, _curBodyForward);
            float r = float3.Dot(live, _curBodyRight);
            if (MathF.Abs(f) > 0.025f && MathF.Abs(f) > MathF.Abs(r) * 0.35f)
            {
                if (!preferRear || f < -0.05f)
                    return live + float3.Down * 0.10f;
            }
        }

        return fallback;
    }

    private bool PreferRearLegBend()
    {
        if (_rig != null && _rig.ForwardFlipped.Value)
            return true;

        if (_lHip == null || _rHip == null || _lHip.IsDestroyed || _rHip.IsDestroyed)
            return false;

        float3 mid = (_lHip.GlobalPosition + _rHip.GlobalPosition) * 0.5f;
        bool labeledLeftOnRight = float3.Dot(_lHip.GlobalPosition - mid, _curBodyRight) > 0f;
        bool labeledRightOnLeft = float3.Dot(_rHip.GlobalPosition - mid, _curBodyRight) < 0f;
        return labeledLeftOnRight && labeledRightOnLeft;
    }

    private bool TryGuideBend(BodyNode rootNode, out float3 bend)
    {
        bend = float3.Zero;
        if (_rig == null || !_rig.TryGetLimbPoseGuide(rootNode, out var guide) || !guide.HasBendDirection)
            return false;

        bend = BodyDirection(guide.BendDirectionBody);
        return bend.LengthSquared > 1e-8f;
    }

    private float3 BodyDirection(float3 bodyDir)
    {
        float3 dir = _curBodyForward * bodyDir.x + _curBodyRight * bodyDir.y + float3.Up * bodyDir.z;
        return dir.LengthSquared > 1e-8f ? dir.Normalized : float3.Zero;
    }

    private static bool TryCurrentBendDirection(Slot? rootSlot, Slot? midSlot, Slot? endSlot, out float3 bendDir)
    {
        bendDir = float3.Zero;
        if (rootSlot == null || midSlot == null || endSlot == null
            || rootSlot.IsDestroyed || midSlot.IsDestroyed || endSlot.IsDestroyed)
            return false;

        float3 root = rootSlot.GlobalPosition;
        float3 end = endSlot.GlobalPosition;
        float3 limb = end - root;
        if (limb.LengthSquared < 1e-8f)
            return false;

        float3 limbDir = limb.Normalized;
        float3 bend = midSlot.GlobalPosition - root;
        bend -= limbDir * float3.Dot(bend, limbDir);
        if (bend.LengthSquared < 1e-8f)
            return false;

        bendDir = bend.Normalized;
        return true;
    }

    // Allow a limb to over-extend slightly (up to MaxStretch) when the target is past its reach,
    // instead of hard-clamping - keeps the hand/foot on target without a rigid snap.
    private void Stretch(float3 root, float3 targetPos, ref float upperLen, ref float lowerLen)
    {
        float reach = upperLen + lowerLen;
        if (reach < 1e-5f)
            return;
        float dist = float3.Distance(root, targetPos);
        float ratio = dist / reach;

        // Ease the stretch IN smoothly starting before full reach, instead of doing nothing until dist>reach and
        // then jumping to a linear scale (that hard derivative jump at exactly arm's length is a visible elbow/knee
        // pop). smoothstep over [kStart, MaxStretch] gives a C1-continuous "give" - the limb lengthens a hair as it
        // approaches full extension so it eases out rather than snapping straight. -xlinka
        const float kStart = 0.9f;
        if (ratio <= kStart)
            return;
        float t = System.Math.Clamp((ratio - kStart) / (MaxStretch - kStart), 0f, 1f);
        float ease = t * t * (3f - 2f * t);
        float scale = 1f + (MaxStretch - 1f) * ease;
        upperLen *= scale;
        lowerLen *= scale;
    }

    // Roll a forearm/lower-arm partway toward the hand's twist (rotation around the bone axis), so
    // the wrist roll is shared instead of snapping entirely at the wrist.
    private void ApplyTwistRelax(Slot forearm, float3 forearmDir, in floatQ handRot)
    {
        if (forearm == null || TwistRelax <= 0f || forearmDir.LengthSquared < 1e-8f)
            return;

        floatQ forearmRot = forearm.GlobalRotation;
        floatQ delta = handRot * forearmRot.Inverse;
        floatQ twist = TwistAround(delta, forearmDir.Normalized);
        floatQ partial = floatQ.Slerp(floatQ.Identity, twist, System.Math.Clamp(TwistRelax, 0f, 1f));
        WriteGlobalRotation(forearm, partial * forearmRot);
    }

    // Swing-twist decomposition: the component of q that rotates around axis.
    private static floatQ TwistAround(in floatQ q, in float3 axis)
    {
        float proj = q.x * axis.x + q.y * axis.y + q.z * axis.z;
        float lenSq = proj * proj + q.w * q.w;
        if (lenSq < 1e-10f)
            return floatQ.Identity;
        return new floatQ(axis.x * proj, axis.y * proj, axis.z * proj, q.w).Normalized;
    }

    // Swing a bone's rest direction onto the solved bone direction.
    private void ApplySwing(Slot bone, float3 solvedDir)
    {
        if (bone == null || solvedDir.LengthSquared < 1e-8f)
            return;
        if (!_restDir.TryGetValue(bone, out var restDir) || !_restRot.TryGetValue(bone, out var restRot))
            return;

        var swing = FabrikSolver.FromToRotation(restDir, solvedDir);
        WriteGlobalRotation(bone, swing * restRot);
    }

    // Spine variant of ApplySwing: align the length axis to the solved joint direction, then twist about
    // that axis so the bone keeps its AUTHORED offset from the body forward - the rest body forward is
    // transported through the swing and rolled onto the current body forward. Twisting the bone's rendered
    // -Z onto the body absolutely assumed belly-on--Z and wrung arbitrary-convention spines sideways; this
    // keeps the authored torso intact at rest and turns it exactly with the body. -xlinka
    private void ApplySpineSwing(Slot bone, float3 solvedDir, float3 bodyForward)
    {
        if (bone == null || solvedDir.LengthSquared < 1e-8f)
            return;
        if (!_restDir.TryGetValue(bone, out var restDir) || !_restRot.TryGetValue(bone, out var restRot))
            return;

        floatQ swing = FabrikSolver.FromToRotation(restDir, solvedDir);
        floatQ swung = swing * restRot;

        if (bodyForward.LengthSquared > 1e-6f)
        {
            float3 axis = solvedDir.Normalized;
            float3 transported = swing * _restBodyForward;
            float3 current = transported - axis * float3.Dot(transported, axis);
            float3 target = bodyForward - axis * float3.Dot(bodyForward, axis);
            if (current.LengthSquared > 1e-6f && target.LengthSquared > 1e-6f)
            {
                current = current.Normalized;
                target = target.Normalized;
                float angle = MathF.Acos(System.Math.Clamp(float3.Dot(current, target), -1f, 1f));
                if (float3.Dot(float3.Cross(current, target), axis) < 0f)
                    angle = -angle;
                if (MathF.Abs(angle) > 1e-4f)
                    swung = floatQ.AxisAngleRad(axis, angle) * swung;
            }
        }

        WriteGlobalRotation(bone, swung);
    }
}
