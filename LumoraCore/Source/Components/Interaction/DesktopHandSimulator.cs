// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// DESKTOP HAND SIMULATOR
//
// Nothing tracks a hand on desktop, so this stands in for the tracker. Every frame it produces a hand
// pose in the user root's local space, blended between a REST pose hanging beside the hips and an
// INTERACTION pose that reaches the hand toward whatever the laser is on, clamped to arm's length and
// kept out of the head and torso. The blend rides InteractionLerp, a critically damped ease toward the
// 0/1 the owning hand asks for, so equipping reads as the arm coming up and putting the tool away as
// it dropping back.
//
// The rotation is composed with the TOOL's frame in hand space (where the tool sits and which way it
// points, relative to the hand bone), so the thing that ends up aimed at the target is the tool, not
// the palm. HandTool feeds it and writes the result onto the controller body node; AvatarIK weights
// the arm by the same lerp so the arm follows the hand. Runs on the owner only; a peer sees the pose on
// the node's transform stream. -xlinka
[ComponentCategory("Users")]
[SingleInstancePerSlot]
public sealed class DesktopHandSimulator : Component
{
    // How far the hand may reach from its shoulder-side origin, in user-root metres.
    public const float MaxHandDistance = 0.67f;
    // The hand point stops this short of the target along the ray, so a tool never sits inside what it aims at.
    public const float TargetOffset = 0.02f;
    // Ease rate for the interaction blend when the source gives none (1/s; see SmoothDamp for the shape).
    public const float DefaultTransitionSpeed = 16f;
    // The face point sits this far ahead of the eyes. A hand point closer than FaceRadius to it is nudged
    // out and down by half the shortfall before the body keep-out below runs.
    public const float FaceForward = 0.1f;
    public const float FaceRadius = 0.15f;
    // Body keep-out radii: the hand is pushed out of a sphere on the head, one on the face point, and five
    // along the neck-to-hips line, so it can never end up inside the torso.
    public const float HeadRadius = 0.2f;
    public const float TorsoRadius = 0.25f;
    // Where the neck sits when no rig says: below and behind the eyes, in head space.
    public static readonly float3 NeckOffset = new float3(0f, -0.12f, 0.1f);

    public readonly Sync<Chirality> Side = new();

    // The body as the simulator sees it, in user-root local space. Forward and Right are horizontal unit
    // vectors of the body's facing; Right is the body's own right, this component mirrors it per side.
    public struct BodyFrame
    {
        public float3 HeadPosition;
        public floatQ HeadRotation;
        public float3 NeckPosition;
        public float3 HipsPosition;
        public float3 Forward;
        public float3 Right;
    }

    // What the owning hand wants this frame. TargetPoint is in user-root local space. ToolOrigin and
    // ToolFrame describe the tool in HAND space: the point the tool aims from, and a rotation whose -Z is
    // the tool's pointing direction and whose +Y is the back of the hand. OverridePosition pins the hand
    // there and only rotates it so the tool still lines up on the target.
    public struct HandInput
    {
        public bool HasTarget;
        public float3 TargetPoint;
        public float3? OverridePosition;
        public float3 ToolOrigin;
        public floatQ ToolFrame;
        public float InteractionLerp;
        public float? TransitionSpeed;
    }

    private float _interactionLerp;
    private float _interactionVelocity;
    private float3 _interactionPosition;
    private floatQ _interactionRotation = floatQ.Identity;
    private bool _hasInteraction;

    public float3 HandPosition { get; private set; }
    public floatQ HandRotation { get; private set; } = floatQ.Identity;
    public float InteractionLerp => _interactionLerp;
    public float3 RestPosition { get; private set; }
    public floatQ RestRotation { get; private set; } = floatQ.Identity;
    // The point the tool aims from at full interaction, before the aim ease, for anything that wants to
    // know where the reach landed rather than where the hand currently is.
    public float3 ReachPoint { get; private set; }
    public float3 ReachOrigin { get; private set; }
    public bool HasInteractionPose => _hasInteraction;

