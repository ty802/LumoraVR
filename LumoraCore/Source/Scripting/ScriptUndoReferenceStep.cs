// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Scripting;

// What a reference pointed at before the script changed it, and after. Pointing it back at something that
// has since been destroyed is refused: a parked slot still counts, a destroyed one does not. A slot's parent
// goes back the way a script's Set Parent moves it, under the same rules.
internal sealed class ScriptUndoReferenceStep : ScriptUndoStep
{
    private readonly ISyncRef _reference;
    private readonly SyncElement _element;
    private readonly Slot? _child;
    private readonly RefID _before;
    private RefID _after;

    public ScriptUndoReferenceStep(ISyncRef reference, SyncElement element)
    {
        _reference = reference;
        _element = element;
        _child = ChildOf(element);
        _before = reference.Value;
    }

    // The slot a reference is the parent of, when it is one.
    public static Slot? ChildOf(SyncElement element)
        => element.Parent is Slot slot && ReferenceEquals(slot.ParentSlotRef, element) ? slot : null;

    public override bool Close()
    {
        if (_element.IsDestroyed)
            return false;
        _after = _reference.Value;
        return _after != _before;
    }

    public override bool Undo(ScriptContext ctx) => Put(ctx, _before);

    public override bool Redo(ScriptContext ctx) => Put(ctx, _after);

    private bool Put(ScriptContext ctx, RefID id)
    {
        if (_element.IsDestroyed)
            return false;
        var target = id.IsNull ? null : ctx.World.ReferenceController?.GetObjectOrNull(in id);
        if (!id.IsNull && target is not { IsDestroyed: false })
            return false;
        if (_child != null)
        {
            if (target is not Slot parent)
                return false;
            ScriptWrites.Reparent(ctx, _child, parent, keepWorldTransform: false);
            return ReferenceEquals(_child.Parent, parent);
        }
        ScriptWrites.Check(ctx, _element);
        _reference.Value = id;
        return _reference.Value == id;
    }

    public override bool Same(ScriptUndoStep other) => other is ScriptUndoReferenceStep step && ReferenceEquals(step._element, _element);

    public override string Describe() => UndoLocale.EditField((_element as ISyncMember)?.Name ?? "reference").Resolve();
}
