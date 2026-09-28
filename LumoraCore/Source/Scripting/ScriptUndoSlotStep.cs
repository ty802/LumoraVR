// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// A slot a script made, or put out of the world. Record Spawn's slot stands while the step is done and is
// parked while it is undone; Destroy With Undo's is the other way round. Parked is out of the world: under
// the world's undo pen, switched off, where an undo can bring it back to the parent and pose it had. A step
// that leaves every history while its slot is still parked destroys it for real. A Local script parks what
// it made under a pen of its own, beside itself on this machine. -xlinka
internal sealed class ScriptUndoSlotStep : ScriptUndoStep
{
    public const string LocalPenName = "Undo Parked";

    private readonly Slot _slot;
    private readonly bool _destroy;
    private Slot? _parent;
    private float3 _position;
    private floatQ _rotation;
    private float3 _scale;
    private bool _active;

    // Where the slot is parked, while it is.
    private Slot? _pen;

    private ScriptUndoSlotStep(Slot slot, bool destroy)
    {
        _slot = slot;
        _destroy = destroy;
    }

    // Record Spawn: where the slot stands is taken as the batch closes.
    public static ScriptUndoSlotStep Spawned(Slot slot) => new(slot, destroy: false);

    // Destroy With Undo: parked now, from where it stands now.
    public static ScriptUndoSlotStep Destroyed(ScriptContext ctx, Slot slot)
    {
        var step = new ScriptUndoSlotStep(slot, destroy: true);
        step.TakePlace();
        if (!step.Park(ctx))
            throw new UnauthorizedAccessException("the slot could not be put out of the world");
        return step;
    }

    public override bool Close()
    {
        if (_slot.IsDestroyed)
            return false;
        if (!_destroy)
            TakePlace();
        return true;
    }

    public override bool Undo(ScriptContext ctx) => _destroy ? Restore(ctx) : Park(ctx);

    public override bool Redo(ScriptContext ctx) => _destroy ? Park(ctx) : Restore(ctx);

    public override void Evict()
    {
        var pen = _pen;
        _pen = null;
        if (pen == null || _slot.IsDestroyed || !ReferenceEquals(_slot.Parent, pen))
            return;
        if (_slot.IsLocalElement)
        {
            _slot.Destroy();
            return;
        }
        // It was put out of the world as its script's principal; this only finishes that. -xlinka
        using (_slot.World?.DataModelPermissions?.EnterSystemBypass())
            _slot.Destroy();
    }

    public override bool Same(ScriptUndoStep other)
        => !_destroy && other is ScriptUndoSlotStep { _destroy: false } step && ReferenceEquals(step._slot, _slot);

    public override string Describe()
        => _destroy ? UndoLocale.Destroy.Resolve() : UndoLocale.SpawnNamed(_slot.Name.Value ?? string.Empty).Resolve();

    private void TakePlace()
    {
        _parent = _slot.Parent;
        _position = _slot.LocalPosition.Value;
        _rotation = _slot.LocalRotation.Value;
        _scale = _slot.LocalScale.Value;
        _active = _slot.ActiveSelf.Value;
    }

    private bool Park(ScriptContext ctx)
    {
        if (_slot.IsDestroyed)
            return false;
        var pen = Pen(ctx);
        if (pen == null)
            return false;
        if (!ReferenceEquals(_slot.Parent, pen))
        {
            ScriptWrites.Check(ctx, _slot.ParentSlotRef);
            ScriptWrites.Check(ctx, _slot.ActiveSelf);
            if (!_slot.TrySetParent(pen, preserveGlobalTransform: false) || !ReferenceEquals(_slot.Parent, pen))
                return false;
        }
        _slot.ActiveSelf.Value = false;
        _pen = pen;
        return true;
    }

    // Back under the parent it had, at the pose it had. A parent destroyed since means there is nowhere to
    // put it, and the step is refused rather than leaving it somewhere it never was.
    private bool Restore(ScriptContext ctx)
    {
        if (_slot.IsDestroyed)
            return false;
        var parent = _parent ?? ctx.World.RootSlot;
        if (parent == null || parent.IsDestroyed)
            return false;
        ScriptWrites.Check(ctx, _slot.ParentSlotRef);
        ScriptWrites.Check(ctx, _slot.LocalPosition);
        ScriptWrites.Check(ctx, _slot.LocalRotation);
        ScriptWrites.Check(ctx, _slot.LocalScale);
        ScriptWrites.Check(ctx, _slot.ActiveSelf);
        if (!ReferenceEquals(_slot.Parent, parent)
            && (!_slot.TrySetParent(parent, preserveGlobalTransform: false) || !ReferenceEquals(_slot.Parent, parent)))
            return false;
        _slot.LocalPosition.Value = _position;
        _slot.LocalRotation.Value = _rotation;
        _slot.LocalScale.Value = _scale;
        _slot.ActiveSelf.Value = _active;
        _pen = null;
        return true;
    }

    private static Slot? Pen(ScriptContext ctx)
    {
        if (ctx.IsLocal)
        {
            var self = ctx.Self;
            if (self == null || self.IsDestroyed)
                return null;
            foreach (var child in self.LocalChildren)
            {
                if (!child.IsDestroyed && child.Name.Value == LocalPenName)
                    return child;
            }
            var pen = self.AddLocalSlot(LocalPenName);
            pen.ActiveSelf.Value = false;
            return pen;
        }
        var world = ctx.World;
        if (UndoGraveyard.Find(world) is { } found)
            return found;
        // The pen is the world's, whoever's script asks for it first.
        using (world.DataModelPermissions?.EnterSystemBypass())
            return UndoGraveyard.Acquire(world);
    }
}
