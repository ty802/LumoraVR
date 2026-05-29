// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class DashSurfacePortal : Component, ILaserPointerTarget
{
    private UserspaceDashboard? _dash;

    public int InteractionTargetPriority => 1000;

    public InteractionDescription GetInteractionDescription(InteractionLaser laser)
    {
        return new InteractionDescription
        {
            Name = "Dashboard",
            Cursor = LaserCursor.Default,
        };
    }

    public bool TryGetLaserPointerHit(InteractionLaser laser, in float3 rayOrigin, in float3 rayDirection, float maxDistance, out LaserPointerHit hit)
    {
        hit = default;
        if (!TryProject(rayOrigin, rayDirection, out var local, out var world))
            return false;

        var size = SurfaceSize();
        if (MathF.Abs(local.x) > size.x * 0.5f || MathF.Abs(local.y) > size.y * 0.5f)
            return false;

        float distance = (world - rayOrigin).Length;
        if (distance > maxDistance)
            return false;

        hit = new LaserPointerHit(distance, world);
        return true;
    }

    public void UpdateLaserPointer(InteractionLaser laser, int pointerId, in float3 rayOrigin, in float3 rayDirection, bool isPressed)
    {
        var dash = Dash();
        if (dash == null)
            return;

        if (!TryProject(rayOrigin, rayDirection, out var local, out _))
        {
            dash.ClearVrPointer(laser, pointerId);
            return;
        }

        var size = SurfaceSize();
        float u = local.x / size.x + 0.5f;
        float v = 0.5f - local.y / size.y;
        dash.UpdateVrPointer(laser, pointerId, new float2(u, v), isPressed);
    }

    public void ClearLaserPointer(InteractionLaser laser, int pointerId)
    {
        Dash()?.ClearVrPointer(laser, pointerId);
    }

    private UserspaceDashboard? Dash() => _dash ??= Slot.GetComponentInParents<UserspaceDashboard>();

    private float2 SurfaceSize()
    {
        var mesh = Slot.GetComponent<CurvedPlaneMesh>();
        return mesh != null ? mesh.Size.Value : new float2(1f, 0.5625f);
    }

    private bool TryProject(in float3 origin, in float3 direction, out float2 local, out float3 world)
    {
        local = default;
        world = default;

        var localOrigin = Slot.GlobalPointToLocal(origin);
        var localDir = Slot.GlobalDirectionToLocal(direction.Normalized);
        if (MathF.Abs(localDir.z) < 1e-6f)
            return false;

        float t = -localOrigin.z / localDir.z;
        if (t < 0f)
            return false;

        var point = localOrigin + localDir * t;
        local = new float2(point.x, point.y);
        world = Slot.LocalPointToGlobal(point);
        return true;
    }
}
