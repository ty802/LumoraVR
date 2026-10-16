// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections;
using System.Collections.Generic;

namespace Lumora.Core;

// Elements by RefID, in pages of 1024 per user byte. Positions are handed out in order, so a page fills
// before the next one starts, and a world of any size never needs one big array: a Dictionary over a few
// hundred thousand elements doubles its way up through a large-object array per step and throws each one
// away. A page that empties is let go. Readers on several threads are safe while nothing writes, as with
// a Dictionary. -xlinka
public sealed class RefIdMap<T> : IEnumerable<KeyValuePair<RefID, T>> where T : class
{
    private const int PageBits = 10;
    private const int PageSize = 1 << PageBits;
    private const ulong PageMask = PageSize - 1;

    private sealed class Page
    {
        public readonly T?[] Items = new T?[PageSize];
        public int Count;
    }

    private readonly Dictionary<ulong, Page> _pages = new();

    public int Count { get; private set; }

    private static ulong PageKey(RefID id, out int index)
    {
        ulong position = id.GetPosition();
        index = (int)(position & PageMask);
        return ((position >> PageBits) << RefID.USER_BITS) | id.GetUserByte();
    }

    public bool TryGetValue(RefID id, out T value)
    {
        if (_pages.TryGetValue(PageKey(id, out int index), out var page) && page.Items[index] is { } found)
        {
            value = found;
            return true;
        }
        value = null!;
        return false;
    }

    public bool ContainsKey(RefID id) => TryGetValue(id, out _);

    public T this[RefID id]
    {
        get => TryGetValue(id, out var value) ? value : throw new KeyNotFoundException($"No element under {id}");
        set => Put(id, value, replace: true);
    }

    public void Add(RefID id, T value) => Put(id, value, replace: false);

    private void Put(RefID id, T value, bool replace)
    {
        ulong key = PageKey(id, out int index);
        if (!_pages.TryGetValue(key, out var page))
        {
            page = new Page();
            _pages.Add(key, page);
        }
        ref var slot = ref page.Items[index];
        if (slot == null)
        {
            page.Count++;
            Count++;
        }
        else if (!replace)
        {
            throw new System.ArgumentException($"An element is already registered under {id}");
        }
        slot = value;
    }

    public bool Remove(RefID id)
    {
        ulong key = PageKey(id, out int index);
        if (!_pages.TryGetValue(key, out var page) || page.Items[index] == null)
            return false;
        page.Items[index] = null;
        Count--;
        if (--page.Count == 0)
            _pages.Remove(key);
        return true;
    }

    public void Clear()
    {
        _pages.Clear();
        Count = 0;
    }

    public IEnumerable<RefID> Keys
    {
        get
        {
            foreach (var pair in this)
                yield return pair.Key;
        }
    }

    public IEnumerable<T> Values
    {
        get
        {
            foreach (var page in _pages.Values)
            {
                var items = page.Items;
                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i] is { } item)
                        yield return item;
                }
            }
        }
    }

    public IEnumerator<KeyValuePair<RefID, T>> GetEnumerator()
    {
        foreach (var (key, page) in _pages)
        {
            byte user = (byte)(key & RefID.USER_MASK);
            ulong first = (key >> RefID.USER_BITS) << PageBits;
            var items = page.Items;
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] is { } item)
                    yield return new KeyValuePair<RefID, T>(RefID.Construct(user, first + (ulong)i), item);
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
