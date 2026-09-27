// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar.IK;

// Alternating ground contacts with fixed swing endpoints. Positions are planar world coordinates;
// the caller resolves the contact floor and adds ArcHeight. -xlinka
internal struct BipedGaitParameters
{
    public float ReferenceSpeed;                 // m/s at which FootTravelDistance is taken in full
    // How close a landing foot may come to the one already down, in metres at avatar scale. The
    // lateral rule below stops a sidestep crossing the support leg, but it only looks along one axis:
    // a turn on the spot brings the two feet together along the OTHER one, and one paw walks straight
    // through the other. A radius does not care which way they closed. -xlinka
    public float AvoidanceRadius;

    public float MinOpposingProgress;            // swing progress at which the other foot lifts
    public float VelocityIncreaseSmoothTime;     // s, ramp up
    public float VelocityDecreaseSmoothTime;     // s, ramp down
    public float FootTravelDistance;             // m one step covers at ReferenceSpeed
    public float FootSidewaysDistanceRatio;      // stride scale when strafing
    public float FootBackwardsDistanceRatio;     // stride scale when walking backwards
    public float FootMaxAngle;                   // deg of facing error a planted foot tolerates
    public float FootTurnInfluence;              // 0..1 of the anticipated turn taken out of the travel vector
    public float FootStepUpHeight;               // m arc height at lift
    public float FootStepDownHeight;             // m arc height at landing
    public float FootStepUpPow;                  // arc sharpness at lift
    public float FootStepDownPow;                // arc sharpness at landing
    public float StepUpVelocityMultiplier;       // how hard the foot peels off the ground
    public float StepUpVerticalRatio;            // 0..1 of that peel that goes upward
    public float MaxSwingTime;                   // s, hard ceiling on one swing
    public float RestAllowance;                  // m of rest error tolerated while standing still
    public float MaxSpeed;                       // m/s cap on the smoothed gait speed
    public float MaxStepHeight;                  // m ceiling on the arc
    public float FootGroundAngle;                // deg a planted foot pitches at half a stride from rest (toe up ahead, toe down behind)
    public float FootGroundPow;                  // shape of that pitch over the offset from rest
    public float FootStepUpAngularVelocityMultiplier; // deg/s per m/s of body speed the foot keeps pitching after lift-off
    public float FootStepDownAngle;              // deg of heel-first pitch a foot lands with
    public float FootTiltMinAngle;               // deg, toe-up limit of the lift pitch
    public float FootTiltMaxAngle;               // deg, toe-down limit of the lift pitch
    public float Scale;                          // avatar scale, multiplies every distance

    public static BipedGaitParameters Default => new()
    {
        ReferenceSpeed = 1.4f,
        AvoidanceRadius = 0.085f,
        MinOpposingProgress = 0.72f,
        VelocityIncreaseSmoothTime = 0.22f,
        VelocityDecreaseSmoothTime = 0.35f,
        FootTravelDistance = 0.62f,
        FootSidewaysDistanceRatio = 0.5f,
        FootBackwardsDistanceRatio = 0.75f,
        FootMaxAngle = 25f,
        FootTurnInfluence = 0.2f,
        FootStepUpHeight = 0.1f,
        FootStepDownHeight = 0.05f,
        FootStepUpPow = 0.4f,
        FootStepDownPow = 0.4f,
        StepUpVelocityMultiplier = 0.25f,
        StepUpVerticalRatio = 0.75f,
        MaxSwingTime = 0.75f,
        RestAllowance = 0.14f,
        MaxSpeed = 6f,
        MaxStepHeight = 0.45f,
        FootGroundAngle = 10f,
        FootGroundPow = 1.2f,
        FootStepUpAngularVelocityMultiplier = 475f,
        FootStepDownAngle = 20f,
        FootTiltMinAngle = -30f,
        FootTiltMaxAngle = 160f,
        Scale = 1f
    };
}

internal sealed class GaitFoot
{
    public GaitFoot Opposing = null!;

    public bool Initialized;
    public bool IsTravelling;
    public float TravelProgress;
    public float3 Position = float3.Zero;        // planar world
    public float3 Facing = float3.Backward;
    public float ArcHeight;                      // m above the caller's ground line
    public float StepLift;                       // 0..1 normalized arc, drives the toe curl
    public float Pitch;                          // deg about the foot's right axis, positive = toe down
    public float3 RestPosition = float3.Zero;
    public float3 RestFacing = float3.Backward;
    public float3 TargetPosition = float3.Zero;
    public float3 TargetFacing = float3.Backward;
    public float3 SourcePosition = float3.Zero;
    public float3 SourceFacing = float3.Backward;
    public bool WantsToMove;
    public float MovePriority;
    public float FootingTime;
    public float TravellingTime;

