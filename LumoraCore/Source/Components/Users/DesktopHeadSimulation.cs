// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

public enum DesktopViewMode
{
    FirstPerson,
    ThirdPerson,
    FreeCam,
}

// Where the avatar's head points on a screen, as a function of what the view is doing. The VIEW (the
// rendering camera) and the HEAD are two different poses on desktop: in first person they coincide, in
// third person the camera orbits while the head only turns to follow it under neck rules, and in free
// cam the camera leaves the body altogether while the head stays where it was. The orbit used to be
// written straight onto the head, so spinning the camera spun the avatar's neck through 180 degrees in
// one frame and every mode switch snapped it. Nothing in here touches a slot or an engine: the owning
// component feeds a Frame in and reads the answer out, which is also what the headless check drives.
// Yaw is a world-space heading (0 = -Z, positive turns left), pitch is positive looking up. -xlinka
public sealed class DesktopHeadSimulation
{
    public const float TransitionSeconds = 0.25f;

    // Third person. The head chases its goal by this fraction per second, plus the metres the body moved
    // this frame (walking turns the head faster), scaled down toward HeadFollowFloor as the remaining
    // angle grows from 90 to 180 degrees so a camera flipped behind the head is a turn, not a whip.
    public const float HeadFollowRate = 8f;
    public const float HeadFollowFloor = 0.25f;
    // The face (body heading) comes round to the orbit once alignment is on: by the metres moved this
    // frame, or at FaceAlignRate per second once the orbit is more than 90 degrees off (ramped in over
    // 75..90). Alignment is on while moving and the orbit is within FaceAlignAngle of the face, or after
    // FaceAlignIntentSeconds of moving toward the orbit, or while a hand is ready to interact.
    public const float FaceAlignRate = 10f;
    public const float FaceAlignAngle = 60f * DegToRad;
    public const float FaceAlignIntentSeconds = 0.15f;
    public const float FaceBlendRate = 8f;
    // A hand ready to interact counts as walking toward the orbit at this many metres per second.
    public const float HandReadyMoveRate = 8f;
    // A pointing hand pulls the head toward where it points, from nothing at HandCaptureStart off it to
    // fully captured at HandCaptureFull, so the head never looks somewhere the hands cannot reach.
    public const float HandCaptureStart = 30f * DegToRad;
    public const float HandCaptureFull = 60f * DegToRad;
    public const float HandBlendRate = 8f;
    // Neck limits, applied last.
    public const float HeadYawLimit = 80f * DegToRad;
    public const float HeadPitchLimit = 89f * DegToRad;

    private const float DegToRad = MathF.PI / 180f;
    private const float FaceAlignRampStart = 75f * DegToRad;
    private const float FaceAlignRampEnd = 90f * DegToRad;
    private const float FollowScaleStart = 90f * DegToRad;
    private const float FollowScaleEnd = 180f * DegToRad;
    private const float BehindAngle = 90f * DegToRad;
    private const float MovingPerSecond = 0.01f;

    public struct Frame
    {
        public DesktopViewMode Mode;
        public float Delta;
        // First person look: the mouse-driven heading and pitch.
        public float LookYaw;
        public float LookPitch;
        // The root's heading, seeds the face when third person begins.
        public float BodyYaw;
        // Third person: the direction the rendering camera looks along, world space.
        public float3 ViewForward;
        // World-space metres the body moved this frame under input.
        public float3 Movement;
        // A hand is pointing at something it can act on, and the flat world direction it points.
        public bool HandReady;
        public float3 HandAimDirection;
    }

    public DesktopViewMode Mode { get; private set; } = DesktopViewMode.FirstPerson;
    public float HeadYaw { get; private set; }
    public float HeadPitch { get; private set; }
    public float FaceYaw { get; private set; }
    public float Transition { get; private set; } = 1f;
    public bool AlignActive => _alignActive;
    // The direction the head is closing on this frame (third person only).
    public float GoalYaw { get; private set; }
    public float GoalPitch { get; private set; }

    public floatQ HeadRotation => floatQ.Euler(HeadYaw, HeadPitch, 0f);
    public float3 HeadForward => ForwardOf(HeadYaw, HeadPitch);

    private bool _started;
    private float _simYaw;
    private float _simPitch;
    private float _residualYaw;
    private float _residualPitch;
    private bool _alignActive;
    private float _alignIntent;
    private float _faceBlend;
    private float _handBlend;
    private float _orbitYaw;
    private float _orbitPitch;
    private bool _orbitValid;

