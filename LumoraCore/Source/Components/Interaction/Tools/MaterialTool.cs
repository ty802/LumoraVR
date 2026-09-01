// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Assets;
using Lumora.Core.Components.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// Carries one material and paints it onto what you point at.
//
// Sampling and applying both go through the renderer's Materials list BY INDEX. MeshRenderer.Material
// is an accessor that ADDS an empty slot-zero entry when the list is empty, so a tool that read
// through it would edit the thing it was only supposed to look at - the same trap the eyedropper's
// provider walk documents. Sample by index, apply by index, never touch the accessor. -xlinka
//
// The stored material is a real sync member: it is what the tool IS once you have loaded it, it
// survives a save, and somebody watching you paint should see the tool holding what you picked up.
// The "next press samples" arming is NOT - that is one hand's momentary mode.
[ComponentCategory("Interaction/Tools")]
public sealed class MaterialTool : EquippableTool
{
    // -1 paints every surface on the renderer. There is no submesh index in a laser hit (the cast
    // reports a collider, not a triangle), so the surface a multi-material mesh gets painted on is a
    // choice you make on the tool rather than one the click can make for you. -xlinka
    public const int AllSurfaces = -1;

    private const int MaxSurfaceCycle = 8;
    private const int MaxParentWalk = 16;

    public readonly AssetRef<MaterialAsset> StoredMaterial = new();
    public readonly Sync<int> Surface = new();

    private bool _sampleArmed;
    private Slot? _bead;

    protected override colorHDR VisualTint => new(0.95f, 0.45f, 0.85f, 1f);

    public bool IsSampleArmed => _sampleArmed;

    public override void OnInit()
    {
        base.OnInit();
        EquipName.Value = "Material Tool";
        Surface.Value = AllSurfaces;
    }

    protected override void BuildVisualExtras(Slot visual)
    {
        _bead = EnsureBead(visual, "Bead", float3.Up * 0.05f, 0.013f, VisualTint);
        RefreshBead();
    }

    public override bool OnPrimaryPress()
    {
        if (!TryGetAim(out var aim))
            return false;

        // An empty tool picks up rather than refusing: the first click on a material orb is what
        // loading the tool looks like, and demanding a trip through the radial menu to do the obvious
        // thing is the sort of gate that gets a tool called broken. -xlinka
        if (_sampleArmed || StoredMaterial.Target == null)
        {
            _sampleArmed = false;
            bool sampled = TrySample(aim.HitSlot);
            RefreshBead();
            return sampled;
        }

        if (EditingBlocked)
            return false;
        return TryApply(aim.HitSlot);
    }

    // Secondary arms the sample, same as the submenu entry. Loading the tool is the thing you do most
    // and it deserves the button rather than a menu trip.
    public override bool OnSecondaryPress()
    {
        _sampleArmed = !_sampleArmed;
        RefreshBead();
        return true;
    }

    public override void OnDequipped()
    {
        base.OnDequipped();
        _sampleArmed = false;
        RefreshBead();
    }

    public bool TrySample(Slot? hitSlot)
    {
        var renderer = FindRenderer(hitSlot);
        if (renderer == null)
            return false;

        int count = renderer.Materials.Count;
        if (count == 0)
            return false;

        int index = Surface.Value == AllSurfaces ? 0 : System.Math.Clamp(Surface.Value, 0, count - 1);
        var provider = renderer.Materials.GetElement(index).Target;
        if (provider == null)
            return false;

        return Guarded(() => StoredMaterial.Target = provider);
    }

