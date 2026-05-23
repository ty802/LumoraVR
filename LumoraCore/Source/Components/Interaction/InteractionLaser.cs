// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Input;
using Lumora.Core.Math;
using Lumora.Core.Physics;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Interaction;

// per-hand interaction laser. casts a ray, finds the best IInteractionTarget
// by sphere/collider intersection + parent-chain walk, fires Activated on trigger.
// also bridges legacy RayTarget hover/activated events. - xlinka
//
// TODO - xlinka: UI pointer routing, richer tool stack, release receivers.
[ComponentCategory("XR/Interaction")]
public sealed class InteractionLaser : Component
{
    public readonly Sync<float> MaxDistance = new();
    public readonly Sync<float> BeamRadius = new();
    public readonly Sync<float> BeamStartOffset = new();
    public readonly Sync<Chirality> ControllerSide = new();
    public readonly Sync<colorHDR> IdleColor = new();
    public readonly Sync<colorHDR> HoverColor = new();
    public readonly Sync<float> HoverSwitchTolerance = new();
    public readonly Sync<float> DefaultHoverRadius = new();
    public readonly Sync<float> StickyHitDistance = new();

    private readonly List<TargetHit> _hitBuffer = new(64);
    private Slot? _beamSlot;
    private CylinderMesh? _beamMesh;
    private UnlitMaterial? _beamMaterial;
    private Grabber? _grabber;
    private IInteractionTarget? _currentTarget;
    private RayTarget? _currentRayTarget;
    private ILaserPointerTarget? _currentPointerTarget;
    private Slot? _currentHitSlot;
    private float3 _currentHitPoint;
    private float _currentHitDistance;
    private bool _prevTriggerState;
    private bool _prevGripState;
    private float3 _smoothedHitPoint;
    private bool _hasSmoothedHitPoint;

    public Grabber? Grabber => _grabber;
    public IInteractionTarget? CurrentTarget => _currentTarget;
    public float3 CurrentHitPoint => _currentHitPoint;
    public float CurrentHitDistance => _currentHitDistance;
    public bool IsActive => _currentTarget != null;

    public event Action<IInteractionTarget?>? TargetChanged;
    public event Action<IInteractionTarget, float3>? Activated;

    private readonly struct TargetHit
    {
        public readonly IInteractionTarget Target;
        public readonly Slot Slot;
        public readonly float Distance;
        public readonly float3 HitPoint;

        public TargetHit(IInteractionTarget target, Slot slot, float distance, float3 hitPoint)
        {
            Target = target;
            Slot = slot;
            Distance = distance;
            HitPoint = hitPoint;
        }
    }

    public override void OnInit()
    {
        base.OnInit();
        MaxDistance.Value = 8f;
        BeamRadius.Value = 0.003f;
        BeamStartOffset.Value = 0.020f;
        ControllerSide.Value = Chirality.Right;
        IdleColor.Value = new colorHDR(0.30f, 0.85f, 1.00f, 0.55f);
        HoverColor.Value = new colorHDR(1.00f, 1.00f, 1.00f, 0.85f);
        HoverSwitchTolerance.Value = 0.05f;
        DefaultHoverRadius.Value = 0.05f;
        StickyHitDistance.Value = 0.08f;
    }

    public override void OnStart()
    {
        base.OnStart();
        _grabber = Slot.GetComponent<Grabber>() ?? Slot.AttachComponent<Grabber>();
        BuildBeamVisual();
        LumoraLogger.Log($"InteractionLaser: Started on '{Slot.SlotName.Value}' side={ControllerSide.Value}");
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        CastAndUpdate(delta);
    }

    public override void OnDestroy()
    {
        ClearCurrentTarget();
        base.OnDestroy();
    }

