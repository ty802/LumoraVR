// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

// Engine-side full-body IK driver. Owns a BipedRig, captures rest bone lengths
// + rest directions once, then each frame: anchors hips from the head target,
// solves the spine with FABRIK, solves each limb with analytic two-bone IK,
// derives bone rotations from the solved directions, and writes world
// transforms back to the bone slots. No platform IK dependency. - xlinka
public sealed class FullBodyIKSolver
{
    public struct Target
    {
        public bool Valid;
        public float3 Position;
        public floatQ Rotation;
    }

    private BipedRig _rig = null!;
    private bool _captured;

    // Spine chain (filtered to existing bones, hips..head).
    private readonly List<Slot> _spine = new();
    private float[] _spineLengths = null!;
    private float3[] _spineJoints = null!;

    // Limb bone slots.
    private Slot _lShoulder = null!, _lUpper = null!, _lLower = null!, _lHand = null!;
    private Slot _rShoulder = null!, _rUpper = null!, _rLower = null!, _rHand = null!;
    private Slot _lHip = null!, _lKnee = null!, _lFoot = null!;
    private Slot _rHip = null!, _rKnee = null!, _rFoot = null!;

    // Rest bone lengths.
    private float _lUpperArmLen, _lLowerArmLen;
    private float _rUpperArmLen, _rLowerArmLen;
    private float _lUpperLegLen, _lLowerLegLen;
    private float _rUpperLegLen, _rLowerLegLen;
    private float _spineTotalLength;

    // Rest bone directions (bone -> child) in world space at capture time,
    // used to derive swing rotations.
    private readonly Dictionary<Slot, float3> _restDir = new();
    private readonly Dictionary<Slot, floatQ> _restRot = new();

    public bool IsReady => _captured && _rig != null && !_rig.IsDestroyed;

    // Bone writes are LOCAL: every peer runs this solve from the replicated
    // proxy poses, so broadcasting the results would have each peer's
    // float-noise-different skeleton fighting every other peer's - visible as
    // constant bone jitter. Change events still fire so transform caches and
    // the Godot hooks update. - xlinka
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

        _rHip = _rig.TryGetBone(BodyNode.RightUpperLeg);
        _rKnee = _rig.TryGetBone(BodyNode.RightLowerLeg);
        _rFoot = _rig.TryGetBone(BodyNode.RightFoot);

        _lUpperArmLen = BoneLen(_lUpper, _lLower);
        _lLowerArmLen = BoneLen(_lLower, _lHand);
        _rUpperArmLen = BoneLen(_rUpper, _rLower);
        _rLowerArmLen = BoneLen(_rLower, _rHand);
        _lUpperLegLen = BoneLen(_lHip, _lKnee);
        _lLowerLegLen = BoneLen(_lKnee, _lFoot);
        _rUpperLegLen = BoneLen(_rHip, _rKnee);
        _rLowerLegLen = BoneLen(_rKnee, _rFoot);

        CaptureRest(_spine);
        CaptureRestChain(_lShoulder, _lUpper, _lLower, _lHand);
        CaptureRestChain(_rShoulder, _rUpper, _rLower, _rHand);
        CaptureRestChain(_lHip, _lKnee, _lFoot, null!);
        CaptureRestChain(_rHip, _rKnee, _rFoot, null!);

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

    // Solve the full body for one frame. headTarget is required; the rest are
    // optional (limbs without a target fall back to rest reach forward).
    public void Solve(
        in Target headTarget,
        in Target leftHand,
        in Target rightHand,
        in Target leftFoot,
        in Target rightFoot)
    {
        if (!IsReady || !headTarget.Valid)
            return;

        SolveSpine(headTarget);

        // Chest (second-from-top spine bone) anchors the arms.
        Slot chest = _spine.Count >= 2 ? _spine[_spine.Count - 2] : _spine[0];
        Slot hips = _spine[0];

        SolveArm(chest, _lShoulder, _lUpper, _lLower, _lHand, _lUpperArmLen, _lLowerArmLen, leftHand, isLeft: true);
        SolveArm(chest, _rShoulder, _rUpper, _rLower, _rHand, _rUpperArmLen, _rLowerArmLen, rightHand, isLeft: false);

        SolveLeg(hips, _lHip, _lKnee, _lFoot, _lUpperLegLen, _lLowerLegLen, leftFoot);
        SolveLeg(hips, _rHip, _rKnee, _rFoot, _rUpperLegLen, _rLowerLegLen, rightFoot);
    }

