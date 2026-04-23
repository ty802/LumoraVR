// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Godot.UI;

#nullable enable

/// <summary>
/// Shared world-space placement helpers for in-world UI panels.
/// </summary>
public static class PanelPlacement
{
    private const float MinDirectionLengthSquared = 0.0001f;

    public static void PlaceInFrontOfCamera(
        Slot slot,
        Camera3D camera,
        float distance,
        float horizontalOffset = 0f,
        float verticalOffset = 0f,
        bool yawOnlyRotation = true)
    {
        var forward = NormalizeOr(-camera.GlobalBasis.Z, Vector3.Forward);
        var right = NormalizeOr(camera.GlobalBasis.X, Vector3.Right);
        var up = NormalizeOr(camera.GlobalBasis.Y, Vector3.Up);
        var position = camera.GlobalPosition
            + forward * distance
            + right * horizontalOffset
            + up * verticalOffset;

        slot.GlobalPosition = ToFloat3(position);
        SetPanelForward(slot, forward, yawOnlyRotation);
    }

    public static void PlaceInFrontOfHead(
        Slot slot,
        Vector3 headPosition,
        Quaternion headRotation,
        float distance,
        float verticalOffset = 0f,
        bool yawOnlyRotation = true)
    {
        var forward = NormalizeOr(headRotation * Vector3.Forward, Vector3.Forward);
        var position = headPosition + forward * distance + Vector3.Up * verticalOffset;

        slot.GlobalPosition = ToFloat3(position);
        SetPanelForward(slot, forward, yawOnlyRotation);
    }

    public static void PlaceBesidePanel(
        Slot slot,
        Slot anchor,
        float rightOffset,
        float upOffset = 0f,
        float forwardOffset = 0f)
    {
        slot.GlobalPosition = anchor.GlobalPosition
            + anchor.Right * rightOffset
            + anchor.Up * upOffset
            + anchor.Forward * forwardOffset;
        slot.GlobalRotation = anchor.GlobalRotation;
    }

    public static void PlaceFacingPoint(
        Slot slot,
        Vector3 position,
        Vector3 target,
        bool yawOnlyRotation = true)
    {
        slot.GlobalPosition = ToFloat3(position);
        FaceTowards(slot, target, yawOnlyRotation);
    }

    public static void FaceTowards(Slot slot, Vector3 target, bool yawOnlyRotation = true)
    {
        var position = ToVector3(slot.GlobalPosition);
        var direction = target - position;
        SetPanelForward(slot, -direction, yawOnlyRotation);
    }

    private static void SetPanelForward(Slot slot, Vector3 forward, bool yawOnlyRotation)
    {
        if (yawOnlyRotation)
        {
            forward.Y = 0f;
        }

        if (forward.LengthSquared() < MinDirectionLengthSquared)
        {
            return;
        }

        // In-world UI quads expose their visible/front side on local backward (-Z).
        slot.GlobalRotation = floatQ.LookRotation(ToFloat3(forward.Normalized()), float3.Up);
    }

    private static Vector3 NormalizeOr(Vector3 value, Vector3 fallback)
    {
        return value.LengthSquared() >= MinDirectionLengthSquared
            ? value.Normalized()
            : fallback;
    }

    private static float3 ToFloat3(Vector3 value)
    {
        return new float3(value.X, value.Y, value.Z);
    }

    private static Vector3 ToVector3(float3 value)
    {
        return new Vector3(value.x, value.y, value.z);
    }
}