    public bool TryApply(Slot? hitSlot)
    {
        var provider = StoredMaterial.Target;
        if (provider == null)
            return false;

        var renderer = FindRenderer(hitSlot);
        if (renderer == null)
            return false;

        int recorded = 0;
        void Add(IUndoBatch? batch)
        {
            if (batch == null)
                return;
            recorded++;
            RecordUndo(batch);
        }

        bool applied;
        using (BeginUndoBatch(UndoLocale.ApplyMaterial))
        {
            applied = Guarded(() =>
            {
                if (renderer.Materials.Count == 0)
                {
                    // A renderer with no surface entries draws nothing at all. Painting it means giving
                    // it its first one, which is a structural list change and gets the list record so
                    // undo takes the entry back out rather than leaving an empty ref behind.
                    Add(ListElementUndoBatch.Add(renderer.Materials, out var added));
                    if (added is AssetRef<MaterialAsset> slotZero)
                        Add(AssignAt(slotZero, provider));
                    return;
                }

                if (Surface.Value == AllSurfaces)
                {
                    for (int i = 0; i < renderer.Materials.Count; i++)
                        Add(AssignAt(renderer.Materials.GetElement(i), provider));
                    return;
                }

                int index = System.Math.Clamp(Surface.Value, 0, renderer.Materials.Count - 1);
                Add(AssignAt(renderer.Materials.GetElement(index), provider));
            });
        }

        return applied && recorded > 0;
    }

    // The before/after here are RefIDs, because a reference IS a RefID field - which is exactly what
    // the shared field record restores.
    private static IUndoBatch? AssignAt(AssetRef<MaterialAsset> element, IAssetProvider<MaterialAsset> provider)
    {
        if (element == null || element.IsDestroyed)
            return null;
        var field = (IField)element;
        object? before = field.BoxedValue;
        element.Target = provider;
        object? after = field.BoxedValue;
        if (Equals(before, after))
            return null;
        return new FieldEditUndoBatch(field, before, after, UndoLocale.ApplyMaterial);
    }

    public override void PopulateToolActions(ContextMenuPage page, ContextMenuContext context)
    {
        page.AddItem(new ContextMenuItem
        {
            Label = _sampleArmed ? "Sampling..." : "Sample Material",
            FillColor = _sampleArmed ? ArmFill : ItemFill,
            OnPressed = _ =>
            {
                if (IsDestroyed)
                    return;
                _sampleArmed = !_sampleArmed;
                RefreshBead();
            },
        });

        page.AddItem(new ContextMenuItem
        {
            Label = Surface.Value == AllSurfaces ? "Surface: All" : $"Surface: {Surface.Value}",
            FillColor = ItemFill,
            OnPressed = _ =>
            {
                if (IsDestroyed)
                    return;
                int next = Surface.Value + 1;
                Surface.Value = next > MaxSurfaceCycle ? AllSurfaces : next;
            },
        });

        if (StoredMaterial.Target != null)
        {
            page.AddItem(new ContextMenuItem
            {
                Label = "Clear Material",
                FillColor = ClearFill,
                OnPressed = _ =>
                {
                    if (IsDestroyed)
                        return;
                    Guarded(() => StoredMaterial.Target = null!);
                    RefreshBead();
                },
            });
        }
    }

    // Walk up from the hit for the renderer that owns the surface. A hit lands on the child slot a
    // mesh happens to live on far more often than on the slot somebody authored the renderer on.
    private static MeshRenderer? FindRenderer(Slot? hitSlot)
    {
        var current = hitSlot;
        for (int depth = 0; current != null && depth < MaxParentWalk; depth++, current = current.Parent)
        {
            if (current.IsDestroyed)
                break;
            var renderer = current.GetComponent<MeshRenderer>();
            if (renderer != null && !renderer.IsDestroyed)
                return renderer;
        }
        return null;
    }

    // The bead shows what the tool is holding: the stored material's own colour when it has one, the
    // arming colour while the next press is a sample, grey when the tool is empty.
    private void RefreshBead()
    {
        if (_bead == null || _bead.IsRemoved)
            return;

        colorHDR tint;
        if (_sampleArmed)
            tint = new colorHDR(1f, 0.75f, 0.2f, 1f);
        else if (StoredMaterial.Target is IPrimaryColorSource source && source.TryGetPrimaryColor(out var stored))
            tint = stored;
        else if (StoredMaterial.Target != null)
            tint = VisualTint;
        else
            tint = new colorHDR(0.35f, 0.35f, 0.4f, 1f);

        SetBeadTint(_bead, tint);
    }
}
