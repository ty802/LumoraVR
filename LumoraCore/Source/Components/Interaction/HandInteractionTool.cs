// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Input;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

public enum HoldRotationMode
{
    Upright,
    Free
}

[ComponentCategory("Interaction")]
public sealed class HandInteractionTool : Component
{
    public readonly Sync<float> HoldScrollStep = new();
    public readonly Sync<float> HoldScaleStep = new();
    public readonly Sync<float> HoldRotationSensitivity = new();
    public readonly Sync<float> AlignDoublePressWindow = new();
    public readonly Sync<HoldRotationMode> RotationMode = new();

    private Grabber? _grabber;
    private bool _prevPrimaryState;
    private bool _prevGripState;
    private bool _isHolding;
    private float _heldDistance;
    private floatQ _heldRotation = floatQ.Identity;
    private bool _desktopRotateLockActive;
    private float3 _desktopLockedHoldPosition;
    private bool _desktopInputSuppressionActive;
    private double _lastAlignPressTime = double.NegativeInfinity;

    public Grabber? Grabber => _grabber;
    public bool PrimaryHeld { get; private set; }
    public bool GripHeld { get; private set; }
    public bool IsHolding => _isHolding && _grabber?.IsHoldingObjects == true;

    public override void OnInit()
    {
        base.OnInit();
        HoldScrollStep.Value = 0.12f;
        HoldScaleStep.Value = 0.10f;
        HoldRotationSensitivity.Value = MathF.PI * 2f;
        AlignDoublePressWindow.Value = 0.5f;
        RotationMode.Value = HoldRotationMode.Upright;
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureReady();
    }

    public override void OnDestroy()
    {
        ResetInteraction(releaseHeld: true);
        base.OnDestroy();
    }

    public void EnsureReady()
    {
        if (Slot == null || Slot.IsRemoved) return;
        _grabber ??= Slot.GetComponent<Grabber>() ?? Slot.AttachComponent<Grabber>();
    }

    public void SampleInput(InteractionLaser laser)
    {
        PrimaryHeld = ReadPrimaryPressed(laser);
        GripHeld = ReadGripPressed(laser);
    }

    public void ProcessFrame(
        InteractionLaser laser,
        float delta,
        in float3 origin,
        in float3 direction,
        IInteractionTarget? currentTarget,
        ILaserPointerTarget? currentPointerTarget,
        RayTarget? currentRayTarget,
        Slot? currentHitSlot,
        in float3 currentHitPoint,
        float currentHitDistance)
    {
        EnsureReady();

        if (PrimaryHeld && !_prevPrimaryState)
        {
            if (IsHolding)
            {
                AlignHeldObjects(laser);
            }
            else if (currentTarget != null && currentPointerTarget == null)
            {
                currentRayTarget?.NotifyActivated(currentHitPoint);
                laser.NotifyActivatedByTool(currentTarget, currentHitPoint);
            }
        }
        _prevPrimaryState = PrimaryHeld;

        if (GripHeld && !_prevGripState)
        {
            TryGrabCurrentTarget(laser, currentTarget, currentHitSlot, currentHitPoint, currentHitDistance);
        }
        else if (!GripHeld && _prevGripState)
        {
            _grabber?.ReleaseAll();
            ResetInteraction(releaseHeld: false);
        }
        _prevGripState = GripHeld;

        if (IsHolding)
        {
            ApplyHeldObjectManipulation(laser, delta, origin, direction);
        }
    }

    public void ResetInteraction(bool releaseHeld)
    {
        if (releaseHeld)
        {
            _grabber?.ReleaseAll();
        }

        SetDesktopInputSuppression(false);
        _isHolding = false;
        _heldDistance = 0f;
        _heldRotation = floatQ.Identity;
        _desktopRotateLockActive = false;
        _desktopLockedHoldPosition = float3.Zero;
        PrimaryHeld = false;
        GripHeld = false;
        _prevPrimaryState = false;
        _prevGripState = false;
    }

