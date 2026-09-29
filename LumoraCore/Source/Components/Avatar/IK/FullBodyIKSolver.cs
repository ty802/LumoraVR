// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

// Engine-side full-body IK driver. Owns a HumanoidRig and, each frame:
//   - resets the bones to the bind pose and measures lengths, heights and the bind skeleton frame,
//   - derives a SOLVE FRAME from this frame's targets alone (up = head over the hips or feet, forward from the
//     foot rotations and a tracked pelvis, sign-fixed against the head) and turns the bind skeleton into it,
//   - places the hips from the feet (each upper leg moves a tenth of its foot's remaining travel, the hip line
//     turns with them) unless a pelvis tracker rules them, then solves the spine to the head,
//   - solves each arm with a shoulder swing, analytic two-bone IK and a swivel model for the elbow,
//   - solves each leg analytically with the knee bent toward the solve forward, and gives the foot bone the
//     target rotation in full, pitch included.
// Body facing is a pure function of the targets: nothing is remembered between frames and there is no sign
// flag, so a body can never wear backwards. Tunables are public instance fields the component drives from sync
// values at runtime. -xlinka
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

        // Optional elbow/knee steer. Arms take only the DIRECTION of BendGoal around the root->target axis; legs
        // take the point itself as the bend target.
        public bool HasBendGoal;
        public float3 BendGoal;

        // Rotation is a FOOT-FRAME rotation (identity = flat, facing the body's forward) rather than a bone
        // rotation: the solver carries it through the bind foot pose so any bone axis convention works. The
        // gait sets this; tracked feet leave it false and supply the bone rotation directly.
        public bool FootFrameRotation;

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
    public float SpineStiffness = 0.2f;    // base pull toward the straight hips->head line
    // A supplied bend goal is an elbow/knee TRACKER (or the component saying "put it here"), so it decides the
    // swivel outright. 0.5 was a position lerp between the pole and the goal, which meant a real tracker only
    // ever got half a vote and the result depended on how far the goal sat from the root. -xlinka
    public float BendGoalWeight = 1f;      // 0..1 swivel blend from the computed pole toward the bend goal
    public float TwistRelax = 0.5f;        // forearm roll distributed toward the hand

    // Elbow swivel from the HAND POSITION IN THE SHOULDER FRAME. The pole is a point on the circle around the
    // shoulder->hand axis, parameterised by an angle: 0 is the base direction (down for a reach in front, back
    // for a hanging arm, both carried by one rotation of the body frame onto the axis), positive swings the
    // elbow OUTWARD, negative inward. The angle is a sum of three clamped linear terms on where the hand sits
    // relative to the shoulder, each normalised by arm reach so the same numbers fit every avatar size:
    //   height:  a hand lifted from the hip to overhead walks the elbow from down, to out, to slightly up;
    //   reach:   a hand pulled in toward the chest flares the elbow out, an extended arm lets it drop;
    //   outward: a cross-body hand pushes the elbow out to clear the torso, a hand far to the side drops it.
    // Then a per-avatar offset, then a clamp so the elbow never ends up above or in front of the axis.
    // This replaces transporting the rest bend by the minimal rotation for big swings: from a hanging rest to
    // a hand at chest height that is a 90-135 degree swing whose rotation axis is whatever the cross product
    // happened to give, and it left the elbow winged behind the torso. Radians; the component converts. -xlinka
    public float SwivelBase = 0.611f;         // ~35 deg: swivel with the hand at shoulder height, arm half-extended
    public float SwivelOffset = 0f;           // per-avatar trim added before the clamp
    public float SwivelHeightGain = 1.309f;   // ~75 deg per reach of hand height above the shoulder
    public float SwivelHeightMin = -0.436f;   // ~-25 deg: a hand at the hip can only tuck the elbow this far
    public float SwivelHeightMax = 1.134f;    // ~65 deg: an overhead hand can only lift it this far
    public float SwivelReachStart = 0.6f;     // reach fraction where pulling the hand in starts to flare the elbow
    public float SwivelReachGain = 1.047f;    // ~60 deg per reach of pull-in below SwivelReachStart
    public float SwivelReachMax = 0.524f;     // ~30 deg cap on the flare
    public float SwivelOutwardStart = 0.1f;   // outward fraction that is neutral (the hand's natural line)
    public float SwivelOutwardGain = 0.873f;  // ~50 deg per reach of cross-body travel
    public float SwivelOutwardMin = -0.349f;  // ~-20 deg: a hand far to the side drops the elbow this much
    public float SwivelOutwardMax = 0.524f;   // ~30 deg: a cross-body hand lifts it this much
    public float SwivelMin = -0.436f;         // ~-25 deg: never further inward than this
    public float SwivelMax = 2.443f;          // ~140 deg: never above the axis (180 would be elbow-up)
    // The authored rest bend still owns SMALL swings (a hanging arm swinging as you walk keeps the artist's
    // elbow); it is faded out across this band of swing-from-rest so the hand swivel takes over big reaches
    // without a step. Band placement, measured: the walk counter-swing is ~23 deg from the hang and the crouch
    // fold ~37 deg (both stay authored), while an A-pose hang to a tool held at chest height is ~59 deg, and
    // that is the pose the hand swivel exists for, so the band has to be mostly gone by then. -xlinka
    public float RestPoleSwingKeep = 0.785f;  // ~45 deg: rest bend fully trusted below this swing from rest
    public float RestPoleSwingDrop = 1.134f;  // ~65 deg: and ignored above this
    // Additive cosmetic offset on the hips. Y carries the vertical body bob (dip while a foot is airborne);
    // X/Z carry the HORIZONTAL body settle that eases the torso toward the centroid of the planted feet (biased to
    // the support foot). Both are computed by the component (smoothed there) and pushed in each solve - the gait
    // step-trigger reads the user root, NOT this, so the settle never feeds back into stepping. -xlinka
    public float3 LocomotionOffset;        // added to the hips (body bob + horizontal settle / weight shift)
    public float? GroundY;                 // floor plane: when set, the hips are kept within leg reach of it

    public float MoveBodyBackWhenCrouching = 0.35f; // shift hips back as the head dips so the knees don't shoot forward

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

    // LOCOMOTION ANIMATION.
    //
    // The arms are a simulation rather than a sine (see HandMotion), so the solver needs to know what
    // the body is doing rather than only how fast the phase is turning. All of it is pushed per solve
    // by the component, which owns the smoothing.
    public float3 BodyVelocity;          // body frame, metres per second: forward/right/up
    public float RunBlend;               // 0 walking, 1 running; blends the hand feel between them
    public float AirBlend;               // 0 on the ground, 1 airborne or gliding
    public float SwayWeight = 1f;        // scales the standing sway
    public float HandMotionWeight = 1f;  // 0 disables the whole layer, for a seat or a cutscene
    public float DeltaTime = 1f / 60f;   // the simulation step

    // Head bob. The head is the one part of a body that a walk moves in a way the eye notices, and
    // ours did not move at all: the avatar glided with a perfectly level skull. Applied to the head
    // BONE, never to the view - a camera that bobs is a camera that makes people ill, and the person
    // wearing the avatar is the one person who cannot see their own head anyway. -xlinka
    public float HeadBobVertical;        // metres at full stride
    public float HeadBobHorizontal;      // metres at full stride
    public float HeadBobRoll;            // radians of lean at full stride
    public float HeadBobWeight;          // 0..1, speed-faded by the component

    // How close two feet may come before they are pushed apart, in metres at avatar scale. Without it
    // a turn on the spot walks one foot straight through the other.
    public float FootAvoidanceRadius;

    private float3 _curBodyCentre;
    private float _torsoHalfWidth;

    private HandMotion _leftHand;
    private HandMotion _rightHand;

    // The hand offsets are read back by the component for diagnostics.
    public float3 LeftHandOffset => _leftHand.Offset;
    public float3 RightHandOffset => _rightHand.Offset;

    // Wrist roll steering the elbow (0..1): rolling a tracked wrist swings the bend pole around the arm
    // axis, the way a real forearm follows the hand. 0 disables.
    public float WristBendInfluence = 0.5f;
    // Wrist roll below this (radians) does not move the elbow at all; only the roll BEYOND it steers. A tracked
    // wrist is never perfectly neutral, and without a dead zone every small roll walked the elbow around the
    // arm axis and fought the hand-position swivel. -xlinka
    public float WristBendDeadZone = 0.698f;  // ~40 deg
    // Chest yaw toward tracked hands (0..1): a cross-body or two-handed reach carries the upper torso
    // instead of leaving all the stretch to the shoulder. 0 disables; untracked hands contribute nothing.
    public float ChestFollowHands = 0.2f;

    // What decided each arm's elbow pole on the last solve, for the ARMDIAG line. Source is the dominant voter:
    // Rest when the authored bend carried more than half, Goal when a bend goal did, Hand otherwise. -xlinka
    public enum PoleSource : byte { None, Rest, Hand, Goal }

    public struct PoleDebug
    {
        public PoleSource Source;
        public float SwivelRad;      // final pole angle in the arm's swivel frame (after rest/goal/wrist)
        public float HandSwivelRad;  // the hand-position term alone, clamped
        public float RestWeight;     // how much the authored rest bend voted (0..1)
    }

    public PoleDebug LeftArmPole { get; private set; }
    public PoleDebug RightArmPole { get; private set; }

    // The solve frame this frame, from the targets alone. Up runs from the hips (or the feet average) to the head,
    // forward comes from the foot rotations (and the pelvis when tracked) rejected against up. The offset is the
    // rotation from the solve frame onto the bind skeleton frame; the skeleton is turned by its inverse about its
    // hips so the bind pose already faces the targets before anything is solved, and the effector centre is where
    // the hips sit before the feet move them. -xlinka
    public float3 SolveForward { get; private set; } = float3.Backward;
    public float3 SolveUp { get; private set; } = float3.Up;
    public floatQ SolveRotationOffset { get; private set; } = floatQ.Identity;
    public float3 SolveEffectorCenter { get; private set; }
    public float3 SolveSkeletonCenter { get; private set; }
    public float3 SkeletonForward => _skeletonForward;

    private const float ToeCurlAngle = 0.5f;     // radians the toe curls at peak step lift
    private const float StiffnessTaper = 0.6f;   // upper spine is (1 - this) as stiff as the base
    private const float MaxChestYaw = 0.6f;      // rad (~34 deg) chest wind-up cap vs the hips
    private const float MaxWristBendSwing = 1.0f; // rad cap so an extreme roll can't flip the elbow over
    private const float BreathPeriodSeconds = 4.3f;   // full breath cycle
    private const float BreathPitchRad = 0.021f;      // chest pitch amplitude at BreathingWeight 1 (~1.2 deg)
    private const float ArmSwingReachFraction = 0.30f; // walking hand swing amplitude as a fraction of arm reach
    // Share of a foot's remaining travel to its target that its upper leg moves by when the hips are placed from
    // the feet. The spine then decides how much of that the head lets through. -xlinka
    private const float HipFollowFootFraction = 0.1f;

    private HumanoidRig _rig = null!;
    private bool _captured;

    // Standing metrics from the upright bind pose (live scale).
    private float _restStandHeight;                    // hips height above the sole plane in the rest pose
    private float _restHeadHeight;                     // head height above the sole plane in the rest pose

    // Read by the one-shot pose diagnostic. Everything the solver believes about how this body stands
    // comes from these, and all of them come from whatever pose the bones happened to be in when the
    // solver initialised - so when a body stands wrong, this is where to look first.
    public float RestStandHeight => _restStandHeight;
    public float RestHeadHeight => _restHeadHeight;
    // Foot BONE height above the sole plane at rest (live scale), per foot. The gait reads these so a
    // planted foot bone sits at its authored height instead of burying the paw in the floor. -xlinka
    public float LeftFootGroundClearance { get; private set; }
    public float RightFootGroundClearance { get; private set; }
    private const float CrouchHipsMinFraction = 0.34f; // deep squat: hips descend toward the heels
    private const float CrouchSpineSlack = 0.92f;      // spine may shorten this much at full crouch (hunch + back-lean)
    // How far one vertebra may deviate from the one below it. Roughly what a human lumbar/thoracic
    // joint gives before the next one has to take over; loose enough that a deliberate hunch still
    // reads, tight enough that the chain cannot double back on itself.
    private const float SpineMaxBendPerJoint = 0.45f;  // radians, ~26 degrees

    // A back arches backwards far less than it folds forwards. Roughly the ratio the anatomical ranges
    // give across lumbar and thoracic, and it is the only direction whose limit changed when per-joint
    // constraints landed: forward and lateral keep the value the crouch work was tuned against.
    private const float SpineExtensionFraction = 0.45f;
    // Flat body frame for the arms and the crouch shift: the solve forward on the ground plane and its right.
    private float3 _curBodyForward = float3.Backward;
    private float3 _curBodyRight = float3.Right;
    private float _crouchLerp;                         // 0 standing .. 1 fully crouched

    // Crouching folds the arms IN FRONT of the body rather than leaving them hanging through the legs,
    // and quiets the walk swing while they are there. Fractions of arm reach, so they scale with the
    // avatar. Forward is the big one: it is what stops the hands intersecting the thighs. -xlinka
    private const float CrouchArmForward = 0.34f;      // of arm reach, along body forward
    private const float CrouchArmUp = 0.28f;           // of arm reach, up toward the chest
    private const float CrouchArmIn = 0.08f;           // of arm reach, toward the body midline
    private const float CrouchSwingDamp = 0.25f;       // walk swing left at full crouch

    // Bind skeleton frame (upright bind pose, world): up from the feet to the head, forward from the left-right
    // lines of the hips, arms, feet and hands crossed with up and sign-fixed against the toes. The flat frame
    // inverse carries foot-frame and view rotations onto bones whose axes are whatever the rig authored. -xlinka
    private float3 _skeletonForward = float3.Backward;
    private float3 _skeletonUp = float3.Up;
    private floatQ _skeletonFrameFlatInv = floatQ.Identity;
    private float3 _bindHipsPos;
    private floatQ _bindHipsRot = floatQ.Identity;
    private float3 _bindHeadPos;
    private floatQ _bindHeadRot = floatQ.Identity;
    private float3 _bindLFootPos, _bindRFootPos;
    private floatQ _bindLFootRot = floatQ.Identity, _bindRFootRot = floatQ.Identity;

    // Spine chain (filtered to existing bones, hips..head).
    private readonly List<Slot> _spine = new();
    private float[] _spineLengths = null!;
    private float3[] _spineJoints = null!;
    private FabrikSolver.JointLimit[] _spineLimits = null!;

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

    private LimbRestPose _lArmRest, _rArmRest;

    // Rest bone lengths.
    private float _lUpperArmLen, _lLowerArmLen, _lShoulderLen;
    private float _rUpperArmLen, _rLowerArmLen, _rShoulderLen;
    private float _lUpperLegLen, _lLowerLegLen;
    private float _rUpperLegLen, _rLowerLegLen;
    private float _spineTotalLength;

    // Bind-pose (default) local transforms of every driven bone, plus the flat bone list. Each frame
    // FixTransforms resets the bones to these before anything is measured, so lengths are always taken
    // from the clean rest pose at the LIVE avatar scale - never from last frame's solved pose, and never
    // from a stale capture-time scale. This is why the torso no longer stretches/offsets when the avatar
    // is rescaled to the user after the solver was set up. -xlinka
    private readonly List<Slot> _bones = new();
    private readonly Dictionary<Slot, float3> _bindLocalPos = new();
    private readonly Dictionary<Slot, floatQ> _bindLocalRot = new();

    // Rest toe orientation relative to the foot.
    private floatQ _lToeRel = floatQ.Identity, _rToeRel = floatQ.Identity;

    // Rest bone directions (bone -> child) and rotations, captured in the SOLVE frame each solve (after the bind
    // skeleton has been turned to face the targets), used to derive swings.
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

    // The view rotation applied over the head bone's authored offset from the bind facing, so the bone keeps its
    // own axes while looking exactly where the view does.
    private void WriteHeadFacing(Slot head, in floatQ lookRotation)
    {
        if (head == null || head.IsDestroyed)
            return;
        WriteGlobalRotation(head, lookRotation * _skeletonFrameFlatInv * _bindHeadRot);
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

        // Built once, not per frame. Forward flex and lateral bend keep the tuned SpineMaxBendPerJoint
        // exactly as they were, because the crouch depth work is measured against them and a spine really
        // does flex that far. Only EXTENSION is tightened: the old symmetric cone let the chain arch
        // backwards as far as it could fold forwards, which no back does, and it is why a head target
        // behind the hips produced a bend nothing anatomical would make. -xlinka
        _spineLimits = new FabrikSolver.JointLimit[_spine.Count - 1];
        for (int i = 0; i < _spineLimits.Length; i++)
        {
            _spineLimits[i] = new FabrikSolver.JointLimit(
                SpineMaxBendPerJoint,
                SpineMaxBendPerJoint * SpineExtensionFraction,
                SpineMaxBendPerJoint);
        }

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
        // solver has valid lengths and standing metrics before the first solve.
        StoreDefaultLocalState();
        MeasureBind();
        CaptureRest();

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

    // Measure bone lengths, standing heights and the bind skeleton frame from the UPRIGHT bind pose (which
    // FixTransforms just restored). Because the bones carry the live avatar scale, every length is correct for
    // the rendered avatar this frame - there is no stored capture scale to drift from. -xlinka
    private void MeasureBind()
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

        // Rest toe orientation relative to the foot, so the toe can ride the foot + curl on steps.
        _lToeRel = ToeRel(_lFoot, _lToe);
        _rToeRel = ToeRel(_rFoot, _rToe);

        var hipsBone = _spine[0];
        var headBone = _spine[_spine.Count - 1];
        _bindHipsPos = hipsBone.GlobalPosition;
        _bindHipsRot = hipsBone.GlobalRotation;
        _bindHeadPos = headBone.GlobalPosition;
        _bindHeadRot = headBone.GlobalRotation;
        Slot? lFootBone = _lFoot != null && !_lFoot.IsDestroyed ? _lFoot : null;
        Slot? rFootBone = _rFoot != null && !_rFoot.IsDestroyed ? _rFoot : null;
        bool hasLFoot = lFootBone != null;
        bool hasRFoot = rFootBone != null;
        _bindLFootPos = lFootBone?.GlobalPosition ?? _bindHipsPos;
        _bindRFootPos = rFootBone?.GlobalPosition ?? _bindHipsPos;
        _bindLFootRot = lFootBone?.GlobalRotation ?? floatQ.Identity;
        _bindRFootRot = rFootBone?.GlobalRotation ?? floatQ.Identity;

        ComputeSkeletonFrame(hasLFoot && hasRFoot);

        // Measure the torso width at bind. Its centre is taken from the solved body below: using
        // the bind centre after moving the skeleton pushes one resting hand away from an old position.
        _torsoHalfWidth = _lUpper != null && _rUpper != null && !_lUpper.IsDestroyed && !_rUpper.IsDestroyed
            ? float3.Distance(_lUpper.GlobalPosition, _rUpper.GlobalPosition) * 0.42f
            : 0f;

        _authoredArmsHang = ArmBindIsHang(_lUpper, _lHand, _lUpperArmLen + _lLowerArmLen)
                         && ArmBindIsHang(_rUpper, _rHand, _rUpperArmLen + _rLowerArmLen);

        // Natural standing heights, measured from the SOLE PLANE: the lowest foot/toe bone in the rest
        // pose (the bind pose stands the model on the ground, so that IS the sole), at the live scale.
        // The floor-pin uses these so the body stands at its real height (anti-float) and untracked
        // legs reach the ground. Falls back to leg length when the feet aren't mapped. -xlinka
        float hipsRestY = _bindHipsPos.y;
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
        _restHeadHeight = hasFootRest ? System.Math.Max(_bindHeadPos.y - soleRestY, 0f) : 0f;

        // How far each foot BONE sits above the sole plane at rest. The gait plants the BONE this much
        // above the floor - planting it AT the floor buried the paws/soles by exactly this amount. -xlinka
        LeftFootGroundClearance = hasLFoot && hasFootRest ? System.Math.Max(_bindLFootPos.y - soleRestY, 0f) : 0f;
        RightFootGroundClearance = hasRFoot && hasFootRest ? System.Math.Max(_bindRFootPos.y - soleRestY, 0f) : 0f;
    }

    // Bind skeleton orientation from bone POSITIONS: up runs from the feet average to the head, the left-right
    // line sums every labelled pair the rig has, and the toes settle the sign so a rig whose left and right
    // labels sit on the swapped physical sides still reads its real front. No bone rotation is consulted: an
    // authored hips or spine rotation is exactly what points the wrong way on imported anthro rigs. -xlinka
    private void ComputeSkeletonFrame(bool hasFeet)
    {
        float3 up = hasFeet
            ? _bindHeadPos - (_bindLFootPos + _bindRFootPos) * 0.5f
            : _bindHeadPos - _bindHipsPos;
        if (up.LengthSquared < 1e-8f)
            up = float3.Up;
        up = up.Normalized;

        float3 rightLine = float3.Zero;
        AccumulateRight(_lHip, _rHip, ref rightLine);
        AccumulateRight(_lUpper, _rUpper, ref rightLine);
        AccumulateRight(_lFoot, _rFoot, ref rightLine);
        AccumulateRight(_lHand, _rHand, ref rightLine);

        float3 forward = rightLine.LengthSquared > 1e-8f ? float3.Cross(up, rightLine.Normalized) : float3.Zero;
        if (forward.LengthSquared < 1e-8f)
        {
            var guess = _rig.GuessForwardAxis();
            forward = guess.HasValue && guess.Value.LengthSquared > 1e-8f
                ? guess.Value
                : _bindHipsRot * float3.Backward;
        }
        forward -= up * float3.Dot(forward, up);
        if (forward.LengthSquared < 1e-8f)
            forward = float3.Cross(up, float3.Right);
        forward = forward.Normalized;

        if (TryToeDirection(out float3 toes) && float3.Dot(forward, toes) < 0f)
            forward = -forward;

        _skeletonUp = up;
        _skeletonForward = forward;

        float3 flat = forward;
        flat.y = 0f;
        if (flat.LengthSquared < 1e-8f)
            flat = float3.Cross(float3.Up, float3.Cross(forward, float3.Up));
        if (flat.LengthSquared < 1e-8f)
            flat = float3.Backward;
        _skeletonFrameFlatInv = BuildRotation(flat.Normalized, float3.Up).Inverse;
    }

    private static void AccumulateRight(Slot? left, Slot? right, ref float3 sum)
    {
        if (left == null || right == null || left.IsDestroyed || right.IsDestroyed)
            return;
        float3 d = right.GlobalPosition - left.GlobalPosition;
        if (d.LengthSquared > 1e-8f)
            sum += d.Normalized;
    }

    private bool TryToeDirection(out float3 dir)
    {
        float3 sum = float3.Zero;
        Accumulate(_lFoot, _lToe);
        Accumulate(_rFoot, _rToe);
        dir = sum;
        return sum.LengthSquared > 1e-8f;

        void Accumulate(Slot? foot, Slot? toe)
        {
            if (foot == null || toe == null || foot.IsDestroyed || toe.IsDestroyed)
                return;
            float3 d = toe.GlobalPosition - foot.GlobalPosition;
            d.y = 0f;
            if (d.LengthSquared > 1e-8f)
                sum += d.Normalized;
        }
    }

    // Rest directions, rotations and limb rest poses from the skeleton as it currently stands: the solve frame
    // once the bind pose has been turned to face the targets, so every swing is measured against a rest that
    // already faces the right way. -xlinka
    private void CaptureRest()
    {
        _lArmRest = CaptureLimbRest(_lUpper, _lLower, _lHand);
        _rArmRest = CaptureLimbRest(_rUpper, _rLower, _rHand);

        CaptureRestChain(_spine);
        CaptureRestChain(_lShoulder, _lUpper, _lLower, _lHand);
        CaptureRestChain(_rShoulder, _rUpper, _rLower, _rHand);
        CaptureRestChain(_lHip, _lKnee, _lFoot, null!);
        CaptureRestChain(_rHip, _rKnee, _rFoot, null!);
        // Hands/feet are chain ends, so capture their rest rotation explicitly for the rotation blend.
        CaptureBoneRest(_lHand, null!);
        CaptureBoneRest(_rHand, null!);
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

    private void CaptureRestChain(List<Slot> chain)
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

    // Solve the full body for one frame. headTarget is required; the rest are optional.
    // Hands without a valid target use the procedural hang, not the captured rigging pose.
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

        // Bind pose first, measured fresh: the bones carry the current avatar fit scale, so lengths and
        // standing metrics are always sized to the rendered avatar this frame. -xlinka
        FixTransforms();
        MeasureBind();

        ComputeSolveOffset(in headTarget, in pelvisTarget, in leftFoot, in rightFoot);
        TurnSkeletonToSolveFrame();
        CaptureRest();

        // How far into a crouch the body is, 0..1, measured the only way that is avatar-size independent:
        // how far the head has dropped below where it stands, as a fraction of half the standing height.
        // The hips and spine already handle crouch; the ARMS did not, and that is most of why a crouched
        // avatar looked wrong - the arms kept hanging straight down through the thighs and kept swinging
        // at full walking amplitude while folded up. -xlinka
        _crouchLerp = 0f;
        if (GroundY.HasValue && _restHeadHeight > 1e-4f)
        {
            float standHeadY = GroundY.Value + _restHeadHeight;
            float drop = MathF.Max(0f, standHeadY - headTarget.Position.y);
            _crouchLerp = System.Math.Clamp(drop / (_restHeadHeight * 0.5f), 0f, 1f);
        }

        SolveSpine(in headTarget, in pelvisTarget, in leftFoot, in rightFoot);
        TurnChestTowardHands(in leftHand, in rightHand, in headTarget);
        ApplyBreathing(in headTarget);

        // The hand clearance cylinder must follow the solved torso, including translation, turning,
        // crouch and gait settle. MeasureBind's positions belong to the unsolved skeleton.
        _curBodyCentre = (_spine[0].GlobalPosition + _spine[_spine.Count - 1].GlobalPosition) * 0.5f;

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

        SolveLeg(_lHip, _lKnee, _lFoot, _lToe, _lToeRel, _lUpperLegLen, _lLowerLegLen, _bindLFootRot, in leftFoot);
        SolveLeg(_rHip, _rKnee, _rFoot, _rToe, _rToeRel, _rUpperLegLen, _rLowerLegLen, _bindRFootRot, in rightFoot);
    }

    // The solve frame from this frame's targets. Up is head over the pelvis when tracked, else head over the
    // feet average. Forward is the average of the two foot facings (plus the pelvis facing when tracked),
    // each sign-fixed against the head's facing so a foot pointing back never turns the body, rejected against
    // up. The head itself only settles signs: the body faces where the feet stand, and trails the look. -xlinka
    private void ComputeSolveOffset(in Target head, in Target pelvis, in Target lFoot, in Target rFoot)
    {
        float3 headPos = head.Position;
        float3 lFootRef = FootReference(in lFoot, _bindLFootPos - _bindHeadPos, headPos);
        float3 rFootRef = FootReference(in rFoot, _bindRFootPos - _bindHeadPos, headPos);
        float3 feetAvg = (lFootRef + rFootRef) * 0.5f;

        float3 up = pelvis.Valid ? headPos - pelvis.Position : headPos - feetAvg;
        if (up.LengthSquared < 1e-8f)
            up = float3.Up;
        up = up.Normalized;

        float3 headFwd = ForwardOf(head.Rotation * float3.Backward, head.Rotation * float3.Up, up);
        float3 lFwd = FootFacing(in lFoot, _bindLFootRot, up, headFwd);
        float3 rFwd = FootFacing(in rFoot, _bindRFootRot, up, headFwd);
        if (float3.Dot(lFwd, headFwd) < 0f) lFwd = -lFwd;
        if (float3.Dot(rFwd, headFwd) < 0f) rFwd = -rFwd;

        float3 sum = float3.Zero;
        if (pelvis.Valid)
            sum += ForwardOf(pelvis.Rotation * float3.Backward, pelvis.Rotation * float3.Up, up);
        sum += SlerpDirection(lFwd, rFwd, 0.5f);

        float3 forward = RejectUnit(sum, up);
        if (forward.LengthSquared < 1e-8f)
            forward = RejectUnit(headFwd, up);
        if (forward.LengthSquared < 1e-8f)
            forward = RejectUnit(_skeletonForward, up);
        if (forward.LengthSquared < 1e-8f)
            forward = RejectUnit(float3.Backward, up);
        if (forward.LengthSquared < 1e-8f)
            forward = float3.Cross(up, float3.Right).Normalized;

        SolveUp = up;
        SolveForward = forward;
        floatQ solveOrientation = BuildRotation(forward, up);
        floatQ skeletonOrientation = BuildRotation(_skeletonForward, _skeletonUp);
        SolveRotationOffset = (skeletonOrientation * solveOrientation.Inverse).Normalized;
        SolveEffectorCenter = pelvis.Valid ? pelvis.Position : (headPos + feetAvg) * 0.5f;
        SolveSkeletonCenter = _bindHipsPos;

        float3 flat = forward;
        flat.y = 0f;
        _curBodyForward = flat.LengthSquared > 1e-8f ? flat.Normalized : _curBodyForward;
        float3 right = float3.Cross(_curBodyForward, float3.Up);
        _curBodyRight = right.LengthSquared > 1e-8f ? right.Normalized : float3.Right;
    }

    // Where a foot counts for the solve frame: its target when something drives it, else the bind foot's offset
    // from the bind head hung under the live head, so an undriven pair still gives an upright frame.
    private static float3 FootReference(in Target foot, float3 bindOffsetFromHead, float3 headPos)
    {
        if (foot.Valid && foot.PositionWeight > 1e-3f)
            return foot.Position;
        return headPos + bindOffsetFromHead;
    }

    // A foot target's facing. A foot-frame rotation faces along its own forward; a bone rotation faces along
    // whatever bone-local direction was the skeleton forward at bind, and only counts while it carries rotation
    // weight. Anything else faces with the head.
    private float3 FootFacing(in Target foot, floatQ bindFootRot, float3 up, float3 headFwd)
    {
        if (!foot.Valid)
            return headFwd;
        if (foot.FootFrameRotation)
            return ForwardOf(foot.Rotation * float3.Backward, foot.Rotation * float3.Up, up);
        if (foot.RotationWeight <= 1e-3f)
            return headFwd;
        floatQ boneToBind = bindFootRot.Inverse;
        float3 f = foot.Rotation * (boneToBind * _skeletonForward);
        float3 u = foot.Rotation * (boneToBind * _skeletonUp);
        return ForwardOf(f, u, up);
    }

    // A frame's facing that stays meaningful when it pitches vertical: the forward axis blended toward the
    // frame's up (or its negative, whichever leans forward) by how far forward already points along up.
    private static float3 ForwardOf(float3 forward, float3 frameUp, float3 up)
    {
        if (forward.LengthSquared < 1e-8f)
            return frameUp;
        forward = forward.Normalized;
        if (frameUp.LengthSquared < 1e-8f)
            return forward;
        frameUp = frameUp.Normalized;
        float vertical = float3.Dot(forward, up);
        float3 candidate = vertical > 0f ? -frameUp : frameUp;
        return SlerpDirection(forward, candidate, MathF.Abs(vertical));
    }

    private static float3 SlerpDirection(float3 a, float3 b, float t)
    {
        if (a.LengthSquared < 1e-8f) return b;
        if (b.LengthSquared < 1e-8f) return a;
        a = a.Normalized;
        b = b.Normalized;
        float dot = System.Math.Clamp(float3.Dot(a, b), -1f, 1f);
        if (dot > 0.9995f)
            return float3.Lerp(a, b, t).Normalized;
        if (dot < -0.9995f)
            return t < 0.5f ? a : b;
        float angle = MathF.Acos(dot);
        float3 axis = float3.Cross(a, b).Normalized;
        return floatQ.AxisAngleRad(axis, angle * System.Math.Clamp(t, 0f, 1f)) * a;
    }

    private static float3 RejectUnit(float3 v, float3 axis)
    {
        float3 r = v - axis * float3.Dot(v, axis);
        return r.LengthSquared > 1e-8f ? r.Normalized : float3.Zero;
    }

    // Turn the bind skeleton into the solve frame: the hips take the inverse solve offset and sit at the effector
    // centre, every other bone follows on its bind offset. Solving in world from here is the same as solving in
    // the bind frame and turning the results back, because every step below is relative. -xlinka
    private void TurnSkeletonToSolveFrame()
    {
        floatQ turn = SolveRotationOffset.Inverse;
        var hips = _spine[0];
        WriteGlobalRotation(hips, (turn * _bindHipsRot).Normalized);
        WriteGlobalPosition(hips, SolveEffectorCenter + turn * (_bindHipsPos - SolveSkeletonCenter));
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

    // Hips from the feet: each upper leg moves a tenth of the way its foot still has to travel to its target,
    // the hips translate by the average of those moves and turn by the rotation between the bind hip line and
    // the moved one. Weight-0 feet contribute nothing; a rig without legs leaves the hips where the solve frame
    // put them. -xlinka
    private void HipsFromFeet(in Target lFoot, in Target rFoot, out float3 hipsPos, out floatQ hipTilt)
    {
        var hips = _spine[0];
        hipsPos = hips.GlobalPosition;
        hipTilt = floatQ.Identity;
        if (_lHip == null || _rHip == null || _lHip.IsDestroyed || _rHip.IsDestroyed
            || _lFoot == null || _rFoot == null || _lFoot.IsDestroyed || _rFoot.IsDestroyed)
            return;

        float3 lUpper = _lHip.GlobalPosition;
        float3 rUpper = _rHip.GlobalPosition;
        float3 lApprox = lUpper + FootTravel(in lFoot, _lFoot.GlobalPosition) * HipFollowFootFraction;
        float3 rApprox = rUpper + FootTravel(in rFoot, _rFoot.GlobalPosition) * HipFollowFootFraction;

        hipsPos += (lApprox + rApprox) * 0.5f - (lUpper + rUpper) * 0.5f;
        float3 from = rUpper - lUpper;
        float3 to = rApprox - lApprox;
        if (from.LengthSquared > 1e-8f && to.LengthSquared > 1e-8f)
            hipTilt = FabrikSolver.FromToRotation(from, to);
    }

    private static float3 FootTravel(in Target foot, float3 footBone)
    {
        if (!foot.Valid)
            return float3.Zero;
        float w = System.Math.Clamp(foot.PositionWeight, 0f, 1f);
        return (foot.Position - footBone) * w;
    }

    private void SolveSpine(in Target headTarget, in Target pelvisTarget, in Target leftFoot, in Target rightFoot)
    {
        int n = _spine.Count;
        float3 headPos = headTarget.Position;

        // Crouch floor for the BODY, derived so the SPINE NEVER COMPRESSES past its slack: crouch depth belongs to
        // the KNEES (the hips follow the head down toward the heels), and the head bottoms out a near-full spine
        // above the lowest the hips may go. -xlinka
        if (!pelvisTarget.Valid && GroundY.HasValue && _restHeadHeight > 1e-4f && _restStandHeight > 1e-4f)
        {
            float spineVertical = MathF.Max(_restHeadHeight - _restStandHeight, _restHeadHeight * 0.25f);
            float minHeadY = GroundY.Value + _restStandHeight * CrouchHipsMinFraction + spineVertical * CrouchSpineSlack;
            headPos.y = MathF.Max(headPos.y, minHeadY);
        }

        float3 hipsPos;
        float3 hipsForward = SolveForward;
        if (pelvisTarget.Valid)
        {
            hipsPos = pelvisTarget.Position;
        }
        else
        {
            HipsFromFeet(in leftFoot, in rightFoot, out hipsPos, out floatQ hipTilt);
            hipsForward = hipTilt * SolveForward;

            // The head wins the spine: hips further than the chain reaches are pulled toward the head, and a head
            // close enough to fold the spine past its slack pushes the hips down instead, so the crouch goes to
            // the knees. -xlinka
            float3 toHead = headPos - hipsPos;
            float dist = toHead.Length;
            if (dist > _spineTotalLength && dist > 1e-6f)
                hipsPos = headPos - toHead * (_spineTotalLength / dist);
            else if (dist < _spineTotalLength * CrouchSpineSlack)
                hipsPos = PushAlong(hipsPos, headPos, -SolveUp, _spineTotalLength * CrouchSpineSlack);

            hipsPos += LocomotionOffset;

            // Stand the body at its NATURAL standing height above the floor when the feet aren't tracked, so
            // untracked legs reach the ground (anti-float) and the hips can't crouch through it (anti-sink). The MIN
            // lets the hips FOLLOW the head down to near the heels; the head floor above is spine-length-consistent
            // with it. -xlinka
            if (GroundY.HasValue)
            {
                float legLen = System.Math.Max(_lUpperLegLen + _lLowerLegLen, _rUpperLegLen + _rLowerLegLen);
                float stand = _restStandHeight > 1e-4f ? _restStandHeight : legLen * 0.9f;
                if (stand > 1e-4f)
                {
                    float maxHipsY = GroundY.Value + stand;
                    float minHipsY = GroundY.Value + stand * CrouchHipsMinFraction;
                    hipsPos.y = System.Math.Clamp(hipsPos.y, minHipsY, maxHipsY);

                    // Crouch back-shift: as the head dips below standing, lean the hips back along the body forward
                    // so the weight sits over the feet instead of the knees shooting forward through the floor.
                    if (MoveBodyBackWhenCrouching > 0f)
                    {
                        float drop = maxHipsY - hipsPos.y;
                        if (drop > 1e-4f)
                            hipsPos -= _curBodyForward * (drop * MoveBodyBackWhenCrouching);
                    }
                }
            }
        }

        WriteGlobalPosition(_spine[0], hipsPos);

        for (int i = 0; i < n; i++)
            _spineJoints[i] = _spine[i].GlobalPosition;
        _spineJoints[0] = hipsPos;

        // Cone-limited per vertebra. Unconstrained FABRIK will meet a head target that is close to the
        // hips by folding the spine into a hairpin, because segment length is the only rule it knows -
        // which is what a crouch asks for and where it looked worst. The stiffness pull below is a
        // SOFT bias toward straight and cannot stop a fold on its own; this is the hard limit that a
        // spine actually has. -xlinka
        FabrikSolver.SolveChain(_spineJoints, _spineLengths, headPos, _spineLimits, SolveForward,
            iterations: 12, tolerance: 0.001f);

        // Stiffness: pull interior joints toward the straight hips->head line so the spine resists
        // over-bending. Stiffer low (lumbar stable), looser high (thoracic mobile).
        if (SpineStiffness > 0f && n > 2 && _spineTotalLength > 1e-5f)
        {
            float cum = 0f;
            for (int i = 1; i < n - 1; i++)
            {
                cum += _spineLengths[i - 1];
                float frac = cum / _spineTotalLength;
                float3 straight = float3.Lerp(hipsPos, headPos, frac);
                float w = System.Math.Clamp(SpineStiffness * (1f - StiffnessTaper * frac), 0f, 1f);
                _spineJoints[i] = float3.Lerp(_spineJoints[i], straight, w);
            }
        }

        // Both ends pinned: hips exact AND head exact, with the interior joints reconciled to their
        // segment lengths. The old hard head pin after a one-ended solve stretched the neck segment. -xlinka
        FabrikSolver.ReconcilePinnedEnds(_spineJoints, _spineLengths, hipsPos, headPos);

        // Write spine bone positions + swing rotations (hips..neck; the head is handled separately below). The
        // hips twist onto the hip line the feet turned; the rest keep the solve forward.
        for (int i = 0; i < n - 1; i++)
        {
            var bone = _spine[i];
            MaybeWritePosition(bone, _spineJoints[i]);
            ApplySpineSwing(bone, _spineJoints[i + 1] - _spineJoints[i], i == 0 ? hipsForward : SolveForward);
        }

        // Apply view yaw, pitch, and roll over the head bone's authored offset from the body facing.
        var head = _spine[n - 1];
        MaybeWritePosition(head, _spineJoints[n - 1]);

        // HEAD BOB, on the bone and never on the view.
        //
        // A walking body's head rises and falls twice a stride and rolls a little toward the planted
        // foot. Ours did neither: the skull stayed perfectly level and the whole walk read as a glide,
        // which is one of the two or three things that give a procedural body away instantly.
        //
        // It is applied to the head BONE only. Moving the view with it would be motion sickness in a
        // headset and a tax on everyone else's stomach on desktop, and the one person who cannot see
        // their own head is the person wearing it. So: everybody else sees the bob, the wearer's
        // camera stays where their neck actually is. -xlinka
        floatQ headRotation = headTarget.Rotation;
        if (HeadBobWeight > 1e-3f)
        {
            float bob = HeadBobWeight;
            // Twice a stride vertically, once a stride sideways: a body drops on each footfall but
            // leans toward one foot then the other.
            float vertical = MathF.Sin(ArmSwingPhase * 2f) * HeadBobVertical * bob;
            float lateral = MathF.Sin(ArmSwingPhase) * HeadBobHorizontal * bob;
            float roll = MathF.Sin(ArmSwingPhase) * HeadBobRoll * bob;

            MaybeWritePosition(head, _spineJoints[n - 1] + float3.Up * vertical + _curBodyRight * lateral);

            if (MathF.Abs(roll) > 1e-5f)
            {
                float3 axis = headRotation * float3.Backward;
                if (axis.LengthSquared > 1e-8f)
                    headRotation = floatQ.AxisAngleRad(axis.Normalized, roll) * headRotation;
            }
        }

        WriteHeadFacing(head, headRotation);

        // With a real pelvis tracker, the hips bone takes its rotation too.
        if (pelvisTarget.Valid)
            WriteGlobalRotation(_spine[0], pelvisTarget.Rotation);
    }

    // Move point along dir (unit) by the smallest non-negative amount that puts it exactly `distance` from anchor.
    // Leaves it alone when no such amount exists.
    private static float3 PushAlong(float3 point, float3 anchor, float3 dir, float distance)
    {
        if (dir.LengthSquared < 1e-8f)
            return point;
        dir = dir.Normalized;
        float3 v = anchor - point;
        float b = float3.Dot(v, dir);
        float c = v.LengthSquared - distance * distance;
        float disc = b * b - c;
        if (disc < 0f)
            return point;
        float t = b + MathF.Sqrt(disc);
        if (t <= 0f)
            return point;
        return point + dir * t;
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
    // How extended the authored arm has to be before it counts as a relaxed hang rather than a pose. A
    // real hanging arm keeps a slight elbow bend and sits well above this; a held or folded one is far
    // below it.
    private const float AuthoredHangMinExtension = 0.7f;
    // How far down the authored arm has to point, as the cosine of its angle from the skeleton's own
    // down axis: 35 degrees. A hanging arm sits well inside that. The old test accepted 70 degrees off
    // vertical, which is a wide A-pose a few degrees short of a T, and on a real package ('lexiesnep',
    // both arms 22-24 degrees below horizontal at 94-97% reach) that made the arms-out rigging pose the
    // desktop idle. -xlinka
    private const float AuthoredHangMinDrop = 0.82f;

    // Do BOTH authored arms read as a relaxed hang? Decided for the pair, never per arm, and decided
    // from the BIND pose against the skeleton's own up axis in MeasureBind, never from the per-frame
    // rest capture.
    //
    // Two tests have to pass: the arm points down, and it is actually EXTENDED. Downward alone accepts a
    // frozen gesture, because an avatar saved holding something still has a downward shoulder-to-hand
    // vector while its hand sits tucked near its own shoulder.
    //
    // Asking per ARM is what produced the first visible fault. On a real avatar the two came out at 94%
    // and 40% of their own reach - one a genuine hang, the other a held pose - so one arm kept the
    // author's idle and the other fell back to the computed one, and the body wore two different idles
    // at once. The authored pose is only usable as an idle if the WHOLE authored pose is one, so both
    // arms decide it together and both take the same path.
    //
    // Asking every FRAME, in the solve frame, produced the second. CaptureRest runs after the skeleton
    // has been turned to face the targets, so the rest direction it measured carried whatever tilt the
    // solve frame had that frame, and the solve frame tilts while walking (its up runs from the head to
    // the average of two feet that are stepping). A rig sitting near the threshold flipped between the
    // two idles with the gait: arms out standing still, hanging while walking, out again on the next
    // stop. The bind pose does not move and the skeleton's up axis is measured from the same bones, so
    // the answer is the same every frame no matter how the body leans. -xlinka
    private bool _authoredArmsHang;

    private bool ArmBindIsHang(Slot? upper, Slot? hand, float reach)
    {
        if (upper == null || hand == null || upper.IsDestroyed || hand.IsDestroyed || reach <= 1e-4f)
            return false;
        float3 toHand = hand.GlobalPosition - upper.GlobalPosition;
        float length = toHand.Length;
        if (length < 1e-4f || length < AuthoredHangMinExtension * reach)
            return false;
        return float3.Dot(toHand / length, _skeletonUp) < -AuthoredHangMinDrop;
    }

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
        float upperLen, float lowerLen, float shoulderLen, in Target handTarget, float side)
    {
        if (upper == null || lower == null || hand == null
            || upper.IsDestroyed || lower.IsDestroyed || hand.IsDestroyed)
            return;

        // A missing/disconnected hand source still needs a solved arm. FixTransforms has just
        // restored the bind pose; skipping here would display that pose until a target returns.
        Target target = handTarget.Valid ? handTarget : new Target { Rotation = floatQ.Identity };
        float3 root = upper.GlobalPosition;

        // Position weight: blend the goal between a NATURAL hang (down the body frame, not the model's authored bind
        // pose) and the full IK target, so an untracked arm rests by the side instead of frozen wherever the rig was
        // authored. Weight 0 (desktop, no controller) = clean hang; weight 1 = full IK on the tracked hand. -xlinka
        float pw = System.Math.Clamp(target.PositionWeight, 0f, 1f);
        // Untracked rest: use the avatar's AUTHORED arm pose (already in the solve frame) when the rig authors the
        // arms hanging down (a genuine relaxed hang, see ArmBindIsHang) - the two-bone solver then reconstructs the
        // authored elbow bend from the shorter-than-reach target + rest pole, which reads as the model's own idle.
        // A rig authored in a T or a wide A gets the computed by-the-side hang instead: that pose is what the
        // rigger needed, not an idle, and the wearer's arms are driven, so nothing should ever show it. -xlinka
        LimbRestPose restPose = rootNode == BodyNode.LeftUpperArm ? _lArmRest : _rArmRest;
        float3 restEnd = _authoredArmsHang && restPose.Valid
            ? root + restPose.RootToEnd
            : NaturalArmHang(root, upperLen + lowerLen, side);

        // Idle life for untracked arms: the walking counter-swing (opposite phase per side, along the
        // body forward) plus a slow standing drift, so hanging hands are never statues. Amplitudes scale
        // with the arm's reach (avatar-size independent) and fade out as the hand approaches tracked.
        // Deltas on the REST goal only - nothing integrates, tracked hands are untouched. -xlinka
        float armReach = upperLen + lowerLen;
        if (pw < 0.999f && armReach > 1e-4f)
        {
            float life = 1f - pw;

            // Crouch fold, before the idle motions so they ride the folded pose rather than the hanging one.
            if (_crouchLerp > 1e-3f)
            {
                float c = _crouchLerp * life;
                restEnd += _curBodyForward * (CrouchArmForward * armReach * c)
                         + float3.Up * (CrouchArmUp * armReach * c)
                         - _curBodyRight * (CrouchArmIn * armReach * c * side);
            }

            // THE ARM AS A WEIGHT ON A SHOULDER rather than a sine along forward. See HandMotion for
            // why. The simulation runs in arm-reach units so one set of numbers fits every avatar,
            // and the result is rotated into the body frame here.
            if (HandMotionWeight > 1e-3f)
            {
                bool isLeft = rootNode == BodyNode.LeftUpperArm;
                ref var motion = ref (isLeft ? ref _leftHand : ref _rightHand);

                // A crouched walk barely swings the arms; they are up and braced, not hanging free.
                float swingDamp = 1f - (1f - CrouchSwingDamp) * _crouchLerp;
                float drive = ArmSwingWeight * swingDamp;

                var parameters = HandMotionParameters.Lerp(
                    HandMotionParameters.Lerp(HandMotionParameters.Walk, HandMotionParameters.Run,
                        System.Math.Clamp(RunBlend, 0f, 1f)),
                    HandMotionParameters.Air,
                    System.Math.Clamp(AirBlend, 0f, 1f));

                // Where the spring pulls. On the ground that is the hang itself, so zero; in the air
                // the hands drift up and out, which is what a body does when its feet leave the floor.
                float3 restTarget = float3.Zero;
                if (AirBlend > 1e-3f)
                    restTarget += new float3(0.18f * side, 0.22f, -0.05f) * AirBlend;

                // The standing sway, as a force target rather than a separate additive motion, so it
                // shares the same inertia as everything else and cannot fight it.
                if (IdleWeight > 1e-3f && SwayWeight > 1e-3f)
                {
                    float t = TimeSeconds;
                    float phase = side > 0f ? 0.7f : 0f;
                    restTarget += new float3(
                        MathF.Sin(t * 0.37f + phase) * 0.020f,
                        MathF.Sin(t * 0.53f + phase) * 0.014f,
                        MathF.Sin(t * 0.31f + phase) * 0.018f) * (IdleWeight * SwayWeight);
                }

                // Reach units, so the body's own speed means the same thing on any size of avatar.
                float3 bodyVelocity = armReach > 1e-4f ? BodyVelocity / armReach : float3.Zero;

                float phaseOffset = side > 0f ? MathF.PI : 0f;
                motion.Step(in parameters, ArmSwingPhase + phaseOffset, drive,
                    in bodyVelocity, in restTarget, DeltaTime);

                float3 offset = motion.Offset * (armReach * HandMotionWeight * life);
                restEnd += _curBodyForward * offset.z
                         + _curBodyRight * (offset.x * side)
                         + float3.Up * offset.y;

                // KEEP THE HAND OUT OF THE BODY. A swing that crosses the torso is the single most
                // obvious artefact in a procedural walk, and it happens on any avatar whose arms hang
                // closer in than the simulation's own travel. Pushed out along the body's right axis
                // rather than solved against real colliders: a torso is a cylinder to within the
                // accuracy anybody can see, and this costs a dot product. -xlinka
                float3 fromSpine = restEnd - _curBodyCentre;
                float lateral = float3.Dot(fromSpine, _curBodyRight) * side;
                float minimum = _torsoHalfWidth;
                if (minimum > 1e-4f && lateral < minimum)
                    restEnd += _curBodyRight * ((minimum - lateral) * side);
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

        // Elbow pole as a swivel angle about shoulder->goal: authored rest bend for small swings, hand position in
        // the shoulder frame for big ones, an elbow tracker over both, then the wrist roll past its dead zone.
        float3 pole = ComputeArmPole(root, goalPos, in restPose, upperLen + lowerLen, side, hand, pw, in target,
            out var poleDebug);
        if (rootNode == BodyNode.LeftUpperArm)
            LeftArmPole = poleDebug;
        else
            RightArmPole = poleDebug;

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

        // Relax the parent first, then set the wrist's final world rotation. Doing this in reverse
        // adds the forearm's roll to the already-solved wrist and moves it off the tracked target.
        ApplyTwistRelax(lower, hand, end - mid, handRot);
        WriteGlobalRotation(hand, handRot);
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

    // Swivel frame for an arm: rotate the body frame so its forward lands on the shoulder->goal axis, and take
    // where body-down and body-outward went as the circle's base (u) and positive (v) directions. One rotation
    // for every reach direction, so the frame turns continuously as the hand moves: a hand in front gets
    // u = down; a hanging hand gets u = back (the natural hang); an overhead hand gets u = forward with the
    // height term swinging the elbow round to out/back. v is rebuilt from the cross product so it is exactly
    // perpendicular and always means OUTWARD for this arm. The one seam is a hand directly BEHIND the
    // shoulder at shoulder height (axis antiparallel to forward), where the fallback picks the yaw flip;
    // that pose is rare and the rest bend has long since faded out there. -xlinka
    private void ArmSwivelFrame(float3 axis, float side, out float3 u, out float3 v)
    {
        floatQ toAxis = FabrikSolver.FromToRotation(_curBodyForward, axis, float3.Up);
        u = toAxis * float3.Down;
        u -= axis * float3.Dot(u, axis);
        if (u.LengthSquared < 1e-8f)
        {
            u = _curBodyForward * -1f;
            u -= axis * float3.Dot(u, axis);
            if (u.LengthSquared < 1e-8f)
                u = float3.Cross(axis, _curBodyRight);
        }
        u = u.Normalized;
        // side * cross(u, axis) is where the body's outward axis lands under the same rotation.
        v = float3.Cross(u, axis) * side;
        v = v.LengthSquared > 1e-8f ? v.Normalized : float3.Cross(axis, u);
    }

    // Hand-position swivel: three clamped linear terms on the hand's height / forward reach / outward offset in the
    // shoulder frame (each divided by arm reach), a base, the per-avatar offset, then the hard clamp. -xlinka
    private float HandSwivel(float3 limb, float reach, float side)
    {
        float inv = reach > 1e-4f ? 1f / reach : 0f;
        float up = float3.Dot(limb, float3.Up) * inv;
        float forward = float3.Dot(limb, _curBodyForward) * inv;
        float outward = float3.Dot(limb, _curBodyRight) * side * inv;

        float swivel = SwivelBase + SwivelOffset
            + System.Math.Clamp(SwivelHeightGain * up, SwivelHeightMin, SwivelHeightMax)
            + System.Math.Clamp(SwivelReachGain * (SwivelReachStart - forward), 0f, SwivelReachMax)
            + System.Math.Clamp(SwivelOutwardGain * (SwivelOutwardStart - outward), SwivelOutwardMin, SwivelOutwardMax);
        return System.Math.Clamp(swivel, SwivelMin, SwivelMax);
    }

    // Elbow pole for one arm. Everything is a DIRECTION on the circle around the shoulder->goal axis, blended as
    // ANGLES about that axis (shortest arc), never as positions: a position lerp toward a bend goal weighted the
    // vote by how far the goal sat from the shoulder, and a goal at weight 1 still did not land on the goal.
    //   1. hand-position swivel (always defined, clamped),
    //   2. the authored rest bend transported onto the axis, voting by how small the swing from rest is,
    //   3. a bend goal (elbow tracker) voting BendGoalWeight, unclamped: a tracker is authoritative,
    //   4. wrist roll past its dead zone, only when there is no goal.
    // Pole distance is cosmetic: SolveTwoBone only reads the bend plane. -xlinka
    private float3 ComputeArmPole(float3 root, float3 goalPos, in LimbRestPose restPose, float reach, float side,
        Slot? hand, float positionWeight, in Target target, out PoleDebug debug)
    {
        debug = default;
        float poleDist = MathF.Max(reach * 0.45f, 0.05f);
        float3 limb = goalPos - root;
        if (limb.LengthSquared < 1e-8f)
        {
            // Goal on the shoulder: no axis to swivel about. Elbow down and back, the hang.
            float3 hangDir = float3.Down * 0.8f - _curBodyForward * 0.5f + _curBodyRight * (side * 0.3f);
            return root + hangDir.Normalized * poleDist;
        }
        float3 axis = limb.Normalized;
        ArmSwivelFrame(axis, side, out float3 u, out float3 v);

        float handSwivel = HandSwivel(limb, reach, side);
        float3 dir = u * MathF.Cos(handSwivel) + v * MathF.Sin(handSwivel);
        debug.HandSwivelRad = handSwivel;
        debug.Source = PoleSource.Hand;

        // Rest bend: the captured elbow offset carried by the minimal rotation from rest root->end onto the live
        // axis. Trustworthy while the swing from rest is small (that is the walking counter-swing and the idle
        // drift), meaningless for a big reach, so its vote fades across [RestPoleSwingKeep, RestPoleSwingDrop].
        if (TryRestBendDirection(axis, in restPose, out float3 restDir, out float swing))
        {
            float restW = 1f - SmoothStep(RestPoleSwingKeep, RestPoleSwingDrop, swing);
            if (restW > 1e-4f)
            {
                dir = SwivelToward(axis, dir, restDir, restW);
                debug.RestWeight = restW;
                if (restW >= 0.5f)
                    debug.Source = PoleSource.Rest;
            }
        }

        // Bend goal in the same angle space. Direction only; a goal sitting on the axis says nothing.
        if (target.HasBendGoal && TryPerpendicular(target.BendGoal - root, axis, out float3 goalDir))
        {
            float goalW = System.Math.Clamp(BendGoalWeight, 0f, 1f);
            if (goalW > 1e-4f)
            {
                dir = SwivelToward(axis, dir, goalDir, goalW);
                if (goalW >= 0.5f)
                    debug.Source = PoleSource.Goal;
            }
        }

        // Wrist-led elbow: rolling a tracked wrist swings the pole around the arm axis (palm-up flips the elbow
        // out, palm-down tucks it in), the way a real forearm follows the hand. The roll is the hand target's
        // twist about the axis relative to the rest wrist; only the part past the dead zone counts, so a wrist
        // that is merely not neutral leaves the swivel alone. Elbow trackers take precedence. -xlinka
        if (WristBendInfluence > 1e-4f && positionWeight > 1e-4f && !target.HasBendGoal
            && hand != null && _restRot.TryGetValue(hand, out var wristRest))
        {
            floatQ delta = target.Rotation * wristRest.Inverse;
            floatQ twist = TwistAround(delta, axis);
            float roll = WrapPi(2f * MathF.Atan2(
                twist.x * axis.x + twist.y * axis.y + twist.z * axis.z, twist.w));
            float deadZone = MathF.Max(WristBendDeadZone, 0f);
            float past = MathF.Abs(roll) - deadZone;
            if (past > 0f)
            {
                float swingAngle = System.Math.Clamp(
                    MathF.CopySign(past, roll) * WristBendInfluence * positionWeight
                        * System.Math.Clamp(target.RotationWeight, 0f, 1f),
                    -MaxWristBendSwing, MaxWristBendSwing);
                if (MathF.Abs(swingAngle) > 1e-3f)
                    dir = floatQ.AxisAngleRad(axis, swingAngle) * dir;
            }
        }

        // Reported in the (u, v) frame, not by the right-hand rule about the axis: that rule's sign flips with
        // side, this one reads positive = outward on both arms, the same as HandSwivel's own output.
        debug.SwivelRad = MathF.Atan2(float3.Dot(dir, v), float3.Dot(dir, u));
        return root + dir * poleDist;
    }

    // The authored rest elbow offset carried onto the live limb axis by the minimal rotation from the rest
    // root->end, as a unit direction perpendicular to the axis, plus the swing angle it took to get there. False
    // when the rig has no usable rest bend (A/T-pose with a dead-straight arm) or the transported offset
    // collapses onto the axis. -xlinka
    private static bool TryRestBendDirection(float3 axis, in LimbRestPose restPose, out float3 bendDir, out float swing)
    {
        bendDir = float3.Zero;
        swing = 0f;
        if (!restPose.Valid || restPose.RootToEnd.LengthSquared < 1e-8f)
            return false;

        float3 restAxis = restPose.RootToEnd.Normalized;
        swing = MathF.Acos(System.Math.Clamp(float3.Dot(restAxis, axis), -1f, 1f));

        floatQ carry = FabrikSolver.FromToRotation(restPose.RootToEnd, axis, restPose.PlaneNormal);
        float3 bend = carry * restPose.RootToMid;
        bend -= axis * float3.Dot(bend, axis);
        float minBend = MathF.Max(restPose.RootToEnd.Length * 0.015f, 0.003f);
        if (bend.LengthSquared <= minBend * minBend)
            return false;

        bendDir = bend.Normalized;
        return true;
    }

    // Unit component of v perpendicular to axis; false when v lies along the axis.
    private static bool TryPerpendicular(float3 v, float3 axis, out float3 dir)
    {
        dir = v - axis * float3.Dot(v, axis);
        if (dir.LengthSquared < 1e-8f)
            return false;
        dir = dir.Normalized;
        return true;
    }

    // Signed angle (radians) from one perpendicular unit direction to another, measured about axis.
    private static float SignedAngleAbout(float3 axis, float3 from, float3 to)
        => MathF.Atan2(float3.Dot(float3.Cross(from, to), axis), float3.Dot(from, to));

    // Swing a perpendicular unit direction toward another about the axis by fraction t of the SHORTER arc.
    private static float3 SwivelToward(float3 axis, float3 from, float3 to, float t)
    {
        if (t <= 0f)
            return from;
        if (t >= 1f)
            return to;
        float angle = SignedAngleAbout(axis, from, to);
        if (MathF.Abs(angle) < 1e-5f)
            return from;
        return floatQ.AxisAngleRad(axis, angle * t) * from;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        if (edge1 - edge0 < 1e-6f)
            return x >= edge1 ? 1f : 0f;
        float t = System.Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // Analytic leg: the knee sits in the plane of hip->foot and the solve forward, on the forward side, by the
    // law of cosines. A knee tracker replaces the forward with its own point. The foot bone then takes the target
    // rotation in full, pitch included, so a heel strike or toe-off the gait produces reaches the mesh. -xlinka
    private void SolveLeg(Slot? hip, Slot? knee, Slot? foot, Slot? toe, floatQ toeRel,
        float upperLen, float lowerLen, floatQ bindFootRot, in Target target)
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

        float restUpper = upperLen;
        float restLower = lowerLen;
        Stretch(root, goalPos, ref upperLen, ref lowerLen);
        // The visible offsets must match the solve lengths. FixTransforms restores them each frame. -xlinka
        if (!WriteBonePositions)
        {
            ScaleLimbSegment(hip, knee, upperLen / MathF.Max(restUpper, 1e-6f));
            ScaleLimbSegment(knee, foot, lowerLen / MathF.Max(restLower, 1e-6f));
        }

        float3 bend = target.HasBendGoal ? target.BendGoal : SolveForward;
        FabrikSolver.SolveTwoBoneBend(root, goalPos, upperLen, lowerLen, bend, target.HasBendGoal,
            SolveUp, out var mid, out var end);

        MaybeWritePosition(hip, root);
        ApplySwing(hip, mid - root);
        MaybeWritePosition(knee, mid);
        ApplySwing(knee, end - mid);
        MaybeWritePosition(foot, end);

        floatQ boneRot = target.FootFrameRotation
            ? target.Rotation * _skeletonFrameFlatInv * bindFootRot
            : target.Rotation;
        float rw = System.Math.Clamp(target.RotationWeight, 0f, 1f);
        floatQ restFootRot = _restRot.TryGetValue(foot, out var rfr) ? rfr : boneRot;
        WriteGlobalRotation(foot, floatQ.Slerp(restFootRot, boneRot, rw).Normalized);

        SolveToe(foot, toe, toeRel, target.StepLift);
    }

    private static void ScaleLimbSegment(Slot root, Slot end, float scale)
    {
        if (MathF.Abs(scale - 1f) < 1e-6f)
            return;
        Slot? ancestor = end;
        while (ancestor != null && ancestor != root)
            ancestor = ancestor.Parent;
        if (ancestor != root)
            return;
        for (Slot? bone = end; bone != null && bone != root; bone = bone.Parent)
            bone.LocalPosition.SetValueSilently(bone.LocalPosition.Value * scale, change: true);
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
        float maximum = MathF.Max(MaxStretch, 1f);
        float t = System.Math.Clamp((ratio - kStart) / (maximum - kStart), 0f, 1f);
        float ease = t * t * (3f - 2f * t);
        float scale = 1f + (maximum - 1f) * ease;
        upperLen *= scale;
        lowerLen *= scale;
    }

    // Roll a forearm/lower-arm partway toward the hand's twist (rotation around the bone axis), so
    // the wrist roll is shared instead of snapping entirely at the wrist.
    private void ApplyTwistRelax(Slot forearm, Slot hand, float3 forearmDir, in floatQ handRot)
    {
        if (forearm == null || TwistRelax <= 0f || forearmDir.LengthSquared < 1e-8f)
            return;

        floatQ forearmRot = forearm.GlobalRotation;
        // Compare the same anatomical frame. A model may author its forearm and wrist with
        // completely different local axes; their bind offset is not physical wrist twist.
        if (!_restRot.TryGetValue(forearm, out var forearmRest) || !_restRot.TryGetValue(hand, out var handRest))
            return;
        floatQ wristInForearm = forearmRest.Inverse * handRest;
        floatQ desiredForearm = handRot * wristInForearm.Inverse;
        floatQ delta = desiredForearm * forearmRot.Inverse;
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
    // that axis so the bone keeps its AUTHORED offset from the body forward - the solve forward (which the
    // rest already faces) is transported through the swing and rolled onto the requested forward. Twisting
    // the bone's rendered -Z onto the body absolutely assumed belly-on--Z and wrung arbitrary-convention
    // spines sideways; this keeps the authored torso intact and turns it exactly with the body. -xlinka
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
            float3 transported = swing * SolveForward;
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
