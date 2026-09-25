// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

// Viewer-facing editing controls. Built in a local slot on each peer: hover and viewer-distance
// sizing are local, while the selected mode remains the gizmo's existing shared field.
[ComponentCategory("Utility/Gizmos")]
public sealed class GizmoModeToolbar : Component
{
    public readonly SyncRef<SlotGizmo> Gizmo = new();
    private Slot? _localIcon, _worldIcon;
    private GizmoCubeOutlineMesh _outlineMesh = null!;
    private bool _built;

    public void Setup(SlotGizmo gizmo)
    {
        Gizmo.Target = gizmo;
        if (!_built)
        {
            _built = true;
            Build();
        }
        UpdatePlacement();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        UpdatePlacement();
    }

    public void UpdatePlacement()
    {
        var gizmo = Gizmo.Target;
        if (gizmo == null || gizmo.IsDestroyed)
            return;
        var root = World.LocalUser?.Root;
        var head = root?.HeadSlot;
        float userScale = MathF.Max(.01f, MathF.Abs(root?.GlobalScale ?? 1f));
        float distance = head == null ? userScale : (head.GlobalPosition - gizmo.Slot.GlobalPosition).Length;
        float scale = MathF.Max(userScale, distance);
        Slot.GlobalPosition = gizmo.Slot.GlobalPosition + float3.Up * (.36f * scale);
        Slot.GlobalRotation = head?.GlobalRotation ?? floatQ.Identity;
        Slot.GlobalScale = float3.One * scale;
        if (_localIcon != null) _localIcon.ActiveSelf.Value = gizmo.IsLocalSpace.Value;
        if (_worldIcon != null) _worldIcon.ActiveSelf.Value = !gizmo.IsLocalSpace.Value;
    }

    private void Build()
    {
        var shared = WorldGizmos.For(World)!;
        _outlineMesh = Slot.AttachComponent<GizmoCubeOutlineMesh>();
        var axes = new[] { shared.GetMaterial(World, GizmoMaterialKind.AxisX)!,
            shared.GetMaterial(World, GizmoMaterialKind.AxisY)!, shared.GetMaterial(World, GizmoMaterialKind.AxisZ)! };
        var white = Material(Slot, new colorHDR(.72f, .86f, 1f, .85f));
        var outline = Material(Slot, new colorHDR(.45f, .64f, .86f, .65f));
        for (int mode = 0; mode < 4; mode++)
        {
            var button = Slot.AddSlot(new[] { "Move", "Rotate", "Scale", "Axes" }[mode]);
            button.LocalPosition.Value = new float3((mode - 1.5f) * .081f + (mode == 3 ? .012f : 0f), 0, 0);
            var face = Material(button, new colorHDR(.24f, .28f, .4f, .13f));
            Box(button, "Glass", float3.Zero, float3.One * GizmoModeButton.Size, face);
            WireBox(button, GizmoModeButton.Size, outline);
            var click = button.AttachComponent<GizmoModeButton>();
            click.Gizmo.Target = Gizmo.Target!;
            click.Mode.Value = mode;
            click.Face.Target = face;
            var icon = button.AddSlot("Icon");
            icon.LocalRotation.Value = floatQ.Euler(.35f, -.5f, 0f);
            if (mode == 0) AxisIcon(icon, axes, scale: false);
            if (mode == 1) Rings(icon, axes);
            if (mode == 2) AxisIcon(icon, axes, scale: true);
            if (mode == 3)
            {
                _localIcon = icon.AddSlot("Local");
                WireBox(_localIcon, .032f, white);
                _worldIcon = icon.AddSlot("World");
                Rings(_worldIcon, new[] { white, white, white });
            }
        }
    }

    private static OverlayUnlitMaterial Material(Slot slot, colorHDR tint)
    {
        var material = slot.AttachComponent<OverlayUnlitMaterial>();
        material.BlendMode.Value = BlendMode.Alpha;
        material.Culling.Value = Culling.Back;
        material.FrontTintColor.Value = tint;
        material.BehindTintColor.Value = new colorHDR(tint.r, tint.g, tint.b, tint.a * .6f);
        return material;
    }

    private static void Render(Slot slot, ProceduralMesh mesh, OverlayUnlitMaterial material)
    {
        var renderer = slot.AttachComponent<MeshRenderer>();
        renderer.Mesh.Target = mesh;
        renderer.Material.Target = material;
        renderer.ShadowCastMode.Value = ShadowCastMode.Off;
    }

    private static void Box(Slot parent, string name, float3 position, float3 size, OverlayUnlitMaterial material)
    {
        var slot = parent.AddSlot(name);
        slot.LocalPosition.Value = position;
        var mesh = slot.AttachComponent<BoxMesh>();
        mesh.Size.Value = size;
        Render(slot, mesh, material);
    }

    private void WireBox(Slot parent, float size, OverlayUnlitMaterial material)
    {
        var slot = parent.AddSlot("Outline");
        slot.LocalScale.Value = float3.One * size;
        Render(slot, _outlineMesh, material);
    }

    private static floatQ AxisRotation(int axis) => axis switch
    {
        0 => floatQ.AxisAngleRad(float3.Forward, -MathF.PI / 2),
        2 => floatQ.AxisAngleRad(float3.Right, MathF.PI / 2),
        _ => floatQ.Identity,
    };

    private static void AxisIcon(Slot parent, OverlayUnlitMaterial[] axes, bool scale)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            var slot = parent.AddSlot("Axis");
            slot.LocalPosition.Value = new float3(-.011f, -.011f, -.011f);
            slot.LocalRotation.Value = AxisRotation(axis);
            if (scale)
            {
                Box(slot, "Stem", new float3(0, .013f, 0), new float3(.0015f, .026f, .0015f), axes[axis]);
                Box(slot, "Tip", new float3(0, .027f, 0), float3.One * .008f, axes[axis]);
            }
            else
            {
                var arrow = slot.AttachComponent<ArrowMesh>();
                arrow.ShaftRadius.Value = .0015f;
                arrow.ShaftLength.Value = .023f;
                arrow.TipRadius.Value = .004f;
                arrow.TipLength.Value = .009f;
                arrow.Segments.Value = 8;
                Render(slot, arrow, axes[axis]);
            }
        }
    }

    private static void Rings(Slot parent, OverlayUnlitMaterial[] axes)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            var slot = parent.AddSlot("Ring");
            slot.LocalRotation.Value = AxisRotation(axis);
            var ring = slot.AttachComponent<TorusMesh>();
            ring.MajorRadius.Value = .022f;
            ring.MinorRadius.Value = .0012f;
            ring.MajorSegments.Value = 24;
            ring.MinorSegments.Value = 6;
            Render(slot, ring, axes[axis]);
        }
    }
}