    private void SolveSpine(in Target headTarget)
    {
        int n = _spine.Count;

        // Anchor hips below the head target by the spine length, pushed back
        // slightly so the chest leans forward naturally.
        float3 headPos = headTarget.Position;
        float3 forward = headTarget.Rotation * float3.Backward;
        forward.y = 0f;
        forward = forward.LengthSquared > 1e-6f ? forward.Normalized : float3.Backward;

        float3 hipsPos = headPos + new float3(0f, -_spineTotalLength, 0f) + forward * (_spineTotalLength * 0.12f);
        WriteGlobalPosition(_spine[0], hipsPos);

        for (int i = 0; i < n; i++)
            _spineJoints[i] = _spine[i].GlobalPosition;
        _spineJoints[0] = hipsPos;

        FabrikSolver.SolveChain(_spineJoints, _spineLengths, headPos, iterations: 12, tolerance: 0.001f);

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
    }

    private void SolveArm(Slot chest, Slot shoulder, Slot upper, Slot lower, Slot hand,
        float upperLen, float lowerLen, in Target target, bool isLeft)
    {
        if (upper == null || lower == null || hand == null)
            return;

        // Anchor the upper-arm root at the shoulder/chest-relative position.
        float3 root = (shoulder ?? chest)?.GlobalPosition ?? upper.GlobalPosition;
        if (shoulder == null)
            root = upper.GlobalPosition;

        if (!target.Valid)
            return;

        // Pole behind + below the shoulder so elbows bend down/back.
        float3 chestForward = chest != null ? chest.GlobalRotation * float3.Backward : float3.Backward;
        float3 pole = root + chestForward * 0.4f + new float3(0f, -0.4f, 0f);

        FabrikSolver.SolveTwoBone(root, pole, target.Position, upperLen, lowerLen, out var mid, out var end);

        WriteGlobalPosition(upper, root);
        ApplySwing(upper, mid - root);

        WriteGlobalPosition(lower, mid);
        ApplySwing(lower, end - mid);

        WriteGlobalPosition(hand, end);
        WriteGlobalRotation(hand, target.Rotation);
    }

    private void SolveLeg(Slot hips, Slot hip, Slot knee, Slot foot,
        float upperLen, float lowerLen, in Target target)
    {
        if (hip == null || knee == null || foot == null)
            return;

        float3 root = hip.GlobalPosition;

        if (!target.Valid)
            return;

        // Pole in front so knees bend forward.
        float3 hipsForward = hips != null ? hips.GlobalRotation * float3.Backward : float3.Backward;
        float3 pole = root - hipsForward * 0.5f + new float3(0f, -0.2f, 0f);

        FabrikSolver.SolveTwoBone(root, pole, target.Position, upperLen, lowerLen, out var mid, out var end);

        WriteGlobalPosition(hip, root);
        ApplySwing(hip, mid - root);

        WriteGlobalPosition(knee, mid);
        ApplySwing(knee, end - mid);

        WriteGlobalPosition(foot, end);
        if (target.Valid)
            WriteGlobalRotation(foot, target.Rotation);
    }

    // Swing a bone's rest direction onto the solved bone direction.
    private void ApplySwing(Slot bone, float3 solvedDir)
    {
        if (bone == null || solvedDir.LengthSquared < 1e-8f)
            return;
        if (!_restDir.TryGetValue(bone, out var restDir) || !_restRot.TryGetValue(bone, out var restRot))
            return;

        // restDir is already world-space (captured as child world pos - bone
        // world pos), so swing straight from it to the solved direction.
        var swing = FabrikSolver.FromToRotation(restDir, solvedDir);
        WriteGlobalRotation(bone, swing * restRot);
    }
}
