// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Drives avatar eye gaze (procedural idle saccades within a cone, or real eye tracking when available).
/// Works for BONE eyes (rotates the LeftEye/RightEye rig bones in their head-relative frame) AND for flat
/// texture/blendshape eyes (drives eyeLook Left/Right/Up/Down/In/Out blendshapes from the same gaze). Runs
/// locally on every peer; writes are local (no network churn), like the IK bone writes.
/// </summary>
[ComponentCategory("Users/Avatar")]
public sealed class EyeGazeDriver : Component
{
    public readonly Sync<float> MaxYawDegrees = new();
    public readonly Sync<float> MaxPitchDegrees = new();
    public readonly Sync<float> SaccadeMinInterval = new();
    public readonly Sync<float> SaccadeMaxInterval = new();
    public readonly Sync<float> Speed = new();

    private const float Deg2Rad = MathF.PI / 180f;

    private enum LookKind { Left, Right, Up, Down, In, Out }

    private Slot _head = null!, _leftEye = null!, _rightEye = null!;
    private floatQ _leftRestRel = floatQ.Identity, _rightRestRel = floatQ.Identity;
    private floatQ _headRestRot = floatQ.Identity;
    private float3 _restFront = float3.Backward;
    private bool _resolved;
    private EyeStreamManager? _eyeTracking;

    // Flat-eye gaze: eyeLook blendshapes (side: -1 combined, 0 left, 1 right). Driven from the same gaze.
    private readonly List<(SkinnedMeshRenderer Renderer, int Index, LookKind Kind, int Side)> _lookTargets = new();

    private readonly Random _rng = new();
    private float _timer;
    private float _next = 1.5f;
    private float _curYaw, _curPitch, _tgtYaw, _tgtPitch; // radians

    public override void OnInit()
    {
        base.OnInit();
        MaxYawDegrees.Value = 18f;
        MaxPitchDegrees.Value = 12f;
        SaccadeMinInterval.Value = 1.2f;
        SaccadeMaxInterval.Value = 4f;
        Speed.Value = 8f;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (!_resolved)
            Resolve();

        bool hasBones = _head != null && !_head.IsDestroyed && (_leftEye != null || _rightEye != null);
        bool hasLook = _lookTargets.Count > 0;
        if (!hasBones && !hasLook)
            return;

        // Target gaze comes from real eye tracking when the head device reports it; otherwise an idle
        // saccade picks a new target on a randomized cadence. Either way we smooth toward the target.
        if (TryRealGaze(out float realYaw, out float realPitch))
        {
            _tgtYaw = realYaw;
            _tgtPitch = realPitch;
        }
        else
        {
            _timer += delta;
            if (_timer >= _next)
            {
                _timer = 0f;
                float span = MathF.Max(0f, SaccadeMaxInterval.Value - SaccadeMinInterval.Value);
                _next = SaccadeMinInterval.Value + (float)_rng.NextDouble() * span;
                _tgtYaw = ((float)_rng.NextDouble() * 2f - 1f) * MaxYawDegrees.Value * Deg2Rad;
                _tgtPitch = ((float)_rng.NextDouble() * 2f - 1f) * MaxPitchDegrees.Value * Deg2Rad;
            }
        }

        // Frame-rate-independent smoothing toward the target gaze.
        float k = 1f - MathF.Exp(-MathF.Max(0.01f, Speed.Value) * delta);
        _curYaw += (_tgtYaw - _curYaw) * k;
        _curPitch += (_tgtPitch - _curPitch) * k;

        if (hasBones)
        {
            ApplyEye(_leftEye, _leftRestRel, _curYaw, _curPitch);
            ApplyEye(_rightEye, _rightRestRel, _curYaw, _curPitch);
        }

        if (hasLook)
            DriveLook(_curYaw, _curPitch);
    }