    internal float3 LocalRest = float3.Zero;
    internal float SwingDuration;
    internal float SwingHeight;
    // A swing is clocked by the ground the body covers: RawProgress runs past 1 so the overshoot on the frame
    // it crosses the handover gate can be given to the other foot, which keeps the pair phase-locked at any
    // frame rate. SwingFloorTime drives the swing on when the body does not. -xlinka
    internal float RawProgress;
    internal float SwingDistance;
    internal float SwingFloorTime;
    internal float ProgressRate;
    // Pitch state: the angle the foot lifted off with keeps integrating at LiftAngularVelocity through the swing
    // and is blended into the landing pitch; GroundLerp carries the landing pitch into footing and fades as the
    // body walks over the foot. -xlinka
    internal float LiftAngle;
    internal float LiftAngularVelocity;
    internal float GroundLerp;

    public bool IsPlanted => Initialized && !IsTravelling;

    internal void Reset()
    {
        Initialized = false;
        IsTravelling = false;
        TravelProgress = 0f;
        RawProgress = 0f;
        ProgressRate = 0f;
        ArcHeight = 0f;
        StepLift = 0f;
        Pitch = 0f;
        LiftAngle = 0f;
        LiftAngularVelocity = 0f;
        GroundLerp = 0f;
        WantsToMove = false;
        MovePriority = 0f;
        FootingTime = 0f;
        TravellingTime = 0f;
    }
}

internal sealed class BipedGait
{
    private const float StillMinFootingTime = 0.1f;
    private const float MovingMinFootingTime = 0.05f;
    private const float MinSwingTime = 0.12f;
    private const float StillSwingTime = 0.3f;
    private const float StillSidewaysRatio = 0.6f;
    private const float MaxTravelScale = 2f;
    private const float MinStrideRatio = 0.4f;
    private const float YawRateSmoothTime = 0.1f;
    private const float GlideSmoothTime = 0.12f;
    private const float DirectionSmoothTime = 0.08f;
    private const float ReachLookaheadTime = 0.5f;
    private const float FirstSwingScale = 0.6f;
    private const float MaxHeadStart = 0.5f;

    public BipedGaitParameters Parameters = BipedGaitParameters.Default;

    public GaitFoot Left { get; } = new();
    public GaitFoot Right { get; } = new();

    public float SmoothSpeed { get; private set; }
    public float3 SmoothDirection { get; private set; } = float3.Backward;
    public float3 SmoothVelocity => SmoothDirection * SmoothSpeed;
    public float YawRate { get; private set; }
    public bool IsStandingStill { get; private set; } = true;
    // 0 standing, 1 once the body clearly walks; gates how much of the landing pitch a planted foot keeps.
    public float MinVelocityLerp { get; private set; }

    private float _separation = 0.2f;
    private float3 _body = float3.Zero;
    private float3 _forward = float3.Backward;
    private float3 _right = float3.Right;
    private float3 _rawDirection = float3.Backward;
    private float _rawSpeed;
    private float _bodyDelta;
    private float _speedVelocity;
    private float _yawVelocity;
    private bool _hasPreviousFrame;
    private float3 _prevForward = float3.Backward;
    // Stride geometry for the current speed: a planted foot may stand up to _stepAllowance from its target,
    // one swing spans _swingDistance of body travel, and a foot lands _reach ahead of its rest. -xlinka
    private float _stepAllowance;
    private float _swingDistance = 0.3f;
    private float _reach;
    private float _avoidanceRadius = 0.08f;
    private GaitFoot? _nextFoot;
    private bool _wasMoving;
    private int _startupSteps;

    public BipedGait()
    {
        Left.Opposing = Right;
        Right.Opposing = Left;
    }

    // Stance taken from the rig: how far apart the feet rest and how far behind the body centre they sit.
    public void SetRest(float separation, float backwardsOffset, float leftSide)
    {
        _separation = MathF.Max(separation, 0.02f);
        float back = MathF.Max(backwardsOffset, 0f);
        float side = leftSide >= 0f ? 1f : -1f;
        float half = _separation * 0.5f;
        Left.LocalRest = new float3(side * half, 0f, -back);
        Right.LocalRest = new float3(-side * half, 0f, -back);
    }

