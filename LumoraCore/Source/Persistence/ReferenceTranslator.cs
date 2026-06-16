// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Persistence;

/// <summary>
/// Maps a world's local <see cref="RefID"/>s to stable GUIDs and back, so cross-references survive
/// a save/load round-trip. On save, every referenced element's RefID is fetched as a GUID written
/// into the tree. On load, each rebuilt element binds its fresh RefID to its saved GUID; references
/// to a GUID resolve immediately if their target is already loaded, otherwise they wait for it.
/// </summary>
public sealed class ReferenceTranslator
{
    private readonly Dictionary<Guid, RefID> _globalToLocal = new();
    private readonly Dictionary<RefID, Guid> _localToGlobal = new();
    private Dictionary<Guid, List<ISyncRef>> _pendingRequests = new();

    public bool HasLocal(RefID local) => _localToGlobal.ContainsKey(local);
    public bool HasGlobal(Guid global) => _globalToLocal.ContainsKey(global);

    /// <summary>SAVE: the stable GUID for a local RefID, allocating one the first time it's seen.</summary>
    public Guid Fetch(RefID local)
    {
        if (local == RefID.Null)
            throw new InvalidOperationException("Cannot fetch a null reference.");
        if (_localToGlobal.TryGetValue(local, out var existing))
            return existing;
        var global = Guid.NewGuid();
        Associate(local, global);
        return global;
    }

    /// <summary>LOAD: bind a rebuilt element's local RefID to its saved GUID and resolve any waiters.</summary>
    public void Associate(RefID local, Guid global)
    {
        if (local == RefID.Null)
            throw new InvalidOperationException("Cannot associate a null reference.");
        _globalToLocal[global] = local;
        _localToGlobal[local] = global;

        if (_pendingRequests.TryGetValue(global, out var waiters))
        {
            foreach (var waiter in waiters)
                waiter.Value = local;
            _pendingRequests.Remove(global);
        }
    }

    /// <summary>LOAD: point a reference at the GUID's element now, or queue it until that element loads.</summary>
    public void Request(Guid global, ISyncRef requestee)
    {
        if (_globalToLocal.TryGetValue(global, out var local))
        {
            requestee.Value = local;
            return;
        }
        if (!_pendingRequests.TryGetValue(global, out var list))
        {
            list = new List<ISyncRef>();
            _pendingRequests[global] = list;
        }
        list.Add(requestee);
    }

    /// <summary>Take and clear the still-unresolved requests (targets that never loaded).</summary>
    public Dictionary<Guid, List<ISyncRef>> TakeUnresolved()
    {
        var unresolved = _pendingRequests;
        _pendingRequests = new Dictionary<Guid, List<ISyncRef>>();
        return unresolved;
    }
}
