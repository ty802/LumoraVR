// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Components;

// Several undo records applied as ONE user action: redo runs them in order, undo in reverse.
// A sub-record whose targets are gone is skipped; the step succeeds if any part applied.
public sealed class CompositeUndoBatch : IUndoBatch
{
    private readonly List<IUndoBatch> _batches;

    public string Description { get; }

    private CompositeUndoBatch(string description, List<IUndoBatch> batches)
    {
        Description = description;
        _batches = batches;
    }

    // null entries are dropped; returns null (nothing), the single record, or a composite
    public static IUndoBatch? Combine(string description, params IUndoBatch?[] batches)
    {
        var list = new List<IUndoBatch>();
        foreach (var batch in batches)
        {
            if (batch != null)
                list.Add(batch);
        }
        if (list.Count == 0)
            return null;
        if (list.Count == 1)
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
}