    public void Reset()
    {
        Left.Reset();
        Right.Reset();
        SmoothSpeed = 0f;
        _rawSpeed = 0f;
        _bodyDelta = 0f;
        _speedVelocity = 0f;
        _yawVelocity = 0f;
        YawRate = 0f;
        _hasPreviousFrame = false;
        IsStandingStill = true;
        MinVelocityLerp = 0f;
        _nextFoot = null;
        _wasMoving = false;
        _startupSteps = 0;
    }

    // The whole root jumped (teleport, a world move): carry the feet with it rather than let them
    // re-step from wherever they were left behind. -xlinka
    public void Carry(float3 delta)
    {
        delta.y = 0f;
        CarryFoot(Left, delta);
        CarryFoot(Right, delta);
    }

    private static void CarryFoot(GaitFoot foot, float3 delta)
    {
        if (!foot.Initialized)
            return;
        foot.Position += delta;
        foot.SourcePosition += delta;
        foot.TargetPosition += delta;
    }

    // Feet off the ground: no stepping, they just trail their rest place so touch-down resumes from
    // wherever they actually are.
    public void Glide(float3 bodyPlanar, float3 bodyForward, float dt)
    {
        if (_hasPreviousFrame)
            Carry(bodyPlanar - _body);
        SetFrame(bodyPlanar, bodyForward);
        _hasPreviousFrame = true;
        _prevForward = _forward;
        UpdateRest();
        SmoothSpeed = 0f;
        _rawSpeed = 0f;
        _bodyDelta = 0f;
        IsStandingStill = true;
        MinVelocityLerp = 0f;
        _wasMoving = false;
        _speedVelocity = 0f;
        _nextFoot = null;
        ComputeTravelMetrics();
        GlideFoot(Left, dt);
        GlideFoot(Right, dt);
    }

    private void GlideFoot(GaitFoot foot, float dt)
    {
        if (!foot.Initialized)
        {
            PlantAtRest(foot);
            return;
        }
        foot.IsTravelling = false;
        foot.TravelProgress = 0f;
        foot.RawProgress = 0f;
        foot.StepLift = 0f;
        float k = SmoothingFactor(GlideSmoothTime, dt);
        foot.Position = float3.Lerp(foot.Position, foot.RestPosition, k);
        foot.Facing = SlerpFlat(foot.Facing, foot.RestFacing, k);
        foot.ArcHeight *= 1f - k;
        foot.Pitch *= 1f - k;
        foot.LiftAngle = foot.Pitch;
        foot.GroundLerp = 0f;
        foot.SourcePosition = foot.Position;
        foot.SourceFacing = foot.Facing;
        foot.TargetPosition = foot.Position;
        foot.TargetFacing = foot.Facing;
        foot.FootingTime = 0f;
    }

    public void Update(float3 bodyPlanar, float3 bodyForward, float3 rawVelocity, float dt)
    {
        if (dt <= 0f)
            return;

        SetFrame(bodyPlanar, bodyForward);
        UpdateYawRate(dt);
        UpdateVelocity(rawVelocity, dt);
        UpdateRest();
        ComputeTravelMetrics();

        if (!Left.Initialized) PlantAtRest(Left);
        if (!Right.Initialized) PlantAtRest(Right);

        UpdateFooting(Left, dt);
        UpdateFooting(Right, dt);
        UpdateTravel(Left, dt);
        UpdateTravel(Right, dt);

        bool moving = !IsStandingStill;
        if (moving && !_wasMoving)
        {
            _startupSteps = 1;
            float lateral = float3.Dot(_rawDirection, _right);
            _nextFoot = MathF.Abs(lateral) > 0.5f
                ? (MathF.Sign(lateral) == MathF.Sign(Left.LocalRest.x) ? Left : Right)
                : (Left.MovePriority >= Right.MovePriority ? Left : Right);
        }
        _wasMoving = moving;

        if (moving)
            UpdateWalkingStarts(dt);
        else
            UpdateStandingStarts(dt);
    }

    private void SetFrame(float3 bodyPlanar, float3 bodyForward)
    {
        bodyPlanar.y = 0f;
        _body = bodyPlanar;
        _forward = FlattenDir(bodyForward, _forward);
        _right = float3.Cross(_forward, float3.Up);
        _right = _right.LengthSquared > 1e-8f ? _right.Normalized : float3.Right;
    }