    public void Step(in Frame frame)
    {
        float dt = MathF.Max(frame.Delta, 0f);

        if (!_started)
        {
            _started = true;
            Mode = frame.Mode;
            _simYaw = Wrap(frame.LookYaw);
            _simPitch = ClampPitch(frame.LookPitch);
            FaceYaw = _simYaw;
            HeadYaw = _simYaw;
            HeadPitch = _simPitch;
            Transition = 1f;
        }

        bool switched = frame.Mode != Mode;
        float fromYaw = HeadYaw;
        float fromPitch = HeadPitch;
        if (switched)
            EnterMode(frame.Mode, frame.BodyYaw);

        switch (Mode)
        {
            case DesktopViewMode.FirstPerson:
                _simYaw = Wrap(frame.LookYaw);
                _simPitch = ClampPitch(frame.LookPitch);
                FaceYaw = _simYaw;
                break;
            case DesktopViewMode.ThirdPerson:
                StepThirdPerson(frame, dt);
                break;
            case DesktopViewMode.FreeCam:
                break;
        }

        // A switch carries whatever gap there is between the old answer and the new mode's first one
        // across TransitionSeconds on a smoother-step, riding on top of the live answer so the new mode
        // responds at once and the gap just fades. The modes are seeded to make that gap zero; this is
        // the guarantee for when they cannot be. -xlinka
        if (switched)
        {
            _residualYaw = Wrap(fromYaw - _simYaw);
            _residualPitch = fromPitch - _simPitch;
            Transition = 0f;
        }

        if (Transition < 1f)
        {
            Transition = MathF.Min(Transition + dt / TransitionSeconds, 1f);
            float fade = 1f - SmootherStep(Transition);
            HeadYaw = Wrap(_simYaw + _residualYaw * fade);
            HeadPitch = _simPitch + _residualPitch * fade;
        }
        else
        {
            HeadYaw = _simYaw;
            HeadPitch = _simPitch;
        }
    }

    // The body was re-based from outside (a seat, a placement): the face starts over from there.
    public void ResetFace(float yaw)
    {
        FaceYaw = Wrap(yaw);
        _alignIntent = 0f;
    }

    private void EnterMode(DesktopViewMode mode, float bodyYaw)
    {
        Mode = mode;
        // Every mode starts from where the head already is; the residual above only sees whatever the
        // mode's first step adds on top of that.
        _simYaw = HeadYaw;
        _simPitch = HeadPitch;
        switch (mode)
        {
            case DesktopViewMode.ThirdPerson:
                FaceYaw = Wrap(bodyYaw);
                _alignActive = false;
                _alignIntent = 0f;
                _faceBlend = 0f;
                _orbitValid = false;
                break;
            case DesktopViewMode.FreeCam:
                _alignActive = false;
                _alignIntent = 0f;
                break;
        }
    }