    // 1/s ease on the interaction pose itself. The laser's end point jumps the moment the hover leaves an
    // object edge (half a metre away, then the sky), and the reach direction swings with it, so taking the
    // raw pose snaps the hand round on every sweep. Zero takes the raw pose. -xlinka
    public float AimSmoothing { get; set; } = 10f;

    public void Step(in BodyFrame frame, in HandInput input, float delta)
    {
        delta = MathF.Max(delta, 0f);
        float sign = Side.Value == Chirality.Left ? -1f : 1f;
        float3 forward = SafeUnit(frame.Forward, float3.Backward);
        float3 right = SafeUnit(frame.Right, float3.Right) * sign;
        floatQ toolInverse = input.ToolFrame.Normalized.Inverse;

        float3 rest = frame.HipsPosition * new float3(1f, 0.9f, 1f) + right * 0.25f + forward * 0.05f;
        floatQ restRotation = (PointingRotation(float3.Down, right) * toolInverse).Normalized;
        float3 reachOrigin = float3.Lerp(frame.NeckPosition + right * 0.2f, rest, 0.5f) - forward * 0.2f;

        float target = System.Math.Clamp(input.InteractionLerp, 0f, 1f);
        float speed = input.TransitionSpeed ?? DefaultTransitionSpeed;
        _interactionLerp = SmoothDamp(_interactionLerp, target, ref _interactionVelocity, speed, delta);
        if (_interactionLerp < 0.001f)
        {
            _interactionLerp = 0f;
            if (target <= 0f)
                _interactionVelocity = 0f;
        }
        else if (_interactionLerp > 0.999f)
        {
            _interactionLerp = 1f;
            if (target >= 1f)
                _interactionVelocity = 0f;
        }

        float3 reachPoint = reachOrigin;
        if (input.HasTarget)
        {
            float3 position;
            floatQ rotation;
            if (input.OverridePosition.HasValue)
            {
                position = input.OverridePosition.Value;
                float3 toolPoint = position + restRotation * input.ToolOrigin;
                float3 toolDirection = restRotation * (input.ToolFrame * float3.Backward);
                rotation = (RotationAtTarget(position, toolPoint, toolDirection, input.TargetPoint) * restRotation).Normalized;
                reachPoint = toolPoint;
            }
            else
            {
                float3 reach = input.TargetPoint - reachOrigin;
                float reachLength = reach.Length;
                if (reachLength > MaxHandDistance)
                {
                    reach *= MaxHandDistance / reachLength;
                    reachLength = MaxHandDistance;
                }
                reachPoint = reachLength > 1e-5f
                    ? reachOrigin + reach - reach * (TargetOffset / reachLength)
                    : reachOrigin;

                float3 face = frame.HeadPosition + frame.HeadRotation * float3.Backward * FaceForward;
                float faceDistance = float3.Distance(reachPoint, face);
                if (faceDistance < FaceRadius)
                {
                    reachPoint += (right + float3.Down).Normalized * ((FaceRadius - faceDistance) * 0.5f);
                }
                reachPoint = PushOutOfBody(reachPoint, in frame, right);

                float3 aim = SafeUnit(input.TargetPoint - reachPoint, forward);
                rotation = (PointingRotation(aim, right) * toolInverse).Normalized;
                position = reachPoint - rotation * input.ToolOrigin;
            }

            // Snap while the blend is at zero: nothing shows, and easing from a stale pose would start the
            // next reach pointing wherever the last one ended.
            bool snap = !_hasInteraction || _interactionLerp <= 0f || AimSmoothing <= 0f;
            float t = snap ? 1f : 1f - MathF.Exp(-AimSmoothing * delta);
            _interactionPosition = float3.Lerp(_interactionPosition, position, t);
            _interactionRotation = floatQ.Slerp(_interactionRotation, rotation, t).Normalized;
            _hasInteraction = true;
        }
        else if (!_hasInteraction)
        {
            _interactionPosition = rest;
            _interactionRotation = restRotation;
        }

        RestPosition = rest;
        RestRotation = restRotation;
        ReachOrigin = reachOrigin;
        ReachPoint = reachPoint;
        HandPosition = PushOutOfBody(float3.Lerp(rest, _interactionPosition, _interactionLerp), in frame, right);
        HandRotation = floatQ.Slerp(restRotation, _interactionRotation, _interactionLerp).Normalized;
    }

