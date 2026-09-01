// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

public enum ToolShape
{
    Box,
    Sphere,
    Cylinder,
    Cone,
    Capsule
}

// Places a primitive where you point, standing on the surface you pointed at.
//
// The shape's +Y goes up the surface normal and the body is pushed out by half its own height, so a
// cylinder placed on a wall sticks out of the wall rather than being buried halfway into it. Flat
// ground and a sloped roof therefore behave the same way, which is the whole reason the normal is
// worth a second raycast. -xlinka
[ComponentCategory("Interaction/Tools")]
public sealed class ShapeTool : EquippableTool
{
    private static readonly float[] SizePresets = { 0.1f, 0.25f, 0.5f, 1f };

    public readonly Sync<ToolShape> Shape = new();
    public readonly Sync<float> ShapeSize = new();
    public readonly Sync<colorHDR> ShapeTint = new();

    protected override colorHDR VisualTint => new(0.35f, 0.72f, 1f, 1f);

    public override void OnInit()
    {
        base.OnInit();
        EquipName.Value = "Shape Tool";
        Shape.Value = ToolShape.Box;
        ShapeSize.Value = 0.25f;
        ShapeTint.Value = new colorHDR(0.72f, 0.76f, 0.82f, 1f);
    }

    protected override void BuildVisualExtras(Slot visual)
    {
        EnsureBead(visual, "Bead", float3.Up * 0.05f, 0.011f, VisualTint);
    }

    public override bool OnPrimaryPress()
    {
        if (EditingBlocked || SpawnBlocked || !TryGetAim(out var aim))
            return false;

        var parent = World?.RootSlot;
        if (parent == null)
            return false;

        var shape = Shape.Value;
        float size = MathF.Max(0.01f, ShapeSize.Value);
        var slot = TryAddSlot(parent, shape.ToString());
        if (slot == null)
            return false;

        bool built = Guarded(() =>
        {
            slot.GlobalRotation = AlignUpTo(aim.Normal);
            // Half the body's height along the normal: the shape stands ON the surface instead of
            // straddling it. Every primitive here is centred on its own origin, so it is the same
            // number for all of them.
            slot.GlobalPosition = aim.Point + aim.Normal * (size * 0.5f);

            var material = slot.AttachComponent<PBS_Metallic>();
            material.AlbedoColor.Value = ShapeTint.Value;
            material.Metallic.Value = 0.1f;
            material.Smoothness.Value = 0.35f;

            var renderer = slot.AttachComponent<MeshRenderer>();
            renderer.Material.Target = material;
            renderer.Mesh.Target = BuildMesh(slot, shape, size);
            BuildCollider(slot, shape, size);

            slot.AttachComponent<Grabbable>();
        });

        if (!built)
        {
            Guarded(() => slot.Destroy());
            return false;
        }

        RecordUndo(SlotExistenceUndoBatch.Created(World, new[] { slot }, UndoLocale.SpawnShape));
        return true;
    }

    // Secondary steps the shape, same as the submenu entries - reaching for the radial menu to turn a
    // box into a sphere is more ceremony than the change deserves.
    public override bool OnSecondaryPress()
    {
        if (EditingBlocked)
            return false;
        var values = Enum.GetValues<ToolShape>();
        Shape.Value = values[((int)Shape.Value + 1) % values.Length];
        return true;
    }

    public override void PopulateToolActions(ContextMenuPage page, ContextMenuContext context)
    {
        foreach (var shape in Enum.GetValues<ToolShape>())
        {
            var pick = shape;
            page.AddItem(new ContextMenuItem
            {
                Label = shape.ToString(),
                FillColor = Shape.Value == shape ? ActiveFill : ItemFill,
                OnPressed = _ => { if (!IsDestroyed) Shape.Value = pick; },
            });
        }

        foreach (float preset in SizePresets)
        {
            float pick = preset;
            page.AddItem(new ContextMenuItem
            {
                Label = $"Size {preset:0.##} m",
                FillColor = MathF.Abs(ShapeSize.Value - preset) < 1e-4f ? ActiveFill : ItemFill,
                OnPressed = _ => { if (!IsDestroyed) ShapeSize.Value = pick; },
            });
        }
    }

    private static Component BuildMesh(Slot slot, ToolShape shape, float size)
    {
        switch (shape)
        {
            case ToolShape.Sphere:
            {
                var mesh = slot.AttachComponent<SphereMesh>();
                mesh.Radius.Value = size * 0.5f;
                mesh.Segments.Value = 24;
                mesh.Rings.Value = 14;
                return mesh;
            }
            case ToolShape.Cylinder:
            {
                var mesh = slot.AttachComponent<CylinderMesh>();
                mesh.Radius.Value = size * 0.5f;
                mesh.Height.Value = size;
                mesh.Segments.Value = 24;
                return mesh;
            }
            case ToolShape.Cone:
            {
                var mesh = slot.AttachComponent<ConeMesh>();
                mesh.RadiusBase.Value = size * 0.5f;
                mesh.RadiusTop.Value = 0f;
                mesh.Height.Value = size;
                mesh.Segments.Value = 24;
                return mesh;
            }
            case ToolShape.Capsule:
            {
                var mesh = slot.AttachComponent<CapsuleMesh>();
                mesh.Radius.Value = size * 0.25f;
                mesh.Height.Value = size;
                mesh.Segments.Value = 20;
                mesh.Rings.Value = 8;
                return mesh;
            }
            default:
            {
                var mesh = slot.AttachComponent<BoxMesh>();
                mesh.Size.Value = float3.One * size;
                mesh.ScaleUVWithSize.Value = true;
                return mesh;
            }
        }
    }

    private static void BuildCollider(Slot slot, ToolShape shape, float size)
    {
        switch (shape)
        {
            case ToolShape.Sphere:
                slot.AttachComponent<SphereCollider>().Radius.Value = size * 0.5f;
                break;
            case ToolShape.Cylinder:
            {
                var collider = slot.AttachComponent<CylinderCollider>();
                collider.Radius.Value = size * 0.5f;
                collider.Height.Value = size;
                break;
            }
            case ToolShape.Cone:
            {
                var collider = slot.AttachComponent<ConeCollider>();
                collider.Radius.Value = size * 0.5f;
                collider.Height.Value = size;
                break;
            }
            case ToolShape.Capsule:
            {
                var collider = slot.AttachComponent<CapsuleCollider>();
                collider.Radius.Value = size * 0.25f;
                collider.Height.Value = size;
                break;
            }
            default:
                slot.AttachComponent<BoxCollider>().Size.Value = float3.One * size;
                break;
        }
    }
}