    private void StepThirdPerson(in Frame frame, float dt)
    {
        if (TryHeading(frame.ViewForward, out float viewYaw, out float viewPitch))
        {
            _orbitYaw = viewYaw;
            _orbitPitch = viewPitch;
            _orbitValid = true;
        }
        if (!_orbitValid)
        {
            // No view yet this session: nothing to follow, hold the pose.
            return;
        }

        float3 movement = frame.HandReady ? ForwardOf(_orbitYaw, _orbitPitch) * (dt * HandReadyMoveRate) : frame.Movement;
        float moveMagnitude = movement.Length;
        bool moving = moveMagnitude > dt * MovingPerSecond;

        if (moving)
        {
            if (MathF.Abs(Wrap(_orbitYaw - FaceYaw)) < FaceAlignAngle)
            {
                _alignActive = true;
            }
            else
            {
                if (TryHeading(movement, out float moveYaw, out _) && MathF.Abs(Wrap(moveYaw - _orbitYaw)) < FaceAlignAngle)
                    _alignIntent += dt;
                if (_alignIntent >= FaceAlignIntentSeconds)
                    _alignActive = true;
            }
        }
        else
        {
            _alignActive = false;
            _alignIntent = 0f;
        }

        if (_alignActive)
        {
            float gap = MathF.Abs(Wrap(_orbitYaw - FaceYaw));
            float rate = MathF.Min(MathF.Max(moveMagnitude, Clamp01(InverseLerp(FaceAlignRampStart, FaceAlignRampEnd, gap)) * dt * FaceAlignRate), 1f);
            FaceYaw = Wrap(FaceYaw + Wrap(_orbitYaw - FaceYaw) * rate);
        }
        _faceBlend = Clamp01(_faceBlend + (_alignActive ? dt : -dt) * FaceBlendRate);

        float handTarget = frame.HandReady ? 1f : 0f;
        _handBlend += (handTarget - _handBlend) * MathF.Min(dt * HandBlendRate, 1f);
        float handYaw = 0f;
        bool handValid = _handBlend > 1e-4f && TryHeading(frame.HandAimDirection, out handYaw, out _);

        float faceYaw = FaceYaw;
        float facePitch = 0f;
        if (handValid)
            CaptureByHand(ref faceYaw, ref facePitch, handYaw);

        // Camera behind the face: the head looks where the camera looks. Camera in front: the head looks
        // straight ahead, turning toward the lens only as alignment brings the face round.
        float goalYaw;
        float goalPitch;
        if (MathF.Abs(Wrap(faceYaw - _orbitYaw)) < BehindAngle)
        {
            goalYaw = _orbitYaw;
            goalPitch = _orbitPitch;
        }
        else
        {
            float towardYaw = Wrap(_orbitYaw + MathF.PI);
            float towardPitch = -_orbitPitch;
            goalYaw = Wrap(faceYaw + Wrap(towardYaw - faceYaw) * _faceBlend);
            goalPitch = towardPitch * _faceBlend;
        }
        if (handValid)
            CaptureByHand(ref goalYaw, ref goalPitch, handYaw);
        GoalYaw = goalYaw;
        GoalPitch = goalPitch;

        float separation = float3.Angle(ForwardOf(_simYaw, _simPitch), ForwardOf(goalYaw, goalPitch));
        float scale = 1f + (HeadFollowFloor - 1f) * Clamp01(InverseLerp(FollowScaleStart, FollowScaleEnd, separation));
        float follow = MathF.Min(dt * HeadFollowRate + moveMagnitude, 1f) * scale;
        _simYaw = Wrap(_simYaw + Wrap(goalYaw - _simYaw) * follow);
        _simPitch += (goalPitch - _simPitch) * follow;

        _simYaw = Wrap(FaceYaw + System.Math.Clamp(Wrap(_simYaw - FaceYaw), -HeadYawLimit, HeadYawLimit));
        _simPitch = ClampPitch(_simPitch);
    }

    private void CaptureByHand(ref float yaw, ref float pitch, float handYaw)
    {
        float off = MathF.Abs(Wrap(yaw - handYaw));
        float pull = _handBlend * Clamp01(InverseLerp(HandCaptureStart, HandCaptureFull, off));
        if (pull <= 0f)
            return;
        yaw = Wrap(yaw + Wrap(handYaw - yaw) * pull);
        pitch *= 1f - pull;
    }

    // The per-frame fraction the head may close on its goal by, exposed so the check can hold every
    // frame to it without re-deriving the rule.
    public static float FollowFraction(float dt, float moveMagnitude, float separation)
    {
        float scale = 1f + (HeadFollowFloor - 1f) * Clamp01(InverseLerp(FollowScaleStart, FollowScaleEnd, separation));
        return MathF.Min(dt * HeadFollowRate + moveMagnitude, 1f) * scale;
    }

    public static float3 ForwardOf(float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch);
        return new float3(-MathF.Sin(yaw) * cp, MathF.Sin(pitch), -MathF.Cos(yaw) * cp);
    }

    public static bool TryHeading(float3 direction, out float yaw, out float pitch)
    {
        float length = direction.Length;
        if (!(length > 1e-6f) || float.IsNaN(length))
        {
            yaw = 0f;
            pitch = 0f;
            return false;
        }
        direction /= length;
        yaw = MathF.Atan2(-direction.x, -direction.z);
        pitch = MathF.Asin(System.Math.Clamp(direction.y, -1f, 1f));
        return true;
    }

    // Only touches a value that is out of range, so a heading that is already wrapped comes back bit for
    // bit the same; the free-cam hold relies on that.
    public static float Wrap(float radians)
    {
        const float twoPi = MathF.PI * 2f;
        if (radians > MathF.PI || radians < -MathF.PI)
            radians -= twoPi * MathF.Floor((radians + MathF.PI) / twoPi);
        return radians;
    }

    public static float SmootherStep(float t)
    {
        t = Clamp01(t);
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private static float ClampPitch(float pitch) => System.Math.Clamp(pitch, -HeadPitchLimit, HeadPitchLimit);

    private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

    private static float InverseLerp(float a, float b, float value)
        => b - a > 1e-9f ? (value - a) / (b - a) : (value >= b ? 1f : 0f);
}
