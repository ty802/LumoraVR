// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Localization;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

public sealed class FieldEditUndoBatch : IUndoBatch
{
    private readonly IField _field;
    private readonly object? _before;
    private object? _after;

    public LocaleText LocalizedDescription { get; }

    public string Description => LocalizedDescription.Resolve();

    public FieldEditUndoBatch(IField field, object? before, object? after, LocaleText description)
    {
        _field = field;
        _before = before;
        _after = after;
        LocalizedDescription = description;
    }

    // consecutive edits of the same field merge into one step (slider drags, typing)
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
    // no-op without a manager or batch
    public static void Record(Worker context, IUndoBatch? batch)
        => Record(context?.World, batch);

    // overload for a caller that has a world but no worker of its own (static import paths)
    public static void Record(World? world, IUndoBatch? batch)
    {
        if (batch == null)
            return;
        world?.LocalUser?.Root?.Slot?.GetComponentInChildren<UndoManager>()?.Record(batch);
    }

    public static void RecordEdit(Worker context, IField field, object? before, object? after)
    {
        var world = context?.World;
        var manager = world?.LocalUser?.Root?.Slot?.GetComponentInChildren<UndoManager>();
        if (manager == null)
            return;

        if (manager.CurrentBatch is FieldEditUndoBatch merge && merge.TryMerge(field, after))
            return;
        string name = (field as ISyncMember)?.Name ?? "field";
        manager.Record(new FieldEditUndoBatch(field, before, after, UndoLocale.EditField(name)));
    }
}
