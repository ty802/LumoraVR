// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core;

/// <summary>
/// Tracks fields whose driving link was just released on the authority so the sync loop can
/// re-broadcast their current value to everyone.
///
/// While a field is being driven the authority suppresses its delta generation - peers only ever
/// see whatever the drive last pushed. When the drive goes away the field stops changing, so there's
/// no dirty delta to send and peers would otherwise be stuck on the stale driven value forever. To
/// fix that we remember which fields lost their drive and, once per sync cycle, re-send their real
/// current value as a small full-state batch. Only the authority does this; a client has no business
/// restating values for anyone. -xlinka
/// </summary>
public class LinkManager
{
    // DriveReleased runs on the data-model thread (a link being released during an update), while
    // GetReleasedDrives drains on the sync thread. Cheap lock keeps the two from racing the list. -xlinka
    private readonly object _lock = new();
    private readonly List<ILinkable> _releasedDrives = new();

    public World World { get; private set; }

    public LinkManager(World world)
    {
        World = world;
    }

    /// <summary>
    /// Note that a field's granted, driving link was just released. Only matters on the authority and
    /// only for elements that actually replicate (Local-allocation elements never leave this machine).
    /// </summary>
    public void DriveReleased(ILinkable linkable)
    {
        if (World == null || !World.IsAuthority || linkable == null)
            return;

        // Local elements live on one machine only, so there's nobody to re-broadcast a correction to. -xlinka
        if (linkable.ReferenceID.IsLocalID)
            return;

        lock (_lock)
        {
            _releasedDrives.Add(linkable);
        }
    }

    /// <summary>
    /// Drain the pending released-drive set into <paramref name="released"/>, keeping only entries that
    /// are real, still-alive sync elements whose drive genuinely went away. The list is cleared after.
    /// </summary>
    public void GetReleasedDrives(List<Networking.Sync.SyncElement> released)
    {
        if (!World.IsAuthority)
            throw new InvalidOperationException("Only the authority handles released drives");

        lock (_lock)
        {
            foreach (var d in _releasedDrives)
            {
                // A genuinely released drive: still a live SyncElement, no longer driven, not disposed.
                // Filtering on SyncElement also drops any hook-only linkables that never replicate. -xlinka
                if (d is Networking.Sync.SyncElement se && !d.IsDriven && !se.IsDisposed)
                {
                    released.Add(se);
                }
            }
            _releasedDrives.Clear();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _releasedDrives.Clear();
        }
        World = null!;
    }
}
