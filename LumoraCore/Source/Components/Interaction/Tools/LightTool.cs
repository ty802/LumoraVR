// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// Drops a lamp where you point and lets you size it by pulling away from it.
//
// The numbers are the ones the lamp stands in the scratch space were tuned to, because "a light with
// default values" is a white 1-intensity 10-metre point light that washes out everything near it. A
// point lamp is warm, soft-shadowed and six metres; a spot is cold, hard and a 32 degree cone; a
// directional is dialled right down, because a second sun is a change to every area at once.
//
// Hold-to-size drives RANGE on the positional types and INTENSITY on the directional one, which has
// no range to speak of. Both read the same gesture: the further the hand gets from where the lamp
// landed, the more of it there is. -xlinka
[ComponentCategory("Interaction/Tools")]
public sealed class LightTool : EquippableTool
{
    private const float MinRange = 0.5f;
    private const float MaxRange = 60f;
    private const float MaxIntensity = 4f;
    // Floor on the press-time hand-to-lamp distance used as the drag's unit. Without it, placing a
    // lamp on a surface right under the hand makes every millimetre of drift a huge multiplier.
    private const float MinDragReference = 0.2f;

    public readonly Sync<LightType> Kind = new();
    public readonly Sync<color> Tint = new();
    public readonly Sync<float> Intensity = new();
    public readonly Sync<float> Range = new();
    public readonly Sync<ShadowType> Shadows = new();

    // Aim state for the press currently in progress. Transient by design: the drag belongs to the one
    // hand doing it and replicating a half-finished lamp size would fight the owner's own writes.
    private Light? _placing;
    private float3 _placedAt;
    private float _dragReference;

    protected override colorHDR VisualTint => new(1f, 0.85f, 0.35f, 1f);

    public override void OnInit()
    {
        base.OnInit();
        EquipName.Value = "Light Tool";
        Kind.Value = LightType.Point;
        Tint.Value = new color(1.00f, 0.72f, 0.42f, 1f);
        Intensity.Value = 3.2f;
        Range.Value = 6f;
        Shadows.Value = ShadowType.Soft;
    }

    // Not called "Bulb": the scratch space already has one of those on the touch console, and a tool
    // that plants a second slot of that name in the same world breaks anything looking the first one
    // up by name. -xlinka
    protected override void BuildVisualExtras(Slot visual)
    {
        EnsureBead(visual, "Tool Bulb", float3.Up * 0.055f, 0.014f, VisualTint);
    }

    public override bool OnPrimaryPress()
    {
        if (EditingBlocked || SpawnBlocked || !TryGetAim(out var aim))
            return false;

        var parent = World?.RootSlot;
        if (parent == null)
            return false;

        var kind = Kind.Value;
        var slot = TryAddSlot(parent, kind + " Light");
        if (slot == null)
            return false;

        Light? light = null;
        bool built = Guarded(() =>
        {
            // A lamp is placed a little off the surface it was aimed at: sitting a point light exactly
            // on the floor lights half a sphere of nothing.
            slot.GlobalRotation = AlignUpTo(aim.Normal);
            slot.GlobalPosition = aim.Point + aim.Normal * 0.3f;
            if (kind != LightType.Point)
            {
                // A spot flat against its own mounting surface lights the mount. Aim it back down the
                // beam that placed it, which is where the user was looking.
                slot.GlobalRotation = AlignUpTo(-aim.Direction);
            }

            light = slot.AttachComponent<Light>();
            light.Type.Value = kind;
            light.LightColor.Value = Tint.Value;
            light.Intensity.Value = Intensity.Value;
            light.Shadows.Value = Shadows.Value;
            if (kind != LightType.Directional)
            {
                light.Range.Value = Range.Value;
                light.SpotAngle.Value = 32f;
                // Stop paying for the shadow map of a six-metre lamp from across the world.
                light.DistanceFadeBegin.Value = 10f;
                light.DistanceFadeLength.Value = 4f;
            }

            var bulb = slot.AddSlot("Bulb");
            var mesh = bulb.AttachComponent<SphereMesh>();
            mesh.Radius.Value = 0.06f;
            mesh.Segments.Value = 16;
            mesh.Rings.Value = 10;
            var material = bulb.AttachComponent<UnlitMaterial>();
            material.TintColor.Value = new colorHDR(Tint.Value.r, Tint.Value.g, Tint.Value.b, 1f);
            material.UseVertexColor.Value = false;
            var renderer = bulb.AttachComponent<MeshRenderer>();
            renderer.Mesh.Target = mesh;
            renderer.Material.Target = material;
            renderer.ShadowCastMode.Value = ShadowCastMode.Off;

            slot.AttachComponent<SphereCollider>().Radius.Value = 0.06f;
            slot.AttachComponent<Grabbable>();
        });

        if (!built)
        {
            Guarded(() => slot.Destroy());
            return false;
        }

        _placing = light;
        _placedAt = slot.GlobalPosition;
        // The distance the hand was at when the lamp landed IS the unit of the drag, so the lamp keeps
        // exactly the authored range until the hand actually moves. Anchoring on a fixed number instead
        // made a press collapse the range to a fraction of itself before you had done anything. -xlinka
        _dragReference = MathF.Max(MinDragReference, float3.Distance(Tip, _placedAt));
        RecordUndo(SlotExistenceUndoBatch.Created(World, new[] { slot }, UndoLocale.SpawnLight));
        return true;
    }

    public override bool OnPrimaryHold()
    {
        var light = _placing;
        if (light == null || light.IsDestroyed)
        {
            _placing = null;
            return false;
        }

        float scale = float3.Distance(Tip, _placedAt) / _dragReference;
        Guarded(() =>
        {
            if (light.Type.Value == LightType.Directional)
                light.Intensity.Value = System.Math.Clamp(Intensity.Value * scale, 0.02f, MaxIntensity);
            else
                light.Range.Value = System.Math.Clamp(Range.Value * scale, MinRange, MaxRange);
        });
        return true;
    }

    public override bool OnPrimaryRelease()
    {
        bool had = _placing != null;
        _placing = null;
        return had;
    }

    public override void OnDequipped()
    {
        base.OnDequipped();
        _placing = null;
    }

    public override void PopulateToolActions(ContextMenuPage page, ContextMenuContext context)
    {
        foreach (var kind in Enum.GetValues<LightType>())
        {
            var pick = kind;
            page.AddItem(new ContextMenuItem
            {
                Label = kind.ToString(),
                FillColor = Kind.Value == kind ? ActiveFill : ItemFill,
                OnPressed = _ => { if (!IsDestroyed) Kind.Value = pick; },
            });
        }

        page.AddItem(new ContextMenuItem
        {
            Label = $"Shadows: {Shadows.Value}",
            FillColor = ItemFill,
            OnPressed = _ =>
            {
                if (IsDestroyed)
                    return;
                var values = Enum.GetValues<ShadowType>();
                Shadows.Value = values[((int)Shadows.Value + 1) % values.Length];
            },
        });
    }
}