    public void ResetInteraction()
    {
        _interactionLerp = 0f;
        _interactionVelocity = 0f;
        _hasInteraction = false;
    }

    // The body frame off a user root, in its local space. Neck and hips come from the worn rig's bones
    // when the caller has them, otherwise from the head: the neck a little below and behind the eyes,
    // the hips six tenths of the way up from the floor and a hand's width back. -xlinka
    public static bool TryResolveBodyFrame(UserRoot? root, Slot? neckBone, Slot? hipsBone, out BodyFrame frame)
    {
        frame = default;
        var rootSlot = root?.Slot;
        var head = root?.HeadSlot;
        if (root == null || root.IsDestroyed || rootSlot == null || rootSlot.IsDestroyed
            || head == null || head.IsDestroyed)
        {
            return false;
        }

        frame.HeadPosition = rootSlot.GlobalPointToLocal(head.GlobalPosition);
        frame.HeadRotation = rootSlot.GlobalRotationToLocal(head.GlobalRotation).Normalized;

        float3 forward = frame.HeadRotation * float3.Backward;
        forward.y = 0f;
        frame.Forward = SafeUnit(forward, float3.Backward);
        frame.Right = SafeUnit(float3.Cross(frame.Forward, float3.Up), float3.Right);

        frame.NeckPosition = neckBone != null && !neckBone.IsDestroyed
            ? rootSlot.GlobalPointToLocal(neckBone.GlobalPosition)
            : frame.HeadPosition + frame.HeadRotation * NeckOffset;

        if (hipsBone != null && !hipsBone.IsDestroyed)
        {
            frame.HipsPosition = rootSlot.GlobalPointToLocal(hipsBone.GlobalPosition);
        }
        else
        {
            float3 feet = new float3(frame.HeadPosition.x, 0f, frame.HeadPosition.z);
            frame.HipsPosition = float3.Lerp(feet, frame.HeadPosition, 0.6f) - frame.Forward * 0.1f;
        }
        return true;
    }

    // Critically damped ease of `current` toward `target`. `speed` is a rate in 1/s; the spring's natural
    // frequency is half of it, so 16 settles in about half a second and 50 in about a fifth. Never
    // overshoots, and the velocity carries across frames so a target that flips mid-move turns round
    // smoothly instead of kinking. -xlinka
    public static float SmoothDamp(float current, float target, ref float velocity, float speed, float delta)
    {
        if (delta <= 0f)
            return current;

        float omega = MathF.Max(speed, 0.0001f) * 0.5f;
        float x = omega * delta;
        float decay = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        float change = current - target;
        float temp = (velocity + omega * change) * delta;
        velocity = (velocity - omega * temp) * decay;
        float result = target + (change + temp) * decay;
        if ((target - current > 0f) == (result > target))
        {
            result = target;
            velocity = 0f;
        }
        return result;
    }

    // The rotation whose pointing axis (-Z, the way a slot points) runs along `pointing` and whose +Y lies
    // as close to `up` as the pointing allows. Falls through to the world up, then right, when the two
    // are parallel.
    public static floatQ PointingRotation(float3 pointing, float3 up)
    {
        pointing = SafeUnit(pointing, float3.Backward);
        float3 side = up - pointing * float3.Dot(up, pointing);
        if (side.LengthSquared < 1e-4f)
        {
            side = float3.Up - pointing * float3.Dot(float3.Up, pointing);
            if (side.LengthSquared < 1e-4f)
                side = float3.Right - pointing * float3.Dot(float3.Right, pointing);
        }
        // LookRotation hands back the inverse of the facing it names (its basis goes in as rows), and its
        // forward is +Z, so invert and feed it the opposite of the pointing.
        return floatQ.LookRotation(-pointing, side.Normalized).Inverse.Normalized;
    }

