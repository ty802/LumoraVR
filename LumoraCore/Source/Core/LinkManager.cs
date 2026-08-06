// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core;

// Arbitrates who gets to hold a linkable element, and remembers fields whose driving link was just
// released so the sync loop can re-broadcast their current value.
//
// GRANTING. A link never takes a target by itself. When a link's reference resolves - on the machine that
// authored it, on a peer that received it, or on a peer that loaded it from a save - the link asks here,
// and this decides. One granted DRIVER per element: a second drive aimed at an already-driven field is
// REFUSED, stays inert (IsLinkValid false, it never writes), and gets retried each update in case the
// holder lets go. That is the whole reason a drive can be a replicated member at all - every peer runs
// the same arbitration over the same replicated refs and lands on the same holder.
//
// RELEASING. While a field is driven the authority suppresses its delta generation, so peers only ever
// see whatever the drive last pushed. When the drive goes away the field stops changing, so there is no
// dirty delta to send and peers would be stuck on the stale driven value forever. We remember which
// fields lost their drive and, once per sync cycle, re-send their real current value as a small
// full-state batch. Only the authority does this; a client has no business restating values for anyone.
// -xlinka
public class LinkManager
{
    // DriveReleased runs on the data-model thread (a link released during an update), while
    // GetReleasedDrives drains on the sync thread. Cheap lock keeps the two from racing the list. -xlinka
    private readonly object _lock = new();
    private readonly List<ILinkable> _releasedDrives = new();

    // Refused link requests awaiting a retry. Network decode can set a link's value off the data-model
    // thread, so this list takes the same treatment.
    private readonly object _pendingLock = new();
    private readonly List<ILinkRef> _pendingLinks = new();

    public World World { get; private set; }

    public LinkManager(World world)
    {
        World = world;
    }

    // Grants it immediately when the target is free; otherwise the request is parked and retried once per
    // update until it wins the target or the link moves on.
    public void RequestLink(ILinkRef link)
    {
        if (link == null || link.IsDestroyed)
            return;

        if (TryGrant(link))
        {
            CancelLink(link);
            return;
        }

        lock (_pendingLock)
        {
            if (!_pendingLinks.Contains(link))
                _pendingLinks.Add(link);
        }
    }

    public void CancelLink(ILinkRef link)
    {
        if (link == null)
            return;

        lock (_pendingLock)
        {
            _pendingLinks.Remove(link);
        }
    }

    // Retry every refused request. Runs once per world update, before component updates, so a link that
    // won its target this frame is already active by the time its owner reads IsLinkValid.
    public void GrantLinks()
    {
        List<ILinkRef> snapshot;
        lock (_pendingLock)
        {
            if (_pendingLinks.Count == 0)
                return;
            snapshot = new List<ILinkRef>(_pendingLinks);
        }

        List<ILinkRef>? settled = null;
        foreach (var link in snapshot)
        {
            // Dead, cleared, or granted: either way it stops being a pending request.
            if (link.IsDestroyed || link.Target == null || TryGrant(link))
            {
                (settled ??= new List<ILinkRef>()).Add(link);
            }
        }

        if (settled == null)
            return;

        lock (_pendingLock)
        {
            foreach (var link in settled)
                _pendingLinks.Remove(link);
        }
    }

    // One granted driver per element. A non-driving link (a hook) can take over from anything, and a link
    // re-asking for a target it already holds is a no-op that reports success. -xlinka
    private static bool TryGrant(ILinkRef link)
    {
        var target = link.Target;
        if (target == null || target.IsDestroyed)
            return false;

        var holder = target.DirectLink;
        if (holder != null && ReferenceEquals(holder, link))
        {
            // Already holding it. Re-linking here would look to the target like its driver is changing,
            // which fires a released-drive re-broadcast and invalidates the field for nothing. -xlinka
            if (link.WasLinkGranted)
                return true;
        }
        else if (holder != null && link.IsDriving && holder.IsDriving && holder.WasLinkGranted)
        {
            return false;
        }

        target.Link(link);
        link.GrantLink();
        return true;
    }

    // A field's granted, driving link was just released. Only matters on the authority and
    // only for elements that actually replicate (Local-allocation elements never leave this machine).
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

    // Keeps only real, still-alive sync elements whose drive genuinely went away. The list is cleared
    // after.
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
        lock (_pendingLock)
        {
            _pendingLinks.Clear();
        }
        World = null!;
    }
}
