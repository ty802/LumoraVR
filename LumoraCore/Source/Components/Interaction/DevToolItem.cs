// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components.Gizmos;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

[ComponentCategory("Interaction/Tools")]
public sealed class DevToolItem : ToolItem
{
    public enum SelectionMode
    {
        Single,
        Multi
    }

    public readonly Sync<SelectionMode> Selection = new();
    public readonly SyncRef<Slot> SelectedSlot = new();
    public readonly SyncRef<Slot> CurrentGizmoSlot = new();

    private Gizmo? _activeGizmo;
    private Slot? _visualSlot;
    private UnlitMaterial? _visualMaterial;

    public override bool UsesLaser => true;

    public override bool UsesSecondary => true;

    protected override float3 DefaultLocalTip => float3.Backward * 0.075f;

    public override void OnInit()
    {
        base.OnInit();
        Selection.Value = SelectionMode.Single;
        EquipName.Value = "Dev Tool";
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureVisual();
    }

    public override bool OnPrimaryPress()
    {
        var laser = ActiveTool?.Laser;
        var hitSlot = laser?.CurrentHitSlot;
        if (laser == null || hitSlot == null)
        {
            return false;
        }

        var gizmo = hitSlot.GetComponentInParents<Gizmo>();
        if (gizmo == null)
        {
            return false;
        }

        if (!gizmo.BeginInteraction(laser.CurrentHitPoint))
        {
            return false;
        }

        _activeGizmo = gizmo;
        return true;
    }

    public override bool OnPrimaryHold()
    {
        var laser = ActiveTool?.Laser;
        if (_activeGizmo == null || laser == null)
        {
            return false;
        }

        return _activeGizmo.UpdateInteraction(laser.CurrentHitPoint);
    }

    public override bool OnPrimaryRelease()
    {
        if (_activeGizmo == null)
        {
            return false;
        }

        bool ended = _activeGizmo.EndInteraction();
        _activeGizmo = null;
        return ended;
    }

    public override bool OnSecondaryPress()
    {
        var laser = ActiveTool?.Laser;
        var target = ResolveSelectableSlot(laser?.CurrentHitSlot);
        if (target == null)
        {
            return false;
        }

        if (Selection.Value == SelectionMode.Single && SelectedSlot.Target != null && !ReferenceEquals(SelectedSlot.Target, target))
        {
            GizmoHelper.DestroyGizmo(SelectedSlot.Target);
        }

        var gizmo = GizmoHelper.ToggleGizmo(target);
        SelectedSlot.Target = gizmo == null ? null : target;
        CurrentGizmoSlot.Target = (gizmo as Component)?.Slot;
        return true;
    }

    public override void OnDequipped()
    {
        _activeGizmo?.EndInteraction();
        _activeGizmo = null;
    }

    private Slot? ResolveSelectableSlot(Slot? hitSlot)
    {
        if (hitSlot == null || hitSlot.IsRootSlot)
        {
            return null;
        }

        var slotGizmo = hitSlot.GetComponentInParents<SlotGizmo>();
        if (slotGizmo?.TargetSlot != null)
        {
            return slotGizmo.TargetSlot;
        }

        if (IsInActiveToolHierarchy(hitSlot))
        {
            return null;
        }

        return hitSlot;
    }

    private bool IsInActiveToolHierarchy(Slot slot)
    {
        var toolSlot = ActiveTool?.Slot;
        var current = slot;
        while (current != null)
        {
            if (ReferenceEquals(current, toolSlot) || ReferenceEquals(current, Slot))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private void EnsureVisual()
    {
        if (_visualSlot != null && !_visualSlot.IsRemoved)
        {
            return;
        }

        _visualSlot = Slot.FindChild("Visual", recursive: false) ?? Slot.AddSlot("Visual");
        _visualSlot.LocalPosition.Value = float3.Backward * 0.05f;
        _visualSlot.LocalScale.Value = float3.One;

        var sphere = _visualSlot.GetComponent<SphereMesh>() ?? _visualSlot.AttachComponent<SphereMesh>();
        sphere.Radius.Value = 0.015f;
        sphere.Segments.Value = 16;
        sphere.Rings.Value = 8;

        _visualMaterial = _visualSlot.GetComponent<UnlitMaterial>() ?? _visualSlot.AttachComponent<UnlitMaterial>();
        _visualMaterial.TintColor.Value = new colorHDR(0.2f, 1f, 0.45f, 1f);
        _visualMaterial.BlendMode.Value = BlendMode.Alpha;
        _visualMaterial.Culling.Value = Culling.None;

        var renderer = _visualSlot.GetComponent<MeshRenderer>() ?? _visualSlot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = sphere;
        renderer.Material.Target = _visualMaterial;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
    }
}