    private void TryGrabCurrentTarget(
        InteractionLaser laser,
        IInteractionTarget? currentTarget,
        Slot? currentHitSlot,
        in float3 currentHitPoint,
        float currentHitDistance)
    {
        var grabber = _grabber ?? Slot.GetComponent<Grabber>() ?? Slot.AttachComponent<Grabber>();
        _grabber = grabber;

        var grabbable = FindBestGrabbable(currentTarget, currentHitSlot);
        if (grabbable == null) return;

        var holder = grabber.HolderSlot;
        if (holder == null) return;

        holder.GlobalPosition = currentHitPoint;
        holder.GlobalRotation = ComputeInitialHoldRotation(laser);
        holder.GlobalScale = float3.One;

        if (!grabber.TryGrab(grabbable)) return;

        _isHolding = true;
        _heldDistance = MathF.Max(0.05f, currentHitDistance);
        _heldRotation = holder.GlobalRotation;
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

    private floatQ ComputeInitialHoldRotation(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input != null && !input.IsVRActive)
        {
            var head = laser.FindHeadSlot();
            if (head != null)
            {
                return head.GlobalRotation;
            }
        }

        return Slot.GlobalRotation;
    }

    private void ApplyHeldObjectManipulation(InteractionLaser laser, float delta, in float3 origin, in float3 direction)
    {
        if (!_isHolding || _grabber == null || !_grabber.IsHoldingObjects)
        {
            ResetInteraction(releaseHeld: false);
            return;
        }

        var holder = _grabber.HolderSlot;
        if (holder == null || holder.IsRemoved)
        {
            ResetInteraction(releaseHeld: false);
            return;
        }

        var input = Engine.Current?.InputInterface;
        bool desktopRotateLocked = input != null && !input.IsVRActive && IsKeyHeld(input, Key.E);
        SetDesktopInputSuppression(desktopRotateLocked);
        if (desktopRotateLocked && !_desktopRotateLockActive)
        {
            _desktopLockedHoldPosition = holder.GlobalPosition;
            _desktopRotateLockActive = true;
        }

        if (input != null)
        {
            if (input.IsVRActive)
            {
                ApplyVrHoldInputs(laser, input, delta, holder);
            }
            else
            {
                ApplyDesktopHoldInputs(laser, input, holder, desktopRotateLocked);
            }
        }

        _heldDistance = Clamp(_heldDistance, 0.05f, MathF.Max(laser.MaxDistance.Value, 0.05f));
        if (desktopRotateLocked)
        {
            holder.GlobalPosition = _desktopLockedHoldPosition;
        }
        else
        {
            if (_desktopRotateLockActive)
            {
                _desktopRotateLockActive = false;
                _heldDistance = ComputeDistanceAlongRay(origin, direction, holder.GlobalPosition);
            }

            holder.GlobalPosition = new float3(
                origin.x + direction.x * _heldDistance,
                origin.y + direction.y * _heldDistance,
                origin.z + direction.z * _heldDistance);
        }
        holder.GlobalRotation = _heldRotation;
    }

    private void ApplyDesktopHoldInputs(InteractionLaser laser, InputInterface input, Slot holder, bool rotateLocked)
    {
        float scroll = input.Mouse?.ScrollWheelDelta.Value ?? 0f;
        bool scaleModifier = IsScaleModifierHeld(input);
        if (scroll != 0f)
        {
            if (scaleModifier && CanScaleHeldObjects())
            {
                ScaleHolder(holder, scroll * HoldScaleStep.Value);
            }
            else if (!rotateLocked)
            {
                float step = MathF.Max(0.05f, _heldDistance * HoldScrollStep.Value);
                _heldDistance -= scroll * step;
            }
        }

        if (!rotateLocked)
        {
            return;
        }

        float2 delta = input.Mouse?.DirectDelta.Value ?? float2.Zero;
        if (delta == float2.Zero)
        {
            return;
        }

        var head = laser.FindHeadSlot();
        float3 yawAxis = head?.Up ?? float3.Up;
        float3 pitchAxis = head?.Right ?? Slot.Right;
        float yaw = -delta.x * HoldRotationSensitivity.Value;
        float pitch = -delta.y * HoldRotationSensitivity.Value;
        RotateHeldObjectsInPlace(yawAxis, yaw, pitchAxis, pitch);
    }

