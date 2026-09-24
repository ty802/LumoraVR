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

    private RespawnRegistry() { }

    public static RespawnRegistry? For(World? world)
        => world == null ? null : Registries.GetValue(world, static _ => new RespawnRegistry());

    public IReadOnlyList<RespawnData> Items
    {
        get { Prune(); return _items; }
    }

    internal void Register(RespawnData data)
    {
        if (data != null && !_items.Contains(data))
            _items.Add(data);
    }

    internal void Unregister(RespawnData data)
    {
        if (data != null)
            _items.Remove(data);
    }

    private void Prune()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var data = _items[i];
            if (data == null || data.IsDestroyed || data.Slot == null || data.Slot.IsDestroyed)
                _items.RemoveAt(i);
        }
    }
}
