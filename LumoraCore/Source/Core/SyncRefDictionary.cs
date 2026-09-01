// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core;

// A keyed collection whose KEYS are plain values and whose VALUES are world references. Built on the
// element dictionary, so each entry is a real sub-worker: the reference resolves asynchronously,
// survives save/load as a stable identity, and follows the clone when the owning slot is duplicated.
//
// Lookups scan the entries (see SyncObjectDictionary), so this suits modest keyed collections - a
// handful of named sockets, per-slot overrides - rather than large maps.
public class SyncRefDictionary<TKey, TValue> : SyncObjectDictionary<TKey, SyncRef<TValue>>
    where TKey : notnull
    where TValue : class, IWorldElement
{
    // The referenced target under the key, or null when the key is absent or the reference is empty.
    public new TValue? this[TKey key]
    {
        get => base[key]?.Target;
        set => Add(key).Target = value!;
    }

    // Retargets the existing entry when the key is already present, matching the base collection's
    // add-or-return contract - a duplicate key is never an error here.
    public void Add(TKey key, TValue? target)
    {
        Add(key).Target = target!;
    }

    public bool TryGetTarget(TKey key, out TValue? target)
    {
        if (TryGetValue(key, out var reference))
        {
            target = reference.Target;
            return true;
        }
        target = null;
        return false;
    }

    // The backing reference member, for callers that need the member itself (drives, drop targets).
    public SyncRef<TValue>? GetReference(TKey key) => base[key];

    public IEnumerable<TValue?> Targets
    {
        get
        {
            foreach (var reference in Values)
                yield return reference.Target;
        }
    }

    public bool ContainsTarget(TValue? target)
    {
        foreach (var reference in Values)
        {
            if (ReferenceEquals(reference.Target, target))
                return true;
        }
        return false;
    }

    public override string ToString() => $"SyncRefDictionary<{typeof(TKey).Name}, {typeof(TValue).Name}>[{Count}]";
}