    private void ApplyVrHoldInputs(InteractionLaser laser, InputInterface input, float delta, Slot holder)
    {
        var controller = laser.ControllerSide.Value == Chirality.Left
            ? input.LeftController
            : input.RightController;
        if (controller == null) return;

        float slide = ApplyDeadzone(controller.ThumbstickPosition.Y, 0.15f);
        float rotate = ApplyDeadzone(controller.ThumbstickPosition.X, 0.15f);

        if (controller.SecondaryButtonPressed && slide != 0f && CanScaleHeldObjects())
        {
            ScaleHolder(holder, slide * delta);
        }
        else if (slide != 0f)
        {
            _heldDistance += slide * MathF.Max(1f, _heldDistance) * 4f * delta;
        }

        if (rotate != 0f)
        {
            _heldRotation = floatQ.AxisAngle(float3.Up, rotate * MathF.PI * 2f * delta) * _heldRotation;
        }
    }

    private void AlignHeldObjects(InteractionLaser laser)
    {
        double now = Engine.Current?.TotalTime ?? 0.0;
        double interval = System.Math.Max(0.05, AlignDoublePressWindow.Value);
        if (now - _lastAlignPressTime <= interval)
        {
            RotationMode.Value = RotationMode.Value == HoldRotationMode.Upright
                ? HoldRotationMode.Free
                : HoldRotationMode.Upright;
        }
        _lastAlignPressTime = now;

        if (_grabber == null || !_grabber.IsHoldingObjects) return;

        float3 targetAxis = GetAlignmentAxis(laser);
        foreach (var grabbable in _grabber.GrabbedObjects)
        {
            var slot = (grabbable as Component)?.Slot;
            if (slot == null || slot.IsRemoved) continue;
            AlignSlotAxis(slot, targetAxis);
        }
    }

    private float3 GetAlignmentAxis(InteractionLaser laser)
    {
        if (RotationMode.Value == HoldRotationMode.Free)
        {
            return -laser.Slot.Forward;
        }

        var head = laser.FindHeadSlot();
        float3 axis = head?.Up ?? float3.Up;
        return axis.Length > 0.0001f ? axis.Normalized : float3.Up;
    }

    private static void AlignSlotAxis(Slot slot, float3 targetAxis)
    {
        if (targetAxis.Length <= 0.0001f) return;
        targetAxis = targetAxis.Normalized;

        float3 bestWorldAxis = slot.LocalDirectionToGlobal(float3.Up);
        float bestDot = float3.Dot(bestWorldAxis.Normalized, targetAxis);

        TestAxis(slot, float3.Down, targetAxis, ref bestWorldAxis, ref bestDot);
        TestAxis(slot, float3.Right, targetAxis, ref bestWorldAxis, ref bestDot);
        TestAxis(slot, float3.Left, targetAxis, ref bestWorldAxis, ref bestDot);
        TestAxis(slot, float3.Forward, targetAxis, ref bestWorldAxis, ref bestDot);
        TestAxis(slot, float3.Backward, targetAxis, ref bestWorldAxis, ref bestDot);

        floatQ delta = RotationFromTo(bestWorldAxis, targetAxis);
        slot.GlobalRotation = delta * slot.GlobalRotation;
    }

    private static void TestAxis(Slot slot, float3 localAxis, float3 targetAxis, ref float3 bestWorldAxis, ref float bestDot)
    {
        float3 worldAxis = slot.LocalDirectionToGlobal(localAxis);
        if (worldAxis.Length <= 0.0001f) return;

        float dot = float3.Dot(worldAxis.Normalized, targetAxis);
        if (dot > bestDot)
        {
            bestDot = dot;
            bestWorldAxis = worldAxis;
        }
    }