    private void UpdateYawRate(float dt)
    {
        float raw = 0f;
        if (_hasPreviousFrame)
        {
            float cross = float3.Dot(float3.Cross(_prevForward, _forward), float3.Up);
            float dot = System.Math.Clamp(float3.Dot(_prevForward, _forward), -1f, 1f);
            raw = MathF.Atan2(cross, dot) / dt;
        }
        _prevForward = _forward;
        _hasPreviousFrame = true;
        YawRate = SmoothDamp(YawRate, raw, ref _yawVelocity, YawRateSmoothTime, dt);
    }

    // Separate rise and fall times: a walk that starts blends in, a stop blends out, and neither is the
    // on/off flag that used to snap the whole stance the instant the stick moved. The raw speed still
    // decides standing versus walking and clocks the swings; the smoothed one shapes the stride. -xlinka
    private void UpdateVelocity(float3 rawVelocity, float dt)
    {
        rawVelocity.y = 0f;
        float scale = MathF.Max(Parameters.Scale, 1e-4f);
        _rawSpeed = MathF.Min(rawVelocity.Length, MathF.Max(Parameters.MaxSpeed, 0.1f) * scale);
        _bodyDelta = _rawSpeed * dt;
        if (rawVelocity.LengthSquared > 1e-8f)
            _rawDirection = rawVelocity.Normalized;
        float smoothTime = _rawSpeed > SmoothSpeed
            ? Parameters.VelocityIncreaseSmoothTime
            : Parameters.VelocityDecreaseSmoothTime;
        SmoothSpeed = SmoothDamp(SmoothSpeed, _rawSpeed, ref _speedVelocity, MathF.Max(smoothTime, 1e-3f), dt);
        SmoothSpeed = MathF.Max(SmoothSpeed, 0f);
        if (rawVelocity.LengthSquared > 1e-8f)
            SmoothDirection = SlerpFlat(SmoothDirection, _rawDirection, SmoothingFactor(DirectionSmoothTime, dt));
        float still = 0.06f * scale;
        IsStandingStill = _rawSpeed < still;
        MinVelocityLerp = System.Math.Clamp((SmoothSpeed - still) / (0.1f * scale), 0f, 1f);
    }

    private void UpdateRest()
    {
        UpdateRest(Left);
        UpdateRest(Right);
        _avoidanceRadius = MathF.Max(0.05f, _separation * 0.4f) * MathF.Max(Parameters.Scale, 1e-4f);
    }

    private void UpdateRest(GaitFoot foot)
    {
        float scale = MathF.Max(Parameters.Scale, 1e-4f);
        foot.RestPosition = _body + _right * (foot.LocalRest.x * scale) + _forward * (foot.LocalRest.z * scale);
        foot.RestFacing = _forward;
    }

    private float OpposingGate => System.Math.Clamp(Parameters.MinOpposingProgress, 0.5f, 1f);

    private float MaxSwing => MathF.Max(Parameters.MaxSwingTime, MinSwingTime);

    // Step length grows with the square root of speed. With the other foot lifting at the gate g of a swing,
    // one swing spans stepLength / g of body travel and the two feet meet the ground stepLength * (1 - 1 / 2g)
    // ahead of rest, so a planted foot is exactly one allowance from its target when its turn comes. The reach
    // itself fades to zero with the smoothed speed so a start or a stop never snaps a target. -xlinka
    private void ComputeTravelMetrics()
    {
        float scale = MathF.Max(Parameters.Scale, 1e-4f);
        float3 local = ToLocal(SmoothDirection);
        float ratio = Lerp(Lerp(1f, Parameters.FootBackwardsDistanceRatio, MathF.Max(0f, -local.z)),
            Parameters.FootSidewaysDistanceRatio, MathF.Abs(local.x));
        float reference = MathF.Max(Parameters.ReferenceSpeed, 0.05f) * scale;
        float speedScale = System.Math.Clamp(SmoothSpeed / reference, 0f, MaxTravelScale);
        float stepLength = MathF.Max(Parameters.FootTravelDistance, 0.01f) * ratio * scale
            * MathF.Max(MathF.Sqrt(speedScale), MinStrideRatio);
        float gate = OpposingGate;
        _swingDistance = stepLength / gate;
        float reach = stepLength * (1f - 0.5f / gate);
        _stepAllowance = MathF.Max(2f * reach, stepLength * 0.2f);
        _reach = MathF.Min(reach, SmoothSpeed * ReachLookaheadTime);
    }

