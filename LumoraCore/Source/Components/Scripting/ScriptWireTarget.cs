// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Scripting;

// A script graph's wires as something a Script Node Tool's beam can point at. A ray passing within reach of a
// wire's curve hits it there, worked out against the curve the wire is drawn along rather than any collider,
// and the wire under a pointer is drawn thicker and brighter with a bead where a press puts a relay. A beam with
// no node tool behind it passes through, the way it always has. Lives on the wires' local slot, holds plain
// references and syncs nothing. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptWireTarget : Component, ILaserPointerTarget
{
    // How near a ray has to pass a wire's curve to take it, in metres: well past the tube itself, which is too
    // thin to land on at arm's length.
    public const float Reach = 0.008f;

    private readonly Dictionary<int, ScriptWireHit> _hovered = new();

    public ScriptGraphSpace? Space { get; set; }

    public int InteractionTargetPriority => 1000;

    public InteractionDescription GetInteractionDescription(InteractionLaser laser) => new()
    {
        Name = "Wire",
        Cursor = LaserCursor.Default,
        ForceActivate = false,
    };

    public bool TryGetLaserPointerHit(InteractionLaser laser, in float3 rayOrigin, in float3 rayDirection, float maxDistance,
        out LaserPointerHit hit)
    {
        hit = default;
        if (!TryHit(laser, rayOrigin, rayDirection, maxDistance, out var found))
            return false;
        hit = new LaserPointerHit(found.Distance, found.RayPoint);
        return true;
    }

    public void UpdateLaserPointer(InteractionLaser laser, int pointerId, in float3 rayOrigin, in float3 rayDirection, bool isPressed)
    {
        ScriptWireHit? shown = null;
        if (TryHit(laser, rayOrigin, rayDirection, float.MaxValue, out var found))
        {
            _hovered[pointerId] = found;
            shown = found;
        }
        else
        {
            _hovered.Remove(pointerId);
        }
        Space?.View?.SetWireHover(pointerId, shown);
    }

    public void ClearLaserPointer(InteractionLaser laser, int pointerId)
    {
        _hovered.Remove(pointerId);
        Space?.View?.SetWireHover(pointerId, null);
    }

    // The wire a pointer is on as of the beam's last look, if any.
    public ScriptWireHit? HoveredBy(int pointerId) => _hovered.TryGetValue(pointerId, out var hit) ? hit : null;

    public override void OnDestroy()
    {
        _hovered.Clear();
        base.OnDestroy();
    }

    private bool TryHit(InteractionLaser laser, in float3 rayOrigin, in float3 rayDirection, float maxDistance, out ScriptWireHit hit)
    {
        hit = default;
        return Space is { IsDestroyed: false, IsShown: true } space && space.Kind.Value == GraphKind.Script && space.View is { } view
            && ScriptNodeTool.Drives(laser) && view.TryHitWire(rayOrigin, rayDirection, maxDistance, Reach, out hit);
    }
}
