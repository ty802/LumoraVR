// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

/// <summary>Undoable sync-field value edit (whole boxed value before/after).</summary>
public sealed class FieldEditUndoBatch : IUndoBatch
{
    private readonly IField _field;
    private readonly object? _before;
    private object? _after;

    public string Description { get; }

    public FieldEditUndoBatch(IField field, object? before, object? after, string description)
    {
        _field = field;
        _before = before;
        _after = after;
        Description = description;
    }

    /// <summary>Consecutive edits of the same field merge into one step (slider drags, typing).</summary>
    public bool TryMerge(IField field, object? after)
    {
        if (!ReferenceEquals(field, _field))
            return false;
        _after = after;
        return true;
    }

    public bool Undo()
    {
        if (_field is not SyncElement { IsDestroyed: false })
            return false;
        // BoxedValue is typed non-null, but a reference-type field legitimately restores to null. -xlinka
        _field.BoxedValue = _before!;
        return true;
    }

    public bool Redo()
    {
        if (_field is not SyncElement { IsDestroyed: false })
            return false;
        _field.BoxedValue = _after!;
        return true;
    }

    public void OnEvicted() { }
}

public static class InspectorUndo
{
    /// <summary>Record a field edit into the local user's undo history (no-op without a manager).</summary>
    public static void RecordEdit(Worker context, IField field, object? before, object? after)
    {
        var world = context?.World;
        var manager = world?.LocalUser?.Root?.Slot?.GetComponentInChildren<UndoManager>();
        if (manager == null)
            return;

        string name = (field as ISyncMember)?.Name ?? "field";
        if (manager.CurrentBatch is FieldEditUndoBatch merge && merge.TryMerge(field, after))
            return;
        manager.Record(new FieldEditUndoBatch(field, before, after, $"Edit {name}"));
    }
}
