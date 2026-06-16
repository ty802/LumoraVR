// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

/// <summary>
/// Drives the avatar's eye bones with procedural gaze (idle saccades within a cone). Rotates the
/// LeftEye/RightEye rig bones in their head-relative frame, so it tracks head motion automatically.
/// Runs locally on every peer; writes are local (no network churn), like the IK bone writes.
/// </summary>
[ComponentCategory("Users/Common Avatar System")]
public sealed class EyeRotationDriver : Component
{
    public readonly Sync<float> MaxYawDegrees = new();
    public readonly Sync<float> MaxPitchDegrees = new();
    public readonly Sync<float> SaccadeMinInterval = new();
    public readonly Sync<float> SaccadeMaxInterval = new();
    public readonly Sync<float> Speed = new();

    private const float Deg2Rad = MathF.PI / 180f;

    private Slot _head = null!, _leftEye = null!, _rightEye = null!;
    private floatQ _leftRestRel = floatQ.Identity, _rightRestRel = floatQ.Identity;
    private bool _resolved;

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
        if (_head == null || _head.IsDestroyed || (_leftEye == null && _rightEye == null))
            return;

        // Pick a new idle gaze target on a randomized cadence.
        _timer += delta;
        if (_timer >= _next)
        {
            _timer = 0f;
            float span = MathF.Max(0f, SaccadeMaxInterval.Value - SaccadeMinInterval.Value);
            _next = SaccadeMinInterval.Value + (float)_rng.NextDouble() * span;
            _tgtYaw = ((float)_rng.NextDouble() * 2f - 1f) * MaxYawDegrees.Value * Deg2Rad;
            _tgtPitch = ((float)_rng.NextDouble() * 2f - 1f) * MaxPitchDegrees.Value * Deg2Rad;
        }

        // Frame-rate-independent smoothing toward the target gaze.
        float k = 1f - MathF.Exp(-MathF.Max(0.01f, Speed.Value) * delta);
        _curYaw += (_tgtYaw - _curYaw) * k;
        _curPitch += (_tgtPitch - _curPitch) * k;

        // TODO(eye-tracking): when a HeadDevice exposes Left/RightEyeGazeDirection, drive the gaze
        // from real eye tracking instead of the procedural saccade above.
        var gaze = floatQ.AxisAngle(float3.Up, _curYaw) * floatQ.AxisAngle(float3.Right, _curPitch);

        ApplyEye(_leftEye, _leftRestRel, gaze);
        ApplyEye(_rightEye, _rightRestRel, gaze);
    }

    private void ApplyEye(Slot? eye, in floatQ restRelToHead, in floatQ gaze)
    {
        if (eye == null || eye.IsDestroyed)
            return;
        // Eye = head orientation * its rest offset from the head * gaze (in the eye's local frame).
        floatQ eyeRot = (_head.GlobalRotation * restRelToHead) * gaze;
        var parent = eye.Parent;
        var local = parent != null ? parent.GlobalRotationToLocal(eyeRot) : eyeRot;
        eye.LocalRotation.SetValueSilently(local, change: true);
    }

    private void Resolve()
    {
        var rig = Slot.GetComponentInChildren<BipedRig>() ?? Slot.GetComponent<BipedRig>();
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
        }

        _resolved = true; // rig resolved; if it has no eye bones we simply no-op
    }
}
