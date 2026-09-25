// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// A mode button consumes clicks rather than starting a transform drag. Its pick box shares the
// visual's transform, so distance scaling and rotated selections cannot separate the two.
[ComponentCategory("Utility/Gizmos")]
public sealed class GizmoModeButton : Component, ILaserPointerTarget, ILaserPreferredTarget
{
    public readonly SyncRef<SlotGizmo> Gizmo = new();
    public readonly Sync<int> Mode = new(); // 0 move, 1 rotate, 2 scale, 3 local/global
    public readonly SyncRef<OverlayUnlitMaterial> Face = new();
    private readonly Dictionary<int, (bool Pressed, bool Armed)> _pointers = new();
    private bool _wasSelected, _wasHovered;
    private bool _painted;

    public const float Size = .065f;
    public int InteractionTargetPriority => 11000;
    public bool IsSelected => Gizmo.Target is { IsDestroyed: false } gizmo
        && (Mode.Value == 3 ? gizmo.IsLocalSpace.Value : gizmo.ActiveMode.Value == Mode.Value);
    public bool IsHovered => _pointers.Count != 0;
    private bool CanUse => Enabled.Value && Slot.IsActive && Gizmo.Target is { IsDestroyed: false } gizmo
        && gizmo.Active.Value && !gizmo.IsFolded.Value && !gizmo.IsInteracting
        && gizmo.TargetSlot is { IsDestroyed: false };

    public InteractionDescription GetInteractionDescription(InteractionLaser laser) => new()
    {
        Name = Mode.Value switch { 0 => "Move", 1 => "Rotate", 2 => "Scale",
            _ => IsSelected ? "Local axes — switch to world" : "World axes — switch to local" },
        Cursor = LaserCursor.Default,
    };

    public bool PreferLaserHit(InteractionLaser laser) => CanUse;

    public bool TryGetLaserPointerHit(InteractionLaser laser, in float3 origin, in float3 direction,
        float maxDistance, out LaserPointerHit hit)
    {
        hit = default;
        if (!CanUse)
            return false;
        var localOrigin = Slot.GlobalPointToLocal(origin);
        var localDirection = Slot.GlobalDirectionToLocal(direction);
        var half = float3.One * (Size * .5f);
        if (localDirection.LengthSquared < 1e-12f
            || !TransformHandle.RayBox(localOrigin, localDirection, -half, half, out float along))
            return false;
        var point = Slot.LocalPointToGlobal(localOrigin + localDirection * along);
        float distance = (point - origin).Length;
        if (distance > maxDistance)
            return false;
        hit = new LaserPointerHit(distance, point);
        return true;
    }

    public void UpdateLaserPointer(InteractionLaser laser, int pointerId, in float3 origin,
        in float3 direction, bool isPressed)
    {
        if (!TryGetLaserPointerHit(laser, origin, direction, float.MaxValue, out _))
        {
            ClearLaserPointer(laser, pointerId);
            return;
        }
        // Entering while a trigger is already held must not click a row of buttons.
        bool existed = _pointers.TryGetValue(pointerId, out var previous);
        bool armed = previous.Armed || (existed && !previous.Pressed && isPressed);
        if (previous.Pressed && !isPressed && armed)
        {
            Activate();
            armed = false;
        }
        _pointers[pointerId] = (isPressed, armed);
        RefreshHighlight();
    }

    public void ClearLaserPointer(InteractionLaser laser, int pointerId)
    {
        _pointers.Remove(pointerId);
        RefreshHighlight();
    }

    private void Activate()
    {
        if (!CanUse)
            return;
        var gizmo = Gizmo.Target!;
        if (Mode.Value == 3)
            gizmo.ToggleSpace();
        else if (Mode.Value is >= 0 and <= 2)
            // Re-clicking a selected mode is harmless; the legacy double-tap reset is not a button action.
            gizmo.ActiveMode.Value = Mode.Value;
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        RefreshHighlight();
    }

    private void RefreshHighlight()
    {
        bool selected = IsSelected, hovered = IsHovered;
        if (_painted && selected == _wasSelected && hovered == _wasHovered)
            return;
        var material = Face.Target;
        if (material == null || material.IsDestroyed)
            return;
        _painted = true;
        _wasSelected = selected;
        _wasHovered = hovered;
        var tint = hovered ? new colorHDR(.55f, .85f, 1f, .42f)
            : selected ? new colorHDR(.22f, .64f, 1f, .3f) : new colorHDR(.24f, .28f, .4f, .13f);
        material.FrontTintColor.Value = tint;
        material.BehindTintColor.Value = new colorHDR(tint.r, tint.g, tint.b, tint.a * .6f);
    }
}