    // Map the current gaze (yaw/pitch radians) onto eyeLook blendshapes. +yaw rotates about Up = looking
    // LEFT (right-handed); +pitch about Right rotates local -Z upward. ARKit-style In/Out are per-eye
    // (In = toward the nose).
    private void DriveLook(float yaw, float pitch)
    {
        float maxYaw = MathF.Max(MaxYawDegrees.Value * Deg2Rad, 1e-3f);
        float maxPitch = MathF.Max(MaxPitchDegrees.Value * Deg2Rad, 1e-3f);

        float lookLeft = System.Math.Clamp(MathF.Max(yaw, 0f) / maxYaw, 0f, 1f);
        float lookRight = System.Math.Clamp(MathF.Max(-yaw, 0f) / maxYaw, 0f, 1f);
        float lookUp = System.Math.Clamp(MathF.Max(pitch, 0f) / maxPitch, 0f, 1f);
        float lookDown = System.Math.Clamp(MathF.Max(-pitch, 0f) / maxPitch, 0f, 1f);

        for (int i = 0; i < _lookTargets.Count; i++)
        {
            var (renderer, index, kind, side) = _lookTargets[i];
            if (renderer == null || renderer.IsDestroyed || !renderer.OwnsBlendShape(index, this))
            {
                _resolved = false; // mesh went away or our claim was lost - rescan next frame
                return;
            }

            float w = kind switch
            {
                LookKind.Left => lookLeft,
                LookKind.Right => lookRight,
                LookKind.Up => lookUp,
                LookKind.Down => lookDown,
                LookKind.In => side == 1 ? lookLeft : lookRight,   // left eye in = look right; right eye in = look left
                LookKind.Out => side == 1 ? lookRight : lookLeft,
                _ => 0f,
            };
            renderer.DriveBlendShapeWeight(index, w);
        }
    }

    // Eye = the authored eye pose carried by the live head, with the gaze applied as a WORLD-frame yaw
    // (about up) + pitch (about the gaze's right axis) around the head's current facing. The gaze must
    // never be composed in the eye BONE's local frame - its axes are arbitrary per rig, and rolling the
    // gaze through them sent eyes the wrong way (same trap as the head/feet aiming). -xlinka
    private void ApplyEye(Slot? eye, in floatQ restRelToHead, float yaw, float pitch)
    {
        if (eye == null || eye.IsDestroyed)
            return;

        floatQ baseRot = _head.GlobalRotation * restRelToHead;

        // Where the head currently faces: the rest body front carried by the head's rotation since rest.
        float3 front = (_head.GlobalRotation * _headRestRot.Inverse) * _restFront;
        float3 frontFlat = front;
        frontFlat.y = 0f;
        frontFlat = frontFlat.LengthSquared > 1e-6f ? frontFlat.Normalized : float3.Backward;

        floatQ gaze = floatQ.AxisAngleRad(float3.Up, yaw);
        float3 right = float3.Cross(frontFlat, float3.Up);
        if (right.LengthSquared > 1e-8f && MathF.Abs(pitch) > 1e-5f)
            gaze = floatQ.AxisAngleRad(right.Normalized, pitch) * gaze;

        floatQ eyeRot = gaze * baseRot;
        var parent = eye.Parent;
        var local = parent != null ? parent.GlobalRotationToLocal(eyeRot) : eyeRot;
        eye.LocalRotation.SetValueSilently(local, change: true);
    }

    // Real eye gaze from the user's eye-tracking stream, as yaw/pitch (radians) clamped to the cone.
    // Returns false when no eye tracking is active, so the caller falls back to the idle saccade. The
    // combined gaze is treated as a head-local direction (-Z forward); exact axis signs depend on the
    // device hook feeding the head device.
    private bool TryRealGaze(out float yaw, out float pitch)
    {
        yaw = 0f;
        pitch = 0f;

        _eyeTracking ??= Slot.ActiveUserRoot?.GetRegisteredComponent<EyeStreamManager>();
        if (_eyeTracking == null || !_eyeTracking.IsTracking)
            return false;
        if (!_eyeTracking.TryGetCombinedGaze(out var dir))
            return false;

        float len = MathF.Sqrt(dir.x * dir.x + dir.y * dir.y + dir.z * dir.z);
        if (len < 1e-4f)
            return false;
        dir /= len;

        // Lumora view forward is local -Z. Positive yaw is a leftward rotation around +Y, so +X
        // (viewer right) is negative yaw and -X (viewer left) is positive yaw.
        yaw = MathF.Atan2(-dir.x, -dir.z);
        pitch = MathF.Asin(System.Math.Clamp(dir.y, -1f, 1f));

        float maxYaw = MaxYawDegrees.Value * Deg2Rad;
        float maxPitch = MaxPitchDegrees.Value * Deg2Rad;
        yaw = System.Math.Clamp(yaw, -maxYaw, maxYaw);
        pitch = System.Math.Clamp(pitch, -maxPitch, maxPitch);
        return true;
    }