    private void PlantAtRest(GaitFoot foot)
    {
        foot.Position = foot.RestPosition;
        foot.Facing = foot.RestFacing;
        foot.SourcePosition = foot.Position;
        foot.SourceFacing = foot.Facing;
        foot.TargetPosition = foot.Position;
        foot.TargetFacing = foot.Facing;
        foot.ArcHeight = 0f;
        foot.StepLift = 0f;
        foot.Pitch = 0f;
        foot.LiftAngle = 0f;
        foot.GroundLerp = 0f;
        foot.IsTravelling = false;
        foot.TravelProgress = 0f;
        foot.RawProgress = 0f;
        foot.FootingTime = 1f;
        foot.Initialized = true;
    }

    private void UpdateFooting(GaitFoot foot, float dt)
    {
        if (foot.IsTravelling)
            return;

        foot.FootingTime += dt;
        float swingTime = MathF.Min(_swingDistance / MathF.Max(SmoothSpeed, 1e-3f), MaxSwing);
        SetTarget(foot, swingTime, 0f);

        if (IsStandingStill)
            UpdateStandingIntent(foot);
        else
            UpdateWalkingIntent(foot);

        if (!foot.WantsToMove)
            foot.MovePriority = 0f;

        UpdateFootingPitch(foot);
    }

    // A planted foot pitches by how far it stands from its rest along the body: heel down and toe up when the
    // body has yet to walk over it, toe down and heel rising once it is behind. The magnitude starts at the angle
    // the foot landed with and fades to the ground angle as the foot passes under the body. -xlinka
    private void UpdateFootingPitch(GaitFoot foot)
    {
        float offset = ToLocal(foot.Position - _body).z - ToLocal(foot.RestPosition - _body).z;
        float sign = MathF.Sign(offset);
        float value = System.Math.Clamp(MathF.Abs(offset) / (MathF.Max(_stepAllowance, 1e-3f) * 0.5f), 0f, 1f);
        foot.GroundLerp = MathF.Min(MathF.Min(MathF.Pow(value * 1.25f, 2f), foot.GroundLerp), MinVelocityLerp);
        float angle = Lerp(Parameters.FootGroundAngle, MathF.Abs(foot.LiftAngle), foot.GroundLerp);
        foot.Pitch = MathF.Pow(value, MathF.Max(Parameters.FootGroundPow, 0.05f)) * -sign * angle;
    }

    private void UpdateWalkingIntent(GaitFoot foot)
    {
        float distance = FlatDistance(foot.Position, foot.TargetPosition);
        float angle = FlatAngleDeg(foot.Facing, foot.TargetFacing);
        float allowance = MathF.Max(_stepAllowance, 1e-3f);
        float angleAllowance = MathF.Max(Parameters.FootMaxAngle, 1f);
        foot.WantsToMove = distance >= allowance || angle >= angleAllowance;
        foot.MovePriority = distance / allowance + angle / angleAllowance;
    }

    // Standing still is not "walk in place slower": a foot only moves when the stance itself has gone
    // wrong, so a body that shuffles a couple of centimetres takes no steps at all.
    private void UpdateStandingIntent(GaitFoot foot)
    {
        var other = foot.Opposing;
        float scale = MathF.Max(Parameters.Scale, 1e-4f);
        float allowance = MathF.Max(Parameters.RestAllowance, 0.01f) * scale;

        float3 local = ToLocal(foot.Position - _body);
        float3 otherLocal = ToLocal(other.Position - _body);
        float3 restLocal = ToLocal(foot.RestPosition - _body);
        float3 otherRestLocal = ToLocal(other.RestPosition - _body);

        float spread = MathF.Abs(local.x - otherLocal.x);
        float restSpread = MathF.Abs(restLocal.x - otherRestLocal.x);
        float centreX = (local.x + otherLocal.x) * 0.5f;
        float pairForward = MathF.Abs((local.z + otherLocal.z) * 0.5f);
        float radius = FlatLength(local);
        float restError = FlatDistance(foot.Position, foot.RestPosition);
        float angleError = FlatAngleDeg(foot.Facing, foot.RestFacing);

        bool wants = MathF.Abs(centreX) >= spread * StillSidewaysRatio
            || pairForward >= allowance
            || radius - restSpread >= allowance
            || restError >= allowance
            || spread <= _avoidanceRadius
            || angleError > MathF.Max(Parameters.FootMaxAngle, 1f);

        if (wants && foot.FootingTime < StillMinFootingTime)
            wants = false;
        // A TURN STEP GOES EVEN WHEN IT CROSSES.
        //
        // After a half turn on the spot every rest sits on the far side of the other leg and right
        // next to it, so the crossing gate and the spacing gate below refused both feet forever: the
        // feet stayed planted facing the old way, the solver takes its facing from the feet, and the
        // wearer looked round at the back of their own body. So an angle error steps regardless of the
        // crossing test; the landing itself is still pushed clear of the other foot in SetTarget, so the
        // two never share a spot. -xlinka
        bool turning = angleError > MathF.Max(Parameters.FootMaxAngle, 1f);
        if (wants && !turning && MathF.Sign(restLocal.x - otherRestLocal.x) != MathF.Sign(restLocal.x - otherLocal.x))
            wants = false;   // stepping would cross the other leg
        if (wants && !turning && FlatDistance(foot.RestPosition, other.Position) < _avoidanceRadius)
            wants = false;

        wants &= restError > allowance * 0.65f || angleError > MathF.Max(Parameters.FootMaxAngle, 1f);

        foot.WantsToMove = wants;
        if (wants)
            foot.MovePriority = restError / allowance + angleError / MathF.Max(Parameters.FootMaxAngle, 1f);
    }

