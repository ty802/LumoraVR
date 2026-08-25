// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Lumora.Core.Components.Magnets;

// Live sockets and guides of one world. Snapping asks this instead of the physics space.
//
// A physics overlap would need every socket to carry a trigger collider sized to its own snap
// distance, which is a collider per socket in the broadphase plus a proxy body to keep in sync with
// the field. Sockets are dozens per world, not thousands, so a flat list plus a squared-distance
// pass is both cheaper and exact: it sees sockets on slots that carry no collider at all, and it
// cannot miss one because the broadphase had not caught up with a field edit that frame.
//
// The table is keyed by World so a closed world takes its entries with it rather than leaking them
// into a process-global list, same reasoning as the particle budget. -xlinka
public sealed class MagnetRegistry
{
    private static readonly ConditionalWeakTable<World, MagnetRegistry> Registries = new();

    private readonly List<MagnetSocket> _sockets = new();
    private readonly List<MagnetGuide> _guides = new();

    private MagnetRegistry() { }

    // Created on first use. Null world yields null.
    public static MagnetRegistry? For(World? world)
        => world == null ? null : Registries.GetValue(world, static _ => new MagnetRegistry());

    // Dead entries pruned on the way out.
    public IReadOnlyList<MagnetSocket> Sockets
    {
        get { Prune(_sockets); return _sockets; }
    }

    // Dead entries pruned on the way out.
    public IReadOnlyList<MagnetGuide> Guides
    {
        get { Prune(_guides); return _guides; }
    }

    // So an item that is being carried knows whether polling is worth anything at all.
    public bool HasAutoAttachSockets
    {
        get
        {
            var sockets = Sockets;
            for (int i = 0; i < sockets.Count; i++)
            {
                if (sockets[i].AutoAttach.Value && sockets[i].Enabled.Value)
                    return true;
            }
            return false;
        }
    }

    internal void Register(MagnetSocket socket)
    {
        if (socket != null && !_sockets.Contains(socket))
            _sockets.Add(socket);
    }

    internal void Unregister(MagnetSocket socket)
    {
        if (socket != null)
            _sockets.Remove(socket);
    }

    internal void Register(MagnetGuide guide)
    {
        if (guide != null && !_guides.Contains(guide))
            _guides.Add(guide);
    }

    internal void Unregister(MagnetGuide guide)
    {
        if (guide != null)
            _guides.Remove(guide);
    }

    private static void Prune<T>(List<T> list) where T : Component
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var entry = list[i];
            if (entry == null || entry.IsDestroyed || entry.Slot == null || entry.Slot.IsDestroyed)
                list.RemoveAt(i);
        }
    }
}