    private static floatQ RotationFromTo(float3 from, float3 to)
    {
        if (from.Length <= 0.0001f || to.Length <= 0.0001f) return floatQ.Identity;

        from = from.Normalized;
        to = to.Normalized;
        float dot = System.Math.Clamp(float3.Dot(from, to), -1f, 1f);
        if (dot > 0.9999f) return floatQ.Identity;

        if (dot < -0.9999f)
        {
            float3 fallback = MathF.Abs(float3.Dot(from, float3.Up)) > 0.9f ? float3.Right : float3.Up;
            float3 axis = float3.Cross(from, fallback).Normalized;
            return floatQ.AxisAngle(axis, MathF.PI);
        }

        float3 rotationAxis = float3.Cross(from, to);
        if (rotationAxis.Length <= 0.0001f) return floatQ.Identity;

        return floatQ.AxisAngle(rotationAxis.Normalized, MathF.Acos(dot));
    }

    private void ScaleHolder(Slot holder, float delta)
    {
        float factor = MathF.Max(0.05f, 1f + delta);
        var scale = holder.GlobalScale;
        holder.GlobalScale = new float3(scale.x * factor, scale.y * factor, scale.z * factor);
    }

    private void RotateHeldObjectsInPlace(float3 yawAxis, float yaw, float3 pitchAxis, float pitch)
    {
        if (_grabber == null) return;

        floatQ rotation = floatQ.AxisAngle(yawAxis, yaw) * floatQ.AxisAngle(pitchAxis, pitch);
        foreach (var grabbable in _grabber.GrabbedObjects)
        {
            var slot = (grabbable as Component)?.Slot;
            if (slot == null || slot.IsRemoved)
            {
                continue;
            }

            slot.GlobalRotation = rotation * slot.GlobalRotation;
        }
    }

    private bool CanScaleHeldObjects()
    {
        if (_grabber == null) return false;
        foreach (var grabbable in _grabber.GrabbedObjects)
        {
            if (grabbable == null || !grabbable.Scalable)
            {
                return false;
            }

            if (grabbable is Component component && component.IsDestroyed)
            {
                return false;
            }
        }
        return _grabber.GrabbedObjects.Count > 0;
    }

    private void SetDesktopInputSuppression(bool active)
    {
        if (_desktopInputSuppressionActive == active)
        {
            return;
        }

        _desktopInputSuppressionActive = active;
        Lumora.Core.Components.LocomotionController.SetDesktopInputSuppressed(this, active);
    }

    private static float ComputeDistanceAlongRay(float3 origin, float3 direction, float3 point)
    {
        float3 delta = point - origin;
        float projected = float3.Dot(delta, direction);
        if (projected > 0.05f)
        {
            return projected;
        }

        return MathF.Max(0.05f, delta.Length);
    }

    private static bool ReadPrimaryPressed(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null) return false;

        bool vrTrigger = laser.ControllerSide.Value == Chirality.Left
            ? input.LeftController.TriggerPressed
            : input.RightController.TriggerPressed;
        if (vrTrigger) return true;

        if (!input.IsVRActive && input.Mouse != null)
        {
            return laser.ControllerSide.Value == Chirality.Right && input.Mouse.LeftButton.Held;
        }
        return false;
    }

    private static bool ReadGripPressed(InteractionLaser laser)
    {
        var input = Engine.Current?.InputInterface;
        if (input == null) return false;

        bool vrGrip = laser.ControllerSide.Value == Chirality.Left
            ? input.LeftController.GripPressed || input.LeftController.GripValue > 0.5f
            : input.RightController.GripPressed || input.RightController.GripValue > 0.5f;
        if (vrGrip) return true;

        if (!input.IsVRActive && input.Mouse != null)
        {
            return laser.ControllerSide.Value == Chirality.Right && input.Mouse.RightButton.Held;
        }
        return false;
    }

    private static float ApplyDeadzone(float value, float deadzone)
    {
        return MathF.Abs(value) < deadzone ? 0f : value;
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static bool IsScaleModifierHeld(InputInterface input)
    {
        return IsKeyHeld(input, Key.LeftShift) ||
               IsKeyHeld(input, Key.RightShift) ||
               IsKeyHeld(input, Key.LeftControl) ||
               IsKeyHeld(input, Key.RightControl);
    }

    private static bool IsKeyHeld(InputInterface input, Key key)
    {
        return input.Keyboard?.IsKeyPressed(key) == true;
    }
}
