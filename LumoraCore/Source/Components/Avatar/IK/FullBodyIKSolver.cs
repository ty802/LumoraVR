// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

// Engine-side full-body IK driver. Owns a BipedRig, captures rest bone
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
        // tilted onto GroundNormal and turned with the body. Tracked feet leave this false.
        public bool GroundAlign;
        public float3 GroundNormal;

        // 0..1 step swing height (procedural feet), used to curl the toe during the step.
        public float StepLift;
    }

    // Rest world pose of a 3-joint limb (root -> mid -> end), captured once. Drives the dynamic
    // bend pole: the rest elbow/knee offset is swung to follow the current root->target direction.
    private struct LimbRest
    {
        public bool Valid;
        public float3 Root;
        public float3 Mid;
        public float3 End;
    }

    // --- Tunables (driven from the component's sync values; defaults are sensible fallbacks) ---
    public float ShoulderWeight = 0.45f;   // how far the clavicle swings toward the hand
    public float MaxStretch = 1.08f;       // max limb over-extension past rest length
    public float PelvisDamp = 0.65f;       // hips smoothing toward the anchor (1 = snap)
    public float SpineStiffness = 0.2f;    // base pull toward the straight hips->head line
    public float BendGoalWeight = 0.5f;    // how strongly a bend goal steers the pole
    public float TwistRelax = 0.5f;        // forearm roll distributed toward the hand
    public float3 LocomotionOffset;        // added to the hips anchor (body bob / weight shift)

    private const float ToeCurlAngle = 0.5f;     // radians the toe curls at peak step lift
    private const float StiffnessTaper = 0.6f;   // upper spine is (1 - this) as stiff as the base

    private BipedRig _rig = null!;
    private bool _captured;

    // Hips smoothing state (pelvis follow / anti-snap).
    private float3 _prevHipsPos;
    private bool _hasPrevHips;

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

    // Rest bone lengths.
    private float _lUpperArmLen, _lLowerArmLen, _lShoulderLen;
    private float _rUpperArmLen, _rLowerArmLen, _rShoulderLen;
    private float _lUpperLegLen, _lLowerLegLen;
    private float _rUpperLegLen, _rLowerLegLen;
    private float _spineTotalLength;

    // Rest limb poses for dynamic bend goals + rest toe orientation relative to the foot.
    private LimbRest _lArmRest, _rArmRest, _lLegRest, _rLegRest;
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

    private static void WriteGlobalRotation(Slot bone, in floatQ rotation)
    {
        var parent = bone.Parent;
        var local = parent != null ? parent.GlobalRotationToLocal(rotation) : rotation;
        float dot = floatQ.Dot(local, bone.LocalRotation.Value);
        if (1f - (dot < 0 ? -dot : dot) > 1e-9f)
            bone.LocalRotation.SetValueSilently(local, change: true);
    }

    public void Initialize(BipedRig rig)
    {
        _rig = rig;
        _captured = false;
        Capture();
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
        _spineTotalLength = 0f;
        for (int i = 0; i < _spine.Count - 1; i++)
        {
            _spineLengths[i] = float3.Distance(_spine[i].GlobalPosition, _spine[i + 1].GlobalPosition);
            _spineTotalLength += _spineLengths[i];
        }
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

        // Rest facing (flattened) - the baseline the body-turn is measured from.
        float3 restFwd = _spine[0].GlobalRotation * float3.Backward;
        restFwd.y = 0f;
        _restBodyForward = restFwd.LengthSquared > 1e-6f ? restFwd.Normalized : float3.Backward;

        _captured = true;
    }

    private void AddSpineBone(BodyNode node)
    {
        var bone = _rig.TryGetBone(node);
        if (bone != null && !bone.IsDestroyed)
            _spine.Add(bone);
    }

    private static float BoneLen(Slot a, Slot b)
        => (a != null && b != null) ? float3.Distance(a.GlobalPosition, b.GlobalPosition) : 0f;

    private static LimbRest CaptureLimbRest(Slot root, Slot mid, Slot end)
    {
        if (root == null || mid == null || end == null)
            return default;
        return new LimbRest
        {
            Valid = true,
            Root = root.GlobalPosition,
            Mid = mid.GlobalPosition,
            End = end.GlobalPosition,
        };
    }

    private static floatQ ToeRel(Slot foot, Slot toe)
        => (foot != null && toe != null) ? foot.GlobalRotation.Inverse * toe.GlobalRotation : floatQ.Identity;

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

        SolveSpine(headTarget, pelvisTarget);

        // Turn since capture (flattened), so rest-blends and ground-aligned feet yaw with the body.
        float3 curFwd = headTarget.Rotation * float3.Backward;
        curFwd.y = 0f;
        curFwd = curFwd.LengthSquared > 1e-6f ? curFwd.Normalized : _restBodyForward;
        _bodyTurn = FabrikSolver.FromToRotation(_restBodyForward, curFwd);

        // Chest (second-from-top spine bone) anchors the arms.
        Slot chest = _spine.Count >= 2 ? _spine[_spine.Count - 2] : _spine[0];
        Slot hips = _spine[0];

        SolveArm(chest, _lShoulder, _lUpper, _lLower, _lHand, _lUpperArmLen, _lLowerArmLen, _lShoulderLen, in leftHand, in _lArmRest);
        SolveArm(chest, _rShoulder, _rUpper, _rLower, _rHand, _rUpperArmLen, _rLowerArmLen, _rShoulderLen, in rightHand, in _rArmRest);

        SolveLeg(hips, _lHip, _lKnee, _lFoot, _lToe, _lToeRel, _lUpperLegLen, _lLowerLegLen, in leftFoot, in _lLegRest);
        SolveLeg(hips, _rHip, _rKnee, _rFoot, _rToe, _rToeRel, _rUpperLegLen, _rLowerLegLen, in rightFoot, in _rLegRest);
    }

    private void SolveSpine(in Target headTarget, in Target pelvisTarget)
    {
        int n = _spine.Count;
        float3 headPos = headTarget.Position;

        // Anchor the hips. With a pelvis/waist tracker, drive them from it directly (true 3+ point
        // tracking); otherwise estimate them below the head, pushed back so the chest leans forward.
        float3 hipsPos;
        if (pelvisTarget.Valid)
        {
            hipsPos = pelvisTarget.Position;
        }
        else
        {
            float3 forward = headTarget.Rotation * float3.Backward;
            forward.y = 0f;
            forward = forward.LengthSquared > 1e-6f ? forward.Normalized : float3.Backward;
            hipsPos = headPos + new float3(0f, -_spineTotalLength, 0f) + forward * (_spineTotalLength * 0.12f);
        }

        // Damp toward the anchor so head/tracker noise doesn't snap the hips frame to frame, then
        // add the transient locomotion offset (kept out of the smoothed state so it stays responsive).
        if (_hasPrevHips)
            hipsPos = float3.Lerp(_prevHipsPos, hipsPos, PelvisDamp);
        _prevHipsPos = hipsPos;
        _hasPrevHips = true;
        float3 hipsFinal = hipsPos + LocomotionOffset;

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

        // Write spine bone positions + swing rotations.
        for (int i = 0; i < n - 1; i++)
        {
            var bone = _spine[i];
            WriteGlobalPosition(bone, _spineJoints[i]);
            ApplySwing(bone, _spineJoints[i + 1] - _spineJoints[i]);
        }

        // Head bone takes the target rotation directly.
        var head = _spine[n - 1];
        WriteGlobalPosition(head, _spineJoints[n - 1]);
        WriteGlobalRotation(head, headTarget.Rotation);

        // With a real pelvis tracker, the hips bone takes its rotation too.
        if (pelvisTarget.Valid)
            WriteGlobalRotation(_spine[0], pelvisTarget.Rotation);
    }

    private void SolveArm(Slot chest, Slot shoulder, Slot upper, Slot lower, Slot hand,
        float upperLen, float lowerLen, float shoulderLen, in Target target, in LimbRest rest)
    {
        if (upper == null || lower == null || hand == null || !target.Valid)
            return;

        float3 root = upper.GlobalPosition;

        // Position weight: blend the goal between the limb's rest hang (turned with the body) and the
        // full IK target, so the arm can fade out smoothly instead of hard-snapping.
        float pw = System.Math.Clamp(target.PositionWeight, 0f, 1f);
        float3 restEnd = root + _bodyTurn * (rest.Valid ? rest.End - rest.Root : target.Position - root);
        float3 goalPos = float3.Lerp(restEnd, target.Position, pw);

        // Shoulder solve: swing the clavicle partway toward the goal so reaching up/forward lifts the
        // whole arm (not just the elbow). Re-anchors the upper-arm root at the swung shoulder tip.
        if (shoulder != null && !shoulder.IsDestroyed && shoulderLen > 1e-4f
            && _restDir.TryGetValue(shoulder, out var shoulderRestDir)
            && _restRot.TryGetValue(shoulder, out var shoulderRestRot))
        {
            float3 shoulderPos = shoulder.GlobalPosition;
            float3 toGoal = goalPos - shoulderPos;
            if (toGoal.LengthSquared > 1e-6f)
            {
                var fullSwing = FabrikSolver.FromToRotation(shoulderRestDir, toGoal.Normalized);
                var swing = floatQ.Slerp(floatQ.Identity, fullSwing, ShoulderWeight);
                WriteGlobalRotation(shoulder, swing * shoulderRestRot);
                root = shoulderPos + (swing * shoulderRestDir) * shoulderLen;
            }
        }

        float3 chestForward = chest != null ? chest.GlobalRotation * float3.Backward : float3.Backward;
        float3 fallback = root + chestForward * 0.4f + new float3(0f, -0.4f, 0f);
        float3 pole = ComputePole(root, goalPos, in rest, fallback, target.HasBendGoal, target.BendGoal);

        Stretch(root, goalPos, ref upperLen, ref lowerLen);
        FabrikSolver.SolveTwoBone(root, pole, goalPos, upperLen, lowerLen, out var mid, out var end);

        WriteGlobalPosition(upper, root);
        ApplySwing(upper, mid - root);
        WriteGlobalPosition(lower, mid);
        ApplySwing(lower, end - mid);
        WriteGlobalPosition(hand, end);

        // Rotation weight: blend the hand between its rest orientation (turned) and the target.
        float rw = System.Math.Clamp(target.RotationWeight, 0f, 1f);
        floatQ restHandRot = _restRot.TryGetValue(hand, out var rhr) ? _bodyTurn * rhr : target.Rotation;
        floatQ handRot = floatQ.Slerp(restHandRot, target.Rotation, rw);
        WriteGlobalRotation(hand, handRot);

        // Twist relax: roll the forearm partway toward the hand's twist so it doesn't candy-wrap.
        ApplyTwistRelax(lower, end - mid, handRot);
    }

    private void SolveLeg(Slot hips, Slot hip, Slot knee, Slot foot, Slot toe, floatQ toeRel,
        float upperLen, float lowerLen, in Target target, in LimbRest rest)
    {
        if (hip == null || knee == null || foot == null || !target.Valid)
            return;

        float3 root = hip.GlobalPosition;

        float pw = System.Math.Clamp(target.PositionWeight, 0f, 1f);
        float3 restEnd = root + _bodyTurn * (rest.Valid ? rest.End - rest.Root : target.Position - root);
        float3 goalPos = float3.Lerp(restEnd, target.Position, pw);

        // Dynamic bend pole: knees bend forward following the rest pose; fallback in front of the hip.
        float3 hipsForward = hips != null ? hips.GlobalRotation * float3.Backward : float3.Backward;
        float3 fallback = root - hipsForward * 0.5f + new float3(0f, -0.2f, 0f);
        float3 pole = ComputePole(root, goalPos, in rest, fallback, target.HasBendGoal, target.BendGoal);

        Stretch(root, goalPos, ref upperLen, ref lowerLen);
        FabrikSolver.SolveTwoBone(root, pole, goalPos, upperLen, lowerLen, out var mid, out var end);

        WriteGlobalPosition(hip, root);
        ApplySwing(hip, mid - root);
        WriteGlobalPosition(knee, mid);
        ApplySwing(knee, end - mid);
        WriteGlobalPosition(foot, end);

        if (target.GroundAlign && _restRot.TryGetValue(foot, out var grFootRot))
        {
            // Procedural foot: rest pose, turned with the body and tilted onto the ground surface.
            // On flat ground GroundNormal == up so the tilt is identity and the foot sits at rest.
            floatQ tilt = floatQ.Identity;
            if (target.GroundNormal.LengthSquared > 1e-6f)
                tilt = FabrikSolver.FromToRotation(float3.Up, target.GroundNormal.Normalized);
            WriteGlobalRotation(foot, _bodyTurn * tilt * grFootRot);
        }
        else
        {
            float rw = System.Math.Clamp(target.RotationWeight, 0f, 1f);
            floatQ restFootRot = _restRot.TryGetValue(foot, out var rfr) ? _bodyTurn * rfr : target.Rotation;
            WriteGlobalRotation(foot, floatQ.Slerp(restFootRot, target.Rotation, rw));
        }

        SolveToe(foot, toe, toeRel, target.StepLift);
    }

    // Toe rides the foot at its captured relative orientation, curling around the foot's right axis
    // as the foot swings through a step (StepLift). Tracked/planted feet leave it at the rest ride.
    private void SolveToe(Slot foot, Slot toe, floatQ toeRel, float stepLift)
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

    // Pole vector for two-bone IK. With a captured rest pose, swing the rest mid-joint offset to
    // follow the current root->target direction so the elbow/knee bends naturally; an optional bend
    // goal then pulls the pole toward a steer point.
    private float3 ComputePole(float3 root, float3 targetPos, in LimbRest rest, float3 fallback,
        bool hasBendGoal, float3 bendGoal)
    {
        float3 pole;
        if (rest.Valid)
        {
            float3 restDir = rest.End - rest.Root;
            float3 curDir = targetPos - root;
            if (restDir.LengthSquared < 1e-6f || curDir.LengthSquared < 1e-6f)
                pole = fallback;
            else
            {
                var swing = FabrikSolver.FromToRotation(restDir, curDir);
                pole = root + swing * (rest.Mid - rest.Root);
            }
        }
        else
        {
            pole = fallback;
        }

        if (hasBendGoal)
            pole = float3.Lerp(pole, bendGoal, System.Math.Clamp(BendGoalWeight, 0f, 1f));
        return pole;
    }

    // Allow a limb to over-extend slightly (up to MaxStretch) when the target is past its reach,
    // instead of hard-clamping - keeps the hand/foot on target without a rigid snap.
    private void Stretch(float3 root, float3 targetPos, ref float upperLen, ref float lowerLen)
    {
        float reach = upperLen + lowerLen;
        if (reach < 1e-5f)
            return;
        float dist = float3.Distance(root, targetPos);
        if (dist <= reach)
            return;
        float scale = MathF.Min(dist / reach, MaxStretch);
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
}
