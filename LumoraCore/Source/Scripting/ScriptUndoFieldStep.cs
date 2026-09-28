// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Scripting;

// A field's value before the script changed it, and after.
internal sealed class ScriptUndoFieldStep<T> : ScriptUndoStep
{
    private readonly IField<T> _field;
    private readonly T _before;
    private T _after = default!;

    public ScriptUndoFieldStep(IField<T> field)
    {
        _field = field;
        _before = field.Value;
    }

    public override bool Close()
    {
        if (_field.IsDestroyed)
            return false;
        _after = _field.Value;
        return !EqualityComparer<T>.Default.Equals(_before, _after);
    }

    public override bool Undo(ScriptContext ctx) => Put(ctx, _before);

    public override bool Redo(ScriptContext ctx) => Put(ctx, _after);

    private bool Put(ScriptContext ctx, T value)
    {
        if (_field.IsDestroyed)
            return false;
        ScriptWrites.Write(ctx, _field, value);
        return EqualityComparer<T>.Default.Equals(_field.Value, value);
    }

    public override bool Same(ScriptUndoStep other) => other is ScriptUndoFieldStep<T> step && ReferenceEquals(step._field, _field);

    public override string Describe() => UndoLocale.EditField((_field as ISyncMember)?.Name ?? "field").Resolve();
}