    // Where the foot meets the ground: its rest carried by the anticipated turn, pushed along the travel by the
    // reach plus whatever ground the body still covers before touch-down. Recomputed every frame so a swing
    // follows a changing speed or heading instead of landing on a stale prediction. -xlinka
    private void SetTarget(GaitFoot foot, float timeToLand, float remaining)
    {
        float scale = MathF.Max(Parameters.Scale, 1e-4f);
        float turn = YawRate * timeToLand * System.Math.Clamp(Parameters.FootTurnInfluence, 0f, 1f);
        floatQ turnRotation = floatQ.AxisAngleRad(float3.Up, turn);
        float3 target = _body + turnRotation * (foot.RestPosition - _body) + SmoothDirection * (_reach + remaining);
        float3 facing = FlattenDir(turnRotation * foot.RestFacing, foot.RestFacing);

        // A trailing sidestep closes the gap without crossing the support leg. -xlinka
        float side = MathF.Sign(foot.LocalRest.x);
        float separation = float3.Dot(target - foot.Opposing.Position, _right) * side;
        float minimum = _separation * scale * 0.65f;
        if (separation < minimum)
            target += _right * side * (minimum - separation);

        // And whatever is left after that, in any direction. Pushed out along the line between the
        // two feet, which is the shortest way out and the one that disturbs the step least.
        float radius = Parameters.AvoidanceRadius * scale;
        if (radius > 1e-4f)
        {
            float3 apart = target - foot.Opposing.Position;
            apart.y = 0f;
            float distance = apart.Length;
            if (distance < radius)
            {
                // Dead on top of each other: push along the body's right, away from the other foot,
                // because there is no line between them to push along.
                float3 away = distance > 1e-4f ? apart / distance : _right * side;
                target += away * (radius - distance);
            }
        }

        foot.TargetPosition = target;
        foot.TargetFacing = facing;
    }

    // Walking, the pair is locked: the planted foot lifts the moment the swinging one crosses the gate, with a
    // head start equal to the ground covered past it that frame. Both planted while the body moves only happens
    // at a start, where the chosen foot goes at once rather than dragging a full allowance behind. -xlinka
    private void UpdateWalkingStarts(float dt)
    {
        if (Left.IsTravelling && Right.IsTravelling)
            return;
        if (Left.IsTravelling || Right.IsTravelling)
        {
            GaitFoot flying = Left.IsTravelling ? Left : Right;
            GaitFoot next = flying.Opposing;
            float gate = OpposingGate;
            if (flying.RawProgress >= gate && next.FootingTime >= MovingMinFootingTime)
                StartTravel(next, (flying.RawProgress - gate) * flying.SwingDistance, dt);
            return;
        }
        GaitFoot first = _nextFoot ?? (Left.MovePriority >= Right.MovePriority ? Left : Right);
        if (first.FootingTime < MovingMinFootingTime)
            first = first.Opposing;
        if (first.FootingTime >= MovingMinFootingTime)
        {
            StartTravel(first, 0f, dt);
            _nextFoot = null;
        }
    }

    // Standing, a foot steps only to mend its stance. It may go while the other is still airborne once that
    // swing is past the gate, so a walk that stops dead settles inside a second instead of queueing. -xlinka
    private void UpdateStandingStarts(float dt)
    {
        GaitFoot first = Left.MovePriority >= Right.MovePriority ? Left : Right;
        if (!TryStartStanding(first, dt))
            TryStartStanding(first.Opposing, dt);
    }