    private void Resolve()
    {
        var rig = Slot.GetComponentInChildren<HumanoidRig>() ?? Slot.GetComponent<HumanoidRig>();
        if (rig == null)
            return; // keep trying until a rig appears

        _head = rig.TryGetBone(BodyNode.Head);
        _leftEye = rig.TryGetBone(BodyNode.LeftEye);
        _rightEye = rig.TryGetBone(BodyNode.RightEye);

        if (_head != null && !_head.IsDestroyed)
        {
            var headInv = _head.GlobalRotation.Inverse;
            if (_leftEye != null && !_leftEye.IsDestroyed)
                _leftRestRel = headInv * _leftEye.GlobalRotation;
            if (_rightEye != null && !_rightEye.IsDestroyed)
                _rightRestRel = headInv * _rightEye.GlobalRotation;

            _headRestRot = _head.GlobalRotation;
            var front = rig.GuessForwardAxis();
            _restFront = front.HasValue && front.Value.LengthSquared > 1e-6f
                ? front.Value.Normalized
                : float3.Backward;
        }

        ResolveLookTargets();
        _resolved = true; // rig resolved; bone eyes and/or eyeLook blendshapes drive from here, else no-op
    }

    // Find eyeLook blendshapes (flat/texture eyes) and claim them so gaze can drive them. Disjoint from the
    // pupil/widen/squeeze/frown shapes claimed by EyeExpressionDriver.
    private void ResolveLookTargets()
    {
        _lookTargets.Clear();
        foreach (var renderer in Slot.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            for (int i = 0; i < renderer.BlendShapeCount; i++)
            {
                var name = renderer.BlendShapeName(i);
                if (string.IsNullOrEmpty(name))
                    continue;
                var classified = ClassifyLook(name.ToLowerInvariant());
                if (classified == null)
                    continue;
                if (renderer.ClaimBlendShape(i, this, SkinnedMeshRenderer.BlendShapePriorityEye))
                    _lookTargets.Add((renderer, i, classified.Value.Kind, classified.Value.Side));
            }
        }
    }

    private static (LookKind Kind, int Side)? ClassifyLook(string lower)
    {
        if (!lower.Contains("look"))
            return null;
        int side = SideOf(lower);
        if (lower.Contains("lookin")) return (LookKind.In, side);
        if (lower.Contains("lookout")) return (LookKind.Out, side);
        if (lower.Contains("lookup")) return (LookKind.Up, side);
        if (lower.Contains("lookdown")) return (LookKind.Down, side);
        if (lower.Contains("lookleft")) return (LookKind.Left, side);
        if (lower.Contains("lookright")) return (LookKind.Right, side);
        return null;
    }

    private static int SideOf(string lower)
    {
        if (lower.Contains("left")) return 0;
        if (lower.Contains("right")) return 1;
        if (EndsWithSideToken(lower, 'l')) return 0;
        if (EndsWithSideToken(lower, 'r')) return 1;
        return -1;
    }

    private static bool EndsWithSideToken(string s, char side)
    {
        if (s.Length < 2 || s[s.Length - 1] != side)
            return false;
        char sep = s[s.Length - 2];
        return sep == '_' || sep == '.' || sep == '-' || sep == ' ';
    }
}
