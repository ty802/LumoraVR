// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Lumora.Core.Components;

// Every RespawnData in a world, so a RespawnPlane can ask "who is below me" without walking the whole
// slot tree. The walk it replaces visited every slot in the world every frame and cost 40 ms a frame
// once an imported avatar was standing in it. -xlinka
public sealed class RespawnRegistry
{
    private static readonly ConditionalWeakTable<World, RespawnRegistry> Registries = new();
    private readonly List<RespawnData> _items = new();

    // Each entry's place in the list. A search per register and per remove made ten thousand copies of one
    // prop cost seconds to make and seconds to clear.
    private readonly Dictionary<RespawnData, int> _index = new();

    private RespawnRegistry() { }

    public static RespawnRegistry? For(World? world)
        => world == null ? null : Registries.GetValue(world, static _ => new RespawnRegistry());

    public IReadOnlyList<RespawnData> Items
    {
        get { Prune(); return _items; }
    }

    internal void Register(RespawnData data)
    {
        if (data == null || _index.ContainsKey(data))
            return;
        _index[data] = _items.Count;
        _items.Add(data);
    }

    internal void Unregister(RespawnData data)
    {
        if (data != null && _index.TryGetValue(data, out int at))
            RemoveAt(at);
    }

    // The last entry takes the place of the one leaving.
    private void RemoveAt(int at)
    {
        _index.Remove(_items[at]);
        int last = _items.Count - 1;
        if (at != last)
        {
            var moved = _items[last];
            _items[at] = moved;
            _index[moved] = at;
        }
        _items.RemoveAt(last);
    }

    private void Prune()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var data = _items[i];
            if (data == null || data.IsDestroyed || data.Slot == null || data.Slot.IsDestroyed)
                RemoveAt(i);
        }
    }
}