    private bool TryStartStanding(GaitFoot foot, float dt)
    {
        if (foot.IsTravelling || !foot.WantsToMove)
            return false;
        var other = foot.Opposing;
        if (other.IsTravelling && other.RawProgress < OpposingGate)
            return false;
        StartTravel(foot, 0f, dt);
        return true;
    }

    private void StartTravel(GaitFoot foot, float headStart, float dt)
    {
        float scale = MathF.Max(Parameters.Scale, 1e-4f);
        bool standing = IsStandingStill;
        float swingScale = !standing && _startupSteps > 0 ? FirstSwingScale : 1f;
        foot.SwingDistance = MathF.Max(_swingDistance * swingScale, 0.02f * scale);
        foot.SwingFloorTime = standing ? MathF.Min(MaxSwing, StillSwingTime) : MaxSwing;
        foot.SourcePosition = foot.Position;
        foot.SourceFacing = foot.Facing;
        foot.RawProgress = System.Math.Clamp(headStart / foot.SwingDistance, 0f, MaxHeadStart);
        foot.TravelProgress = foot.RawProgress;
        foot.ProgressRate = ProgressRate(foot, dt);
        foot.SwingDuration = (1f - foot.RawProgress) / MathF.Max(foot.ProgressRate, 1e-4f);
        // The foot leaves the ground with its planted pitch and keeps rotating that way, toe down off the back,
        // toe up off the front, at a rate the body speed sets. -xlinka
        foot.LiftAngle = foot.Pitch;
        foot.LiftAngularVelocity = SmoothSpeed / scale * Parameters.FootStepUpAngularVelocityMultiplier
            * -MathF.Sign(ToLocal(foot.Position - _body).z);
        foot.IsTravelling = true;
        foot.WantsToMove = false;
        foot.MovePriority = 0f;
        foot.TravellingTime = 0f;
        foot.FootingTime = 0f;

        SetTarget(foot, foot.SwingDuration, foot.SwingDuration * SmoothSpeed);
        float maximum = MathF.Min(MathF.Max(Parameters.FootStepUpHeight, Parameters.FootStepDownHeight), Parameters.MaxStepHeight) * scale;
        float liftOff = Parameters.StepUpVelocityMultiplier * Parameters.StepUpVerticalRatio
            * SmoothSpeed * foot.SwingDuration * 0.5f;
        foot.SwingHeight = MathF.Min(maximum, MathF.Max(liftOff,
            StrideMotion.Height(foot.SourcePosition, foot.TargetPosition, maximum)));
        ApplyTravel(foot, 0f);
        if (!standing)
            _startupSteps = System.Math.Max(0, _startupSteps - 1);
    }

    // Progress per second: the ground the body covers, or the pace it had when it stopped so a foot already in
    // the air keeps going, and never slower than the swing's floor time. -xlinka
    private float ProgressRate(GaitFoot foot, float dt)
    {
        if (dt <= 1e-6f)
            return 1f / foot.SwingFloorTime;
        float ground = MathF.Max(_bodyDelta, SmoothSpeed * dt);
        float step = MathF.Max(ground, foot.SwingDistance * dt / foot.SwingFloorTime);
        return step / (foot.SwingDistance * dt);
    }

    private void UpdateTravel(GaitFoot foot, float dt)
    {
        if (!foot.IsTravelling)
            return;
        foot.TravellingTime += dt;
        foot.ProgressRate = ProgressRate(foot, dt);
        foot.RawProgress += foot.ProgressRate * dt;
        foot.TravelProgress = MathF.Min(foot.RawProgress, 1f);
        if (foot.RawProgress >= 1f)
        {
            SetTarget(foot, 0f, 0f);
            Land(foot);
            return;
        }
        ApplyTravel(foot, dt);
    }