    // Push `point` out of the head, the face and five spheres down the neck-to-hips line.
    public static float3 PushOutOfBody(float3 point, in BodyFrame frame, float3 fallbackDirection)
    {
        point = PushOutOfSphere(point, frame.HeadPosition, HeadRadius, fallbackDirection);
        point = PushOutOfSphere(point, frame.HeadPosition + frame.HeadRotation * float3.Backward * FaceForward, FaceRadius, fallbackDirection);
        for (int i = 0; i <= 4; i++)
        {
            point = PushOutOfSphere(point, float3.Lerp(frame.NeckPosition, frame.HipsPosition, i * 0.25f), TorsoRadius, fallbackDirection);
        }
        return point;
    }

    private static float3 PushOutOfSphere(float3 point, float3 center, float radius, float3 fallbackDirection)
    {
        float3 offset = point - center;
        float distance = offset.Length;
        if (distance >= radius)
            return point;
        float3 direction = distance > 1e-6f ? offset / distance : SafeUnit(fallbackDirection, float3.Right);
        return center + direction * radius;
    }

    // The rotation about `pivot` that swings the line through `point` along `direction` until it passes
    // through `target`. Found by intersecting that line, moved to its closest approach to the pivot, with
    // the sphere through the target: the hit is the point on the line that lands on the target once the
    // whole thing turns. Identity when the line cannot reach.
    public static floatQ RotationAtTarget(float3 pivot, float3 point, float3 direction, float3 target)
    {
        direction = SafeUnit(direction, float3.Backward);
        float3 tangent = point + direction * float3.Dot(pivot - point, direction);
        float radius = float3.Distance(pivot, target);
        if (!TryRaySphere(pivot, radius, tangent, direction, out float3 hit))
            return floatQ.Identity;
        return RotationFromTo(hit - pivot, target - pivot);
    }

    private static bool TryRaySphere(float3 center, float radius, float3 origin, float3 direction, out float3 hit)
    {
        hit = default;
        float3 toOrigin = origin - center;
        float b = float3.Dot(toOrigin, direction);
        float c = float3.Dot(toOrigin, toOrigin) - radius * radius;
        if (c > 0f && b > 0f)
            return false;
        float discriminant = b * b - c;
        if (discriminant < 0f)
            return false;
        float root = MathF.Sqrt(discriminant);
        float near = -b - root;
        float far = -b + root;
        if (near < 0f && far < 0f)
            return false;
        float t = near > 0f && far > 0f ? MathF.Min(near, far) : (far < 0f ? near : far);
        hit = origin + direction * t;
        return true;
    }

    public static floatQ RotationFromTo(float3 from, float3 to)
    {
        if (from.LengthSquared <= 1e-10f || to.LengthSquared <= 1e-10f)
            return floatQ.Identity;
        from = from.Normalized;
        to = to.Normalized;
        float dot = System.Math.Clamp(float3.Dot(from, to), -1f, 1f);
        if (dot > 0.99999f)
            return floatQ.Identity;
        if (dot < -0.99999f)
        {
            float3 pick = MathF.Abs(float3.Dot(from, float3.Up)) > 0.9f ? float3.Right : float3.Up;
            float3 axis = float3.Cross(from, pick);
            return axis.LengthSquared <= 1e-10f ? floatQ.Identity : floatQ.AxisAngle(axis.Normalized, MathF.PI);
        }
        float3 rotationAxis = float3.Cross(from, to);
        return rotationAxis.LengthSquared <= 1e-12f
            ? floatQ.Identity
            : floatQ.AxisAngle(rotationAxis.Normalized, MathF.Acos(dot));
    }

    private static float3 SafeUnit(float3 v, float3 fallback)
        => v.LengthSquared > 1e-10f ? v.Normalized : fallback;
}
