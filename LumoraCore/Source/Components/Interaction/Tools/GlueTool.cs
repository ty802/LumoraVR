// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components.UI;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// Click A, then click B, and A ends up parented under B without moving.
//
// The pick is a real sync member rather than a private field: half of gluing is a two-step gesture
// that somebody else in the room can see you are halfway through, and a pick that survives a
// disconnect blip is better than one that silently forgets which object you had chosen. The tint on
// the bead is the local half of the same signal.
//
// Group mode is the other half of what people actually mean by "glue these together": rather than
// making one a child of the other, both go under a fresh slot at their midpoint, so neither one
// inherits the other's pivot. -xlinka
[ComponentCategory("Interaction/Tools")]
public sealed class GlueTool : EquippableTool
{
    private static readonly colorHDR IdleTint = new(0.55f, 0.9f, 0.6f, 1f);
    private static readonly colorHDR ArmedTint = new(1f, 0.85f, 0.25f, 1f);

    public readonly SyncRef<Slot> PendingChild = new();
    public readonly Sync<bool> GroupMode = new();

    private Slot? _bead;

    protected override colorHDR VisualTint => new(0.45f, 0.9f, 0.55f, 1f);

    public override void OnInit()
    {
        base.OnInit();
        EquipName.Value = "Glue Tool";
    }

    protected override void BuildVisualExtras(Slot visual)
    {
        _bead = EnsureBead(visual, "Bead", float3.Up * 0.05f, 0.012f, IdleTint);
        RefreshBead();
    }

    public override bool OnPrimaryPress()
    {
        if (EditingBlocked || !TryGetAim(out var aim))
            return false;

        var target = ResolveObjectRoot(aim.HitSlot);
        if (target == null || IsOffLimits(target))
            return false;

        var pending = PendingChild.Target;
        if (pending == null || pending.IsDestroyed)
        {
            Guarded(() => PendingChild.Target = target);
            RefreshBead();
            return true;
        }

        Guarded(() => PendingChild.Target = null!);
        RefreshBead();

        if (ReferenceEquals(pending, target))
            return false;
        // A parent cannot become a child of its own descendant, and asking for it is how you lose a
        // whole subtree.
        if (target.IsDescendantOf(pending))
            return false;

        return GroupMode.Value ? Group(pending, target) : Attach(pending, target);
    }

    public override bool OnSecondaryPress()
    {
        if (PendingChild.Target == null)
            return false;
        Guarded(() => PendingChild.Target = null!);
        RefreshBead();
        return true;
    }

    public override void OnDequipped()
    {
        base.OnDequipped();
        Guarded(() => PendingChild.Target = null!);
        RefreshBead();
    }

    // preserveGlobalTransform is the entire point: gluing must not move anything.
    private bool Attach(Slot child, Slot parent)
    {
        var record = SlotTransformUndoBatch.Begin(child, UndoLocale.Glue);
        if (!child.TrySetParent(parent, preserveGlobalTransform: true))
            return false;
        RecordUndo(record?.Commit());
        return true;
    }

    private bool Group(Slot a, Slot b)
    {
        var host = b.Parent ?? World?.RootSlot;
        if (host == null)
            return false;

        var group = TryAddSlot(host, "Glued Group");
        if (group == null)
            return false;

        var recordA = SlotTransformUndoBatch.Begin(a, UndoLocale.GlueGroup);
        var recordB = SlotTransformUndoBatch.Begin(b, UndoLocale.GlueGroup);
        bool grouped = Guarded(() =>
        {
            group.GlobalPosition = (a.GlobalPosition + b.GlobalPosition) * 0.5f;
            group.GlobalRotation = floatQ.Identity;
            a.SetParent(group, preserveGlobalTransform: true);
            b.SetParent(group, preserveGlobalTransform: true);
        });

        if (!grouped)
        {
            Guarded(() => group.Destroy());
            return false;
        }

        // Order matters on the way back: the transforms have to be restored (which pulls both objects
        // out of the group) BEFORE the group is parked, or they ride it into the graveyard.
        using (BeginUndoBatch(UndoLocale.GlueGroup))
        {
            RecordUndo(SlotExistenceUndoBatch.Created(World, new[] { group }, UndoLocale.GlueGroup));
            RecordUndo(recordA?.Commit());
            RecordUndo(recordB?.Commit());
        }
        return true;
    }

    public override void PopulateToolActions(ContextMenuPage page, ContextMenuContext context)
    {
        page.AddItem(new ContextMenuItem
        {
            Label = GroupMode.Value ? "Mode: Group Under New" : "Mode: Parent Under",
            FillColor = GroupMode.Value ? ActiveFill : ItemFill,
            OnPressed = _ => { if (!IsDestroyed) GroupMode.Value = !GroupMode.Value; },
        });

        if (PendingChild.Target != null)
        {
            page.AddItem(new ContextMenuItem
            {
                Label = "Clear Pick",
                FillColor = ClearFill,
                OnPressed = _ =>
                {
                    if (IsDestroyed)
                        return;
                    Guarded(() => PendingChild.Target = null!);
                    RefreshBead();
                },
            });
        }
    }

    private void RefreshBead()
    {
        bool armed = PendingChild.Target != null && !PendingChild.Target.IsDestroyed;
        SetBeadTint(_bead, armed ? ArmedTint : IdleTint);
    }
}