    private void BuildBeamVisual()
    {
        _beamSlot = Slot.AddSlot("InteractionLaserVisual");

        _beamMesh = _beamSlot.AttachComponent<CylinderMesh>();
        _beamMesh.Radius.Value = BeamRadius.Value;
        _beamMesh.Height.Value = MaxDistance.Value;
        _beamMesh.Segments.Value = 6;

        var matSlot = _beamSlot.AddSlot("InteractionLaserMaterial");
        _beamMaterial = matSlot.AttachComponent<UnlitMaterial>();
        _beamMaterial.TintColor.Value = IdleColor.Value;
        _beamMaterial.BlendMode.Value = BlendMode.Transparent;
        _beamMaterial.RenderQueue.Value = 85;

        var renderer = _beamSlot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = _beamMesh;
        renderer.Material.Target = _beamMaterial;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
        renderer.SortingOrder.Value = 85;
    }

    private void CastAndUpdate(float delta)
    {
        if (_beamSlot == null || World?.RootSlot == null) return;

        var inputCheck = Engine.Current?.InputInterface;
        bool showWorldBeam = true;
        if (inputCheck != null && !inputCheck.IsVRActive)
        {
            // desktop reticle is the cursor; hide the world-space beam to avoid divergence. - xlinka
            showWorldBeam = false;
        }
        if (_beamSlot.ActiveSelf.Value != showWorldBeam)
        {
            _beamSlot.ActiveSelf.Value = showWorldBeam;
        }

        float maxDist = MathF.Max(MaxDistance.Value, 0.01f);
        ResolveRayPose(out float3 origin, out float3 direction);

        float blockingDistance = maxDist;
        if (TryFindNearestColliderHitDistance(origin, direction, maxDist, out float colliderHitDistance))
        {
            blockingDistance = colliderHitDistance;
        }

        _hitBuffer.Clear();
        CollectInteractionHits(World.RootSlot, origin, direction, maxDist, blockingDistance);

        IInteractionTarget? hoveredTarget = null;
        Slot? hoveredSlot = null;
        float hoveredDistance = maxDist;
        float3 hoveredHitPoint = float3.Zero;

        if (_hitBuffer.Count > 0)
        {
            int nearestIndex = 0;
            float nearestDistance = _hitBuffer[0].Distance;
            for (int i = 1; i < _hitBuffer.Count; i++)
            {
                if (_hitBuffer[i].Distance < nearestDistance)
                {
                    nearestDistance = _hitBuffer[i].Distance;
                    nearestIndex = i;
                }
            }

            int selectedIndex = nearestIndex;
            float keepThreshold = nearestDistance + MathF.Max(HoverSwitchTolerance.Value, 0f);
            if (_currentTarget != null)
            {
                for (int i = 0; i < _hitBuffer.Count; i++)
                {
                    var hit = _hitBuffer[i];
                    if (ReferenceEquals(hit.Target, _currentTarget) && hit.Distance <= keepThreshold)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            var selected = _hitBuffer[selectedIndex];
            // walk parent chain on the selected slot for a higher-priority target. - xlinka
            var promoted = PromoteToHighestPriority(selected.Target, selected.Slot);
            hoveredTarget = promoted;
            hoveredSlot = selected.Slot;
            hoveredDistance = selected.Distance;
            hoveredHitPoint = selected.HitPoint;
        }
        else if (TryKeepStickyTarget(origin, direction, maxDist, blockingDistance,
            out var stickyTarget, out var stickySlot, out float stickyDistance, out float3 stickyPoint))
        {
            hoveredTarget = stickyTarget;
            hoveredSlot = stickySlot;
            hoveredDistance = stickyDistance;
            hoveredHitPoint = stickyPoint;
        }

        if (!ReferenceEquals(hoveredTarget, _currentTarget))
        {
            ClearCurrentTarget();
            _currentTarget = hoveredTarget;
            _currentRayTarget = hoveredTarget as RayTarget;
            _currentRayTarget?.NotifyHoverEntered();
            TargetChanged?.Invoke(_currentTarget);
        }
        if (hoveredTarget != null)
        {
            hoveredHitPoint = ApplyHitPointSmoothing(hoveredSlot, hoveredHitPoint, delta);
        }
        else
        {
            _hasSmoothedHitPoint = false;
        }
        _currentHitSlot = hoveredSlot;
        _currentHitPoint = hoveredHitPoint;
        _currentHitDistance = hoveredDistance;

        bool triggerNow = ReadTriggerPressed();
        UpdatePointerTarget(hoveredTarget as ILaserPointerTarget, origin, direction, triggerNow);

        if (triggerNow && !_prevTriggerState && _currentTarget != null && _currentPointerTarget == null)
        {
            _currentRayTarget?.NotifyActivated(_currentHitPoint);
            Activated?.Invoke(_currentTarget, _currentHitPoint);
        }
        _prevTriggerState = triggerNow;

        bool gripNow = ReadGripPressed();
        if (gripNow && !_prevGripState)
        {
            TryGrabCurrentTarget();
        }
        else if (!gripNow && _prevGripState)
        {
            _grabber?.ReleaseAll();
        }
        _prevGripState = gripNow;

        float beamLength = hoveredTarget != null ? hoveredDistance : blockingDistance;
        if (showWorldBeam)
        {
            PositionBeam(origin, direction, beamLength);
        }

        if (_beamMaterial != null)
        {
            colorHDR wantedColor = hoveredTarget != null ? HoverColor.Value : IdleColor.Value;
            colorHDR haveColor = _beamMaterial.TintColor.Value;
            if (haveColor.r != wantedColor.r || haveColor.g != wantedColor.g ||
                haveColor.b != wantedColor.b || haveColor.a != wantedColor.a)
            {
                _beamMaterial.TintColor.Value = wantedColor;
            }
        }

        if (_beamMesh != null)
        {
            SetIfChanged(_beamMesh.Radius, BeamRadius.Value);
            SetIfChanged(_beamMesh.Height, beamLength);
        }
    }

    private static void SetIfChanged(Sync<float> field, float value)
    {
        if (MathF.Abs(field.Value - value) > 0.0001f)
        {
            field.Value = value;
        }
    }

    private void ClearCurrentTarget()
    {
        if (_currentRayTarget != null)
        {
            _currentRayTarget.NotifyHoverExited();
            _currentRayTarget = null;
        }
        _currentTarget = null;
        _currentHitSlot = null;
        ClearPointerTarget();
        _hasSmoothedHitPoint = false;
    }

    private void UpdatePointerTarget(ILaserPointerTarget? pointerTarget, float3 origin, float3 direction, bool isPressed)
    {
        int pointerId = GetPointerId();
        if (!ReferenceEquals(pointerTarget, _currentPointerTarget))
        {
            _currentPointerTarget?.ClearLaserPointer(this, pointerId);
            _currentPointerTarget = pointerTarget;
        }

        _currentPointerTarget?.UpdateLaserPointer(this, pointerId, origin, direction, isPressed);
    }

    private void ClearPointerTarget()
    {
        if (_currentPointerTarget == null) return;

        _currentPointerTarget.ClearLaserPointer(this, GetPointerId());
        _currentPointerTarget = null;
    }

    private int GetPointerId()
    {
        return ControllerSide.Value == Chirality.Left ? 1 : 2;
    }

    // walk parent chain from the hit slot. take the highest-priority IInteractionTarget
    // found. stop at SearchBlock on a non-origin ancestor. - xlinka
    private static IInteractionTarget PromoteToHighestPriority(IInteractionTarget seed, Slot hitSlot)
    {
        IInteractionTarget best = seed;
        int bestPriority = seed.InteractionTargetPriority;
        var current = hitSlot;
        while (current != null)
        {
            if (!ReferenceEquals(current, hitSlot) && current.GetComponent<SearchBlock>() != null)
            {
                break;
            }
            foreach (var t in current.GetComponentsImplementing<IInteractionTarget>())
            {
                if (t.InteractionTargetPriority > bestPriority)
                {
                    best = t;
                    bestPriority = t.InteractionTargetPriority;
                }
            }
            current = current.Parent;
        }
        return best;
    }

    private bool TryKeepStickyTarget(float3 origin, float3 direction, float maxDistance, float blockingDistance,
        out IInteractionTarget? target, out Slot? slot, out float distance, out float3 hitPoint)
    {
        target = null;
        slot = null;
        distance = 0f;
        hitPoint = float3.Zero;

        if (_currentTarget == null || _currentHitSlot == null) return false;
        if (_currentTarget is Component comp && (!comp.Enabled.Value || comp.IsDestroyed)) return false;
        if (!_currentHitSlot.IsActive) return false;

        float3 oldDelta = new(
            _currentHitPoint.x - origin.x,
            _currentHitPoint.y - origin.y,
            _currentHitPoint.z - origin.z);
        float projected = float3.Dot(oldDelta, direction);
        if (projected <= 0f || projected > maxDistance || projected > blockingDistance) return false;

        float3 candidate = new(
            origin.x + direction.x * projected,
            origin.y + direction.y * projected,
            origin.z + direction.z * projected);
        float3 miss = new(
            candidate.x - _currentHitPoint.x,
            candidate.y - _currentHitPoint.y,
            candidate.z - _currentHitPoint.z);
        if (miss.Length > MathF.Max(StickyHitDistance.Value, 0f)) return false;
        if (!TryApplyLaserModifiers(_currentHitSlot, direction, candidate, out candidate)) return false;

        target = _currentTarget;
        slot = _currentHitSlot;
        distance = projected;
        hitPoint = candidate;
        return true;
    }

    private void TryGrabCurrentTarget()
    {
        var grabber = _grabber ?? Slot.GetComponent<Grabber>() ?? Slot.AttachComponent<Grabber>();
        _grabber = grabber;

        var grabbable = FindBestGrabbable(_currentTarget, _currentHitSlot);
        if (grabbable == null) return;

        grabber.TryGrab(grabbable);
    }

    private static IGrabbable? FindBestGrabbable(IInteractionTarget? target, Slot? hitSlot)
    {
        IGrabbable? best = target as IGrabbable;
        int bestPriority = best?.GrabPriority ?? int.MinValue;

        var current = hitSlot;
        while (current != null)
        {
            if (!ReferenceEquals(current, hitSlot) && current.GetComponent<SearchBlock>() != null)
            {
                break;
            }

            foreach (var grabbable in current.GetComponentsImplementing<IGrabbable>())
            {
                if (grabbable.GrabPriority > bestPriority)
                {
                    best = grabbable;
                    bestPriority = grabbable.GrabPriority;
                }
            }

            current = current.Parent;
        }

        return best;
    }

    private bool TryApplyLaserModifiers(Slot hitSlot, float3 direction, float3 point, out float3 filteredPoint)
    {
        filteredPoint = point;

        var current = hitSlot;
        while (current != null)
        {
            if (!ReferenceEquals(current, hitSlot) && current.GetComponent<SearchBlock>() != null)
            {
                break;
            }

            foreach (var modifier in current.GetComponentsImplementing<ILaserInteractionModifier>())
            {
                if (modifier is Component comp && (!comp.Enabled.Value || comp.IsDestroyed)) continue;
                if (!modifier.IsInteractionHit(filteredPoint, direction)) return false;
                filteredPoint = modifier.FilterPoint(this, filteredPoint);
            }

            current = current.Parent;
        }

        return true;
    }

    private float3 ApplyHitPointSmoothing(Slot? hitSlot, float3 newPoint, float delta)
    {
        if (hitSlot == null)
        {
            _smoothedHitPoint = newPoint;
            _hasSmoothedHitPoint = true;
            return newPoint;
        }

        if (!_hasSmoothedHitPoint)
        {
            _smoothedHitPoint = newPoint;
            _hasSmoothedHitPoint = true;
            return newPoint;
        }

        float? smoothSpeed = null;
        var current = hitSlot;
        while (current != null)
        {
            if (!ReferenceEquals(current, hitSlot) && current.GetComponent<SearchBlock>() != null)
            {
                break;
            }

            foreach (var modifier in current.GetComponentsImplementing<ILaserInteractionModifier>())
            {
                if (modifier is Component comp && (!comp.Enabled.Value || comp.IsDestroyed)) continue;
                float? speed = modifier.GetSmoothSpeed(this, newPoint, _smoothedHitPoint);
                if (!speed.HasValue) continue;
                smoothSpeed = smoothSpeed.HasValue
                    ? MathF.Min(smoothSpeed.Value, speed.Value)
                    : speed.Value;
            }

            current = current.Parent;
        }

        if (!smoothSpeed.HasValue || smoothSpeed.Value <= 0f)
        {
            _smoothedHitPoint = newPoint;
            return newPoint;
        }

        float t = 1f - MathF.Exp(-smoothSpeed.Value * MathF.Max(delta, 0f));
        _smoothedHitPoint = float3.Lerp(_smoothedHitPoint, newPoint, System.Math.Clamp(t, 0f, 1f));
        return _smoothedHitPoint;
    }

    private void CollectInteractionHits(Slot slot, float3 origin, float3 direction, float maxDist, float blockingDistance)
    {
        if (!slot.IsActive) return;

        foreach (var target in slot.GetComponentsImplementing<IInteractionTarget>())
        {
            if (target is Component comp && (!comp.Enabled.Value || comp.IsDestroyed)) continue;

            if (target is ILaserPointerTarget pointerTarget)
            {
                if (!pointerTarget.TryGetLaserPointerHit(this, origin, direction, maxDist, out var pointerHit)) continue;
                if (pointerHit.Distance > blockingDistance) continue;
                if (IsSlotOnThisHierarchy(slot)) continue;

                _hitBuffer.Add(new TargetHit(target, slot, pointerHit.Distance, pointerHit.Point));
                continue;
            }

            float radius = GetHoverRadius(target);
            if (radius <= 0f) continue;

            float3 center = slot.GlobalPosition;
            if (!RaySphereIntersect(origin, direction, center, radius, out float distance)) continue;
            if (distance > maxDist || distance > blockingDistance) continue;
            if (IsSlotOnThisHierarchy(slot)) continue;

            var hitPoint = new float3(
                origin.x + direction.x * distance,
                origin.y + direction.y * distance,
                origin.z + direction.z * distance);
            if (!TryApplyLaserModifiers(slot, direction, hitPoint, out hitPoint)) continue;

            _hitBuffer.Add(new TargetHit(target, slot, distance, hitPoint));
        }

        foreach (var child in slot.Children)
        {
            CollectInteractionHits(child, origin, direction, maxDist, blockingDistance);
        }
        foreach (var child in slot.LocalChildren)
        {
            CollectInteractionHits(child, origin, direction, maxDist, blockingDistance);
        }
    }

    private float GetHoverRadius(IInteractionTarget target)
    {
        if (target is RayTarget rt) return MathF.Max(rt.HoverRadius.Value, 0.001f);
        return MathF.Max(DefaultHoverRadius.Value, 0.001f);
    }

    private void ResolveRayPose(out float3 origin, out float3 direction)
    {
        float startOffset = MathF.Max(BeamStartOffset.Value, 0f);
        var input = Engine.Current?.InputInterface;

        // desktop: aim from the head/camera so the ray passes through the reticle. - xlinka
        if (input != null && !input.IsVRActive)
        {
            var headSlot = FindHeadSlot();
            floatQ headRot = headSlot != null ? headSlot.GlobalRotation : floatQ.Identity;
            float3 headPos = headSlot != null ? headSlot.GlobalPosition : Slot.GlobalPosition;

            direction = headRot * new float3(0f, 0f, -1f);
            float dLen = direction.Length;
            if (dLen > 0.0001f) direction /= dLen;
            else direction = float3.Backward;

            origin = new float3(
                headPos.x + direction.x * startOffset,
                headPos.y + direction.y * startOffset,
                headPos.z + direction.z * startOffset);
            return;
        }

        // VR: controller slot forward, with optional fingertip refinement. - xlinka
        direction = -Slot.Forward;
        float dirLen = direction.Length;
        if (dirLen < 0.0001f) direction = float3.Backward;
        else direction /= dirLen;

        origin = new float3(
            Slot.GlobalPosition.x + direction.x * startOffset,
            Slot.GlobalPosition.y + direction.y * startOffset,
            Slot.GlobalPosition.z + direction.z * startOffset);

        if (input == null) return;

        BodyNode tipNode = ControllerSide.Value == Chirality.Left
            ? BodyNode.LeftIndexFinger_Tip
            : BodyNode.RightIndexFinger_Tip;
        BodyNode distalNode = ControllerSide.Value == Chirality.Left
            ? BodyNode.LeftIndexFinger_Distal
            : BodyNode.RightIndexFinger_Distal;

        var tip = input.GetBodyNode(tipNode);
        if (tip == null || !tip.IsTracking) return;

        float3 resolvedOrigin = tip.Position;
        var distal = input.GetBodyNode(distalNode);
        if (distal != null && distal.IsTracking)
        {
            float3 fingertipDirection = new float3(
                tip.Position.x - distal.Position.x,
                tip.Position.y - distal.Position.y,
                tip.Position.z - distal.Position.z);
            float ftLen = fingertipDirection.Length;
            if (ftLen > 0.0001f) direction = fingertipDirection / ftLen;
        }

        origin = new float3(
            resolvedOrigin.x + direction.x * startOffset,
            resolvedOrigin.y + direction.y * startOffset,
            resolvedOrigin.z + direction.z * startOffset);
    }

    private bool TryFindNearestColliderHitDistance(float3 origin, float3 direction, float maxDistance, out float hitDistance)
    {
        hitDistance = maxDistance;
        bool hasHit = false;

        foreach (var collider in World.RootSlot.GetComponentsInChildren<Collider>())
        {
            if (!IsColliderRaycastCandidate(collider)) continue;
            if (!TryIntersectCollider(collider, origin, direction, maxDistance, out float candidateDistance)) continue;
            if (candidateDistance < hitDistance)
            {
                hitDistance = candidateDistance;
                hasHit = true;
            }
        }
        return hasHit;
    }

    private bool IsColliderRaycastCandidate(Collider collider)
    {
        if (collider == null || collider.Slot == null) return false;
        if (!collider.Enabled.Value || !collider.Slot.IsActive) return false;
        if (collider.IgnoreRaycasts.Value || collider.Type.Value == ColliderType.NoCollision) return false;
        if (IsSlotOnThisHierarchy(collider.Slot)) return false;
        return true;
    }

    private static bool TryIntersectCollider(Collider collider, float3 origin, float3 direction, float maxDistance, out float hitDistance)
    {
        hitDistance = 0f;
        if (!TryGetColliderLocalBounds(collider, out float3 localMin, out float3 localMax)) return false;

        float3 localOrigin = collider.Slot.GlobalPointToLocal(origin);
        float3 localDirection = collider.Slot.GlobalDirectionToLocal(direction);
        if (localDirection.Length < 0.0001f) return false;
        if (!RayAabbIntersect(localOrigin, localDirection, localMin, localMax, out float localHitT)) return false;

        float3 localHitPoint = new float3(
            localOrigin.x + localDirection.x * localHitT,
            localOrigin.y + localDirection.y * localHitT,
            localOrigin.z + localDirection.z * localHitT);
        float3 worldHitPoint = collider.Slot.LocalPointToGlobal(localHitPoint);

        float3 worldDelta = new float3(
            worldHitPoint.x - origin.x,
            worldHitPoint.y - origin.y,
            worldHitPoint.z - origin.z);
        float forwardDistance = float3.Dot(worldDelta, direction);
        if (forwardDistance <= 0f) return false;

        float distance = worldDelta.Length;
        if (distance <= 0.0001f || distance > maxDistance) return false;

        hitDistance = distance;
        return true;
    }

    private static bool TryGetColliderLocalBounds(Collider collider, out float3 min, out float3 max)
    {
        switch (collider)
        {
            case BoxCollider box:
            {
                float3 half = AbsVector(box.Size.Value) * 0.5f;
                float3 offset = box.Offset.Value;
                min = new float3(offset.x - half.x, offset.y - half.y, offset.z - half.z);
                max = new float3(offset.x + half.x, offset.y + half.y, offset.z + half.z);
                return true;
            }
            case SphereCollider sphere:
            {
                float radius = MathF.Max(MathF.Abs(sphere.Radius.Value), 0.0005f);
                float3 offset = sphere.Offset.Value;
                min = new float3(offset.x - radius, offset.y - radius, offset.z - radius);
                max = new float3(offset.x + radius, offset.y + radius, offset.z + radius);
                return true;
            }
            case CapsuleCollider capsule:
            {
                float radius = MathF.Max(MathF.Abs(capsule.Radius.Value), 0.0005f);
                float halfHeight = MathF.Max(MathF.Abs(capsule.Height.Value) * 0.5f, radius);
                float3 offset = capsule.Offset.Value;
                min = new float3(offset.x - radius, offset.y - halfHeight, offset.z - radius);
                max = new float3(offset.x + radius, offset.y + halfHeight, offset.z + radius);
                return true;
            }
            case CylinderCollider cylinder:
            {
                float radius = MathF.Max(MathF.Abs(cylinder.Radius.Value), 0.0005f);
                float halfHeight = MathF.Max(MathF.Abs(cylinder.Height.Value) * 0.5f, 0.0005f);
                float3 offset = cylinder.Offset.Value;
                min = new float3(offset.x - radius, offset.y - halfHeight, offset.z - radius);
                max = new float3(offset.x + radius, offset.y + halfHeight, offset.z + radius);
                return true;
            }
            default:
                min = float3.Zero;
                max = float3.Zero;
                return false;
        }
    }

    private static bool RayAabbIntersect(float3 origin, float3 direction, float3 min, float3 max, out float t)
    {
        const float epsilon = 0.000001f;
        float tMin = 0f;
        float tMax = float.MaxValue;

        if (!RayAabbAxis(origin.x, direction.x, min.x, max.x, ref tMin, ref tMax, epsilon) ||
            !RayAabbAxis(origin.y, direction.y, min.y, max.y, ref tMin, ref tMax, epsilon) ||
            !RayAabbAxis(origin.z, direction.z, min.z, max.z, ref tMin, ref tMax, epsilon))
        {
            t = 0f;
            return false;
        }

        if (tMax < 0f)
        {
            t = 0f;
            return false;
        }

        t = tMin >= 0f ? tMin : tMax;
        return t >= 0f;
    }

    private static bool RayAabbAxis(float origin, float direction, float min, float max, ref float tMin, ref float tMax, float epsilon)
    {
        if (MathF.Abs(direction) <= epsilon) return origin >= min && origin <= max;

        float inverse = 1f / direction;
        float t0 = (min - origin) * inverse;
        float t1 = (max - origin) * inverse;
        if (t0 > t1) (t0, t1) = (t1, t0);
        if (t0 > tMin) tMin = t0;
        if (t1 < tMax) tMax = t1;
        return tMin <= tMax;
    }

    private static float3 AbsVector(float3 v) => new(MathF.Abs(v.x), MathF.Abs(v.y), MathF.Abs(v.z));

    private void PositionBeam(float3 origin, float3 direction, float length)
    {
        if (_beamSlot == null) return;
        _beamSlot.GlobalPosition = new float3(
            origin.x + direction.x * length * 0.5f,
            origin.y + direction.y * length * 0.5f,
            origin.z + direction.z * length * 0.5f);
        _beamSlot.GlobalRotation = AlignYToDirection(direction);
    }

    private Slot? FindHeadSlot()
    {
        var current = Slot.Parent;
        while (current != null)
        {
            var userRoot = current.GetComponent<UserRoot>();
            if (userRoot != null)
            {
                var hs = userRoot.HeadSlot;
                if (hs != null) return hs;
                break;
            }
            current = current.Parent;
        }

        current = Slot.Parent;
        while (current != null)
        {
            if (current.Name.Value == "Body Nodes")
            {
                foreach (var child in current.Children)
                {
                    if (child.Name.Value == "Head") return child;
                }
            }
            current = current.Parent;
        }
        return null;
    }

    private bool ReadTriggerPressed()
    {
        var input = Engine.Current?.InputInterface;
        if (input == null) return false;

        bool vrTrigger = ControllerSide.Value == Chirality.Left
            ? input.LeftController.TriggerPressed
            : input.RightController.TriggerPressed;
        if (vrTrigger) return true;

        if (!input.IsVRActive && input.Mouse != null)
        {
            return ControllerSide.Value == Chirality.Left
                ? input.Mouse.RightButton.Held
                : input.Mouse.LeftButton.Held;
        }
        return false;
    }

    private bool ReadGripPressed()
    {
        var input = Engine.Current?.InputInterface;
        if (input == null) return false;

        bool vrGrip = ControllerSide.Value == Chirality.Left
            ? input.LeftController.GripPressed || input.LeftController.GripValue > 0.5f
            : input.RightController.GripPressed || input.RightController.GripValue > 0.5f;
        if (vrGrip) return true;

        if (!input.IsVRActive && input.Mouse != null)
        {
            return input.Mouse.MiddleButton.Held;
        }
        return false;
    }

    private static bool RaySphereIntersect(float3 origin, float3 direction, float3 center, float radius, out float t)
    {
        float3 oc = new(origin.x - center.x, origin.y - center.y, origin.z - center.z);
        float b = float3.Dot(oc, direction);
        float c = float3.Dot(oc, oc) - radius * radius;
        float disc = b * b - c;

        if (disc < 0f) { t = 0f; return false; }

        float sqrtDisc = MathF.Sqrt(disc);
        float nearT = -b - sqrtDisc;
        float farT = -b + sqrtDisc;

        if (nearT > 0f) { t = nearT; return true; }
        if (farT > 0f) { t = farT; return true; }

        t = 0f;
        return false;
    }

    private bool IsSlotOnThisHierarchy(Slot candidate)
    {
        var current = candidate;
        while (current != null)
        {
            if (ReferenceEquals(current, Slot)) return true;
            current = current.Parent;
        }
        return false;
    }

    private static floatQ AlignYToDirection(float3 direction)
    {
        float len = direction.Length;
        if (len < 0.0001f) return floatQ.Identity;

        float3 dir = new(direction.x / len, direction.y / len, direction.z / len);
        float3 axis = float3.Cross(float3.Up, dir);
        float axisLen = axis.Length;

        if (axisLen < 0.001f)
        {
            return float3.Dot(float3.Up, dir) > 0f
                ? floatQ.Identity
                : floatQ.AxisAngle(float3.Forward, MathF.PI);
        }

        float3 normAxis = new(axis.x / axisLen, axis.y / axisLen, axis.z / axisLen);
        float angle = MathF.Acos(System.Math.Clamp(float3.Dot(float3.Up, dir), -1f, 1f));
        return floatQ.AxisAngle(normAxis, angle);
    }
}
