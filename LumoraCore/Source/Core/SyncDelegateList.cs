// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections;
using System.Collections.Generic;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core;

// Network-synchronized list of bound actions. Each entry is a SyncDelegate element, so every handler
// carries its own target reference plus method name, replicates and persists itself, and follows the
// clone when the owning slot is duplicated.
//
// Binding from data still goes through the [SyncMethod] gate on SyncDelegate: a list is not a way
// around it. -xlinka
public class SyncDelegateList<T> : SyncElementList<SyncDelegate<T>>, IEnumerable<T?>, IEnumerable where T : Delegate
{
    public event Action<SyncDelegateList<T>>? OnChanged;

    public SyncDelegateList()
    {
        ElementsAdded += (list, idx, count) => OnChanged?.Invoke(this);
        ElementsRemoved += (list, idx, count) => OnChanged?.Invoke(this);
    }

    public SyncDelegateList(IWorldElement owner) : this()
    {
        if (owner?.World != null)
        {
            Initialize(owner.World, owner);
        }
    }

    // Yields the bound delegates instead of the backing SyncDelegate elements. An entry whose target
    // has not resolved yet (or never will) comes out null.
    public new struct Enumerator : IEnumerator<T?>, IDisposable, IEnumerator
    {
        private SyncElementList<SyncDelegate<T>>.Enumerator _baseEnumerator;

        public T? Current => _baseEnumerator.Current.Target;

        object? IEnumerator.Current => Current;

        internal Enumerator(SyncElementList<SyncDelegate<T>>.Enumerator baseEnumerator)
        {
            _baseEnumerator = baseEnumerator;
        }

        public void Dispose() => _baseEnumerator.Dispose();

        public bool MoveNext() => _baseEnumerator.MoveNext();

        public void Reset() => _baseEnumerator.Reset();
    }

    public new T? this[int index]
    {
        get => GetElement(index).Target;
        set => GetElement(index).Target = value;
    }

    public void Add(T? target)
    {
        Add().Target = target;
    }

    public void Insert(int index, T? target)
    {
        Insert(index).Target = target;
    }

    public int IndexOf(T? target) => FindIndex(entry => Equals(entry.Target, target));

    public bool Contains(T? target) => IndexOf(target) >= 0;

    public bool Remove(T? target)
    {
        int index = IndexOf(target);
        if (index < 0)
            return false;
        RemoveAt(index);
        return true;
    }

    public int RemoveAll(T? target) => RemoveAll(entry => Equals(entry.Target, target));

    // The backing element, for callers that need the member itself.
    public SyncDelegate<T> GetDelegate(int index) => GetElement(index);

    // Runs every entry that actually resolved. One handler throwing must not swallow the rest, so
    // failures are reported per entry and the walk continues.
    public void InvokeAll(params object?[]? args)
    {
        foreach (var entry in Elements)
        {
            if (entry.Target == null)
                continue;
            try
            {
                entry.Invoke(args);
            }
            catch (Exception ex)
            {
                Logging.Logger.Error($"SyncDelegateList: handler '{entry.MethodName}' threw: {ex}");
            }
        }
    }

    public Enumerator GetEnumerator() => new Enumerator(GetElementsEnumerator());

    IEnumerator<T?> IEnumerable<T?>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => $"SyncDelegateList<{typeof(T).Name}>[{Count}]";

    public override void Dispose()
    {
        OnChanged = null;
        base.Dispose();
    }
}
