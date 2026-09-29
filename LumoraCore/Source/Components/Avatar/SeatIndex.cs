// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Lumora.Core.Components.Avatar;

// Every seat in a world, so the seat someone is in is found by asking the seats rather than by walking the
// world. Seats add themselves as they wake and leave as they go. -xlinka
public static class SeatIndex
{
    private static readonly ConditionalWeakTable<World, List<Seat>> _byWorld = new();
    private static readonly object _lock = new();

    internal static void Add(Seat seat)
    {
        var world = seat.World;
        if (world == null)
            return;
        lock (_lock)
        {
            var seats = _byWorld.GetValue(world, static _ => new List<Seat>());
            if (!seats.Contains(seat))
                seats.Add(seat);
        }
    }

    internal static void Remove(World? world, Seat seat)
    {
        if (world == null)
            return;
        lock (_lock)
        {
            if (_byWorld.TryGetValue(world, out var seats))
                seats.Remove(seat);
        }
    }

    // Where a seat holds its occupant: its seat space, else its own slot.
    public static Slot? SpaceOf(Seat seat)
    {
        if (seat.SeatSpace.Target is { IsDestroyed: false } space)
            return space;
        return seat.Slot is { IsDestroyed: false } slot ? slot : null;
    }

    // The seat whose space a user's body hangs straight from, which is what sitting is: the same test a
    // seat makes of its own occupant. Null for someone standing.
    public static Seat? Holding(User? user)
    {
        if (user == null || user.IsDestroyed || user.World is not { } world)
            return null;
        var root = user.Root is { IsDestroyed: false } own ? own : user.UserRootRef.Target;
        var parent = root is { IsDestroyed: false } ? root.Slot?.Parent : null;
        if (parent == null)
            return null;
        lock (_lock)
        {
            if (!_byWorld.TryGetValue(world, out var seats))
                return null;
            foreach (var seat in seats)
            {
                if (!seat.IsDestroyed && ReferenceEquals(SpaceOf(seat), parent))
                    return seat;
            }
        }
        return null;
    }
}
