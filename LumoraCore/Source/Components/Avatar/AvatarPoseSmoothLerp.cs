// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Avatar;

// IAvatarPoseFilter that lerps tracked position/rotation toward incoming
// targets. Negative smooth speed = pass-through. Auto-installed by
// AvatarObjectSlot for Hips/LeftFoot/RightFoot since those nodes jitter
// visibly without smoothing. Head and hands stay pass-through so
// controller-direct input doesn't gain latency. - xlinka
[ComponentCategory("Users/Common Avatar System/Filters")]
public class AvatarPoseSmoothLerp : Component, IAvatarPoseFilter
{
    // Lerp rate per second toward the target. <= 0 means pass through.
    public readonly Sync<float> PositionSmoothSpeed = new();
    public readonly Sync<float> RotationSmoothSpeed = new();

    private bool _hasPrevious;
    private float3 _previousPosition;
    private floatQ _previousRotation;

    public override void OnInit()
    {
        base.OnInit();
        PositionSmoothSpeed.Value = -1f;
        RotationSmoothSpeed.Value = 20f;
    }

    public void ProcessPose(AvatarObjectSlot slot, Slot space, ref float3 position, ref floatQ rotation, ref bool isTracking)
    {
        if (!isTracking)
        {
            _hasPrevious = false;
            return;
        }

        if (!_hasPrevious)
        {
            _previousPosition = position;
            _previousRotation = rotation;
            _hasPrevious = true;
            return;
        }

        float dt = World?.UpdateManager?.DeltaTime ?? 0.016f;

        float posSpeed = PositionSmoothSpeed.Value;
        if (posSpeed > 0f)
        {
            float t = posSpeed * dt;
            if (t > 1f) t = 1f;
            position = float3.Lerp(_previousPosition, position, t);
        }

        float rotSpeed = RotationSmoothSpeed.Value;
        if (rotSpeed > 0f)
        {
            float t = rotSpeed * dt;
            if (t > 1f) t = 1f;
            rotation = floatQ.Slerp(_previousRotation, rotation, t);
        }

        _previousPosition = position;
        _previousRotation = rotation;
    }
}