    private void ApplyTravel(GaitFoot foot, float dt)
    {
        float t = foot.TravelProgress;
        float timeToLand = (1f - t) / MathF.Max(foot.ProgressRate, 1e-4f);
        SetTarget(foot, timeToLand, timeToLand * SmoothSpeed);
        float ease = StrideMotion.Ease(t);
        foot.Position = float3.Lerp(foot.SourcePosition, foot.TargetPosition, ease);
        foot.Facing = SlerpFlat(foot.SourceFacing, foot.TargetFacing, ease);
        foot.StepLift = StrideMotion.Lift(t, 2f + Lerp(Parameters.FootStepUpPow, Parameters.FootStepDownPow, t));
        float height = Lerp(Parameters.FootStepUpHeight, Parameters.FootStepDownHeight, t)
            / MathF.Max(MathF.Max(Parameters.FootStepUpHeight, Parameters.FootStepDownHeight), 1e-5f);
        foot.ArcHeight = foot.SwingHeight * foot.StepLift * height;
        foot.LiftAngle = System.Math.Clamp(foot.LiftAngle + dt * foot.LiftAngularVelocity,
            Parameters.FootTiltMinAngle, Parameters.FootTiltMaxAngle);
        foot.Pitch = Lerp(foot.LiftAngle, LandingPitch(foot), t);
    }

    // Heel first when the foot lands ahead of the body, toe first behind it.
    private float LandingPitch(GaitFoot foot)
        => -MathF.Sign(ToLocal(foot.TargetPosition - _body).z) * Parameters.FootStepDownAngle;

    // A foot touching down is the one moment the body has that the world can hear, and it is known
    // exactly here rather than guessed from a height threshold somewhere downstream. The speed it
    // lands at rides along, because a stroll and a run are not the same sound. -xlinka
    public event System.Action<GaitFoot, bool, float>? Landed;

    private void Land(GaitFoot foot)
    {
        float impact = MathF.Max(SmoothSpeed, 0f);
        foot.Position = foot.TargetPosition;
        foot.Facing = foot.TargetFacing;
        foot.SourcePosition = foot.Position;
        foot.SourceFacing = foot.Facing;
        foot.ArcHeight = 0f;
        foot.StepLift = 0f;
        // The swing ends exactly on its landing pitch, whatever frame the crossing fell on. -xlinka
        foot.Pitch = LandingPitch(foot);
        foot.LiftAngle = foot.Pitch;
        foot.GroundLerp = 1f;
        foot.IsTravelling = false;
        foot.TravelProgress = 0f;
        foot.RawProgress = 0f;
        foot.TravellingTime = 0f;
        foot.FootingTime = 0f;

        try
        {
            Landed?.Invoke(foot, ReferenceEquals(foot, Left), impact);
        }
        catch (System.Exception ex)
        {
            Logging.Logger.Error($"BipedGait: a footstep handler threw: {ex.Message}");
        }
    }

    private float3 ToLocal(float3 world)
        => new(float3.Dot(world, _right), 0f, float3.Dot(world, _forward));

    private static float3 FlattenDir(float3 v, float3 fallback)
    {
        v.y = 0f;
        return v.LengthSquared > 1e-8f ? v.Normalized : fallback;
    }

    private static float FlatLength(float3 v)
    {
        v.y = 0f;
        return v.Length;
    }

    private static float FlatDistance(float3 a, float3 b) => FlatLength(a - b);

    private static float FlatAngleDeg(float3 a, float3 b)
    {
        a.y = 0f;
        b.y = 0f;
        if (a.LengthSquared < 1e-8f || b.LengthSquared < 1e-8f)
            return 0f;
        float dot = System.Math.Clamp(float3.Dot(a.Normalized, b.Normalized), -1f, 1f);
        return MathF.Acos(dot) * (180f / MathF.PI);
    }

    private static float3 SlerpFlat(float3 from, float3 to, float t)
    {
        from.y = 0f;
        to.y = 0f;
        if (from.LengthSquared < 1e-8f)
            return to;
        if (to.LengthSquared < 1e-8f)
            return from;
        from = from.Normalized;
        to = to.Normalized;
        float cross = float3.Dot(float3.Cross(from, to), float3.Up);
        float dot = System.Math.Clamp(float3.Dot(from, to), -1f, 1f);
        float angle = MathF.Atan2(cross, dot) * System.Math.Clamp(t, 0f, 1f);
        return floatQ.AxisAngleRad(float3.Up, angle) * from;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float SmoothingFactor(float smoothTime, float dt)
        => 1f - MathF.Exp(-dt / MathF.Max(smoothTime, 1e-4f));

    private static float SmoothDamp(float current, float target, ref float velocity, float smoothTime, float dt)
    {
        if (dt <= 1e-6f)
            return current;
        float omega = 2f / MathF.Max(smoothTime, 1e-4f);
        float x = omega * dt;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float change = current - target;
        float temp = (velocity + omega * change) * dt;
        velocity = (velocity - omega * temp) * exp;
        return target + (change + temp) * exp;
    }

}
