// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Localization;

namespace Lumora.Core.Components;

// Several undo records applied as ONE user action: redo runs them in order, undo in reverse.
// A sub-record whose targets are gone is skipped; the step succeeds if any part applied.
public sealed class CompositeUndoBatch : IUndoBatch, IUndoTargetQuery
{
    private readonly List<IUndoBatch> _batches;

    public LocaleText LocalizedDescription { get; }

    public string Description => LocalizedDescription.Resolve();

    private CompositeUndoBatch(LocaleText description, List<IUndoBatch> batches)
    {
        LocalizedDescription = description.IsEmpty ? UndoLocale.Batch : description;
        _batches = batches;
    }

    // null entries are dropped; returns null (nothing), the single record, or a composite
    public static IUndoBatch? Combine(LocaleText description, params IUndoBatch?[] batches)
    {
        var list = new List<IUndoBatch>();
        foreach (var batch in batches)
        {
            if (batch != null)
                list.Add(batch);
        }
        return Build(description, list);
    }

    // The caller's list is copied, so a pooled or reused buffer is safe to pass.
    public static IUndoBatch? Combine(LocaleText description, IReadOnlyList<IUndoBatch> batches)
    {
        var list = new List<IUndoBatch>(batches.Count);
        for (int i = 0; i < batches.Count; i++)
        {
            if (batches[i] != null)
                list.Add(batches[i]);
        }
        return Build(description, list);
    }

    private static IUndoBatch? Build(LocaleText description, List<IUndoBatch> list)
    {
        if (list.Count == 0)
            return null;
        if (list.Count == 1 && description.IsEmpty)
            return list[0];
        return new CompositeUndoBatch(description, list);
    }

    public bool Undo()
    {
        bool any = false;
        for (int i = _batches.Count - 1; i >= 0; i--)
            any |= _batches[i].Undo();
        return any;
    }

    public bool Redo()
    {
        bool any = false;
        for (int i = 0; i < _batches.Count; i++)
            any |= _batches[i].Redo();
        return any;
    }

    public void OnEvicted()
    {
        foreach (var batch in _batches)
            batch.OnEvicted();
    }

    public bool ReferencesElement(IWorldElement element)
    {
        for (int i = 0; i < _batches.Count; i++)
        {
            if (_batches[i] is IUndoTargetQuery query && query.ReferencesElement(element))
                return true;
        }
        return false;
    }
}
