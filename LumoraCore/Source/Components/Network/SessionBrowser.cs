// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Nexus.Discovery;
using Lumora.Core.Networking.Session;
using Lumora.Nexus.Cloud;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Network;

[ComponentCategory("Network")]
public class SessionBrowser : Component
{
    private LANDiscovery _discovery = null!;
    private BackendSessionDirectoryQuery _backendQuery = null!;

    // Single aggregate of every session we know about, keyed by SessionId (lowercased). LAN discovery, the
    // backend directory and our own hosted session all funnel through ONE upsert path (Upsert) into this one
    // SessionId-keyed collection - so the same session can never list twice and an entry seen from two angles
    // just merges. -xlinka
    private readonly Dictionary<string, SessionListEntry> _sessions = new();
    private readonly object _sessionsLock = new();

    // Keys currently held by the backend directory. The directory sends a full snapshot every poll, so this
    // is how we tell "the host stopped advertising" from "we never heard about it on LAN": anything in here
    // that falls out of a snapshot goes, anything sourced from LAN is left alone. -xlinka
    private readonly HashSet<string> _backendKeys = new();

    // Tracks the key of our own hosted session so we can keep it fresh and drop it when we stop hosting.
    private string? _ownSessionKey;

    // Re-publish our own session into the list on a light timer (the host re-announces continuously; we mirror
    // that by re-reading live metadata instead of relying on our own filtered-out broadcast). -xlinka
    private float _ownRefreshTimer;
    private const float OwnRefreshInterval = 1f; // seconds

    public readonly Sync<bool> IsScanning;

    public int SessionCount
    {
        get
        {
            lock (_sessionsLock)
            {
                return _sessions.Count;
            }
        }
    }

    public event Action<SessionListEntry> OnSessionFound = null!;

    public event Action<string> OnSessionLost = null!;

    public event Action<SessionListEntry> OnSessionUpdated = null!;

    public SessionBrowser()
    {
        IsScanning = new Sync<bool>(this, false);
    }

    public override void OnAwake()
    {
        base.OnAwake();
    }

    public override void OnDestroy()
    {
        StopScanning();
        base.OnDestroy();
    }

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);

        if (!IsScanning.Value)
            return;

        // Keep our own hosted session live in the list (user count, name changes, and removing it the moment we
        // stop hosting). This is the local-update equivalent of the host re-announcing every tick. -xlinka
        _ownRefreshTimer += delta;
        if (_ownRefreshTimer >= OwnRefreshInterval)
        {
            _ownRefreshTimer = 0f;
            RefreshOwnHostedSession();
        }
    }

    // also false when polling never started; check IsScanning alongside it
    public bool BackendUnreachable => _backendQuery?.IsUnreachable ?? true;

    // LAN discovery is already continuous; this only nudges the backend poll off its interval
    public void RequestRefresh() => _backendQuery?.RequestRefresh();

    public void StartScanning()
    {
        if (_discovery != null)
            return;

        _discovery = new LANDiscovery();
        _discovery.SessionFound += OnDiscoveryFound;
        _discovery.SessionLost += OnDiscoveryLost;
        _discovery.SessionUpdated += OnDiscoveryUpdated;

        // If we're hosting, ignore our own announcer so the discovery side doesn't surface our broadcast copy.
        // We list our own session directly from live metadata instead (RefreshOwnHostedSession). -xlinka
        Guid? ignoreId = null;
        if (World?.Session != null)
        {
            var announcerId = World.Session.LANAnnouncerId;
            if (announcerId != Guid.Empty)
            {
                ignoreId = announcerId;
                LumoraLogger.Log($"SessionBrowser: Filtering out own announcer ID: {announcerId}");
            }
        }

        _discovery.StartDiscovery(ignoreId);

        // Second source: sessions published to the backend directory by hosts we will never hear on the
        // local network. It polls on its own thread and reports through the same upsert path.
        _backendQuery = new BackendSessionDirectoryQuery(Lumora.Core.Networking.Session.Session.BackendSessionDirectoryUrl);
        _backendQuery.OnResults += OnBackendResults;
        _backendQuery.Start();

        IsScanning.Value = true;

        // Show our own hosted session immediately, then OnUpdate keeps it fresh.
        _ownRefreshTimer = 0f;
        RefreshOwnHostedSession();

        LumoraLogger.Log("SessionBrowser: Started scanning for sessions");
    }

    public void StopScanning()
    {
        if (_backendQuery != null)
        {
            _backendQuery.OnResults -= OnBackendResults;
            _backendQuery.Dispose();
            _backendQuery = null!;
        }

        if (_discovery == null)
        {
            IsScanning.Value = false;
            return;
        }

        _discovery.SessionFound -= OnDiscoveryFound;
        _discovery.SessionLost -= OnDiscoveryLost;
        _discovery.SessionUpdated -= OnDiscoveryUpdated;
        _discovery.StopDiscovery();
        _discovery.Dispose();
        _discovery = null!;

        IsScanning.Value = false;

        LumoraLogger.Log("SessionBrowser: Stopped scanning");
    }

    public void ClearSessions()
    {
        lock (_sessionsLock)
        {
            _sessions.Clear();
            _backendKeys.Clear();
        }
        _ownSessionKey = null;
        _discovery?.ClearSessions();
    }

    public List<SessionListEntry> GetSessions()
    {
        lock (_sessionsLock)
        {
            return _sessions.Values.ToList();
        }
    }

    public SessionListEntry GetSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return null!;

        lock (_sessionsLock)
        {
            return _sessions.TryGetValue(sessionId.ToLowerInvariant(), out var entry) ? entry : null!;
        }
    }

    // --- Single choke point every source funnels through -------------------------------------------------

    // Insert or update a session in the aggregate, keyed by SessionId. Fires OnSessionFound for a brand-new
    // session and OnSessionUpdated for one we already had, so the same session never lists twice no matter how
    // many sources report it. -xlinka
    private void Upsert(SessionListEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.SessionId))
            return;

        string key = entry.SessionId.ToLowerInvariant();
        bool isNew;
        lock (_sessionsLock)
        {
            isNew = !_sessions.ContainsKey(key);
            _sessions[key] = entry;

            // A session we can now see locally is no longer the directory's to remove: LAN sees it live,
            // and the directory listing lags by up to a poll.
            if (entry.Source == SessionSource.Local)
                _backendKeys.Remove(key);
        }

        if (isNew)
        {
            LumoraLogger.Log($"SessionBrowser: Found session '{entry.Name}' ({entry.ActiveUsers}/{entry.MaxUsers} users)");
            OnSessionFound?.Invoke(entry);
        }
        else
        {
            OnSessionUpdated?.Invoke(entry);
        }
    }

    private void Remove(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;

        string key = sessionId.ToLowerInvariant();
        SessionListEntry? removed = null;
        lock (_sessionsLock)
        {
            if (_sessions.TryGetValue(key, out removed))
                _sessions.Remove(key);
            _backendKeys.Remove(key);
        }

        if (removed != null)
        {
            LumoraLogger.Log($"SessionBrowser: Lost session '{removed.Name}'");
            OnSessionLost?.Invoke(sessionId);
        }
    }

    // --- LAN discovery feed ------------------------------------------------------------------------------

    private void OnDiscoveryFound(DiscoveredSession discovered) => Upsert(CreateEntry(discovered));

    private void OnDiscoveryUpdated(DiscoveredSession discovered) => Upsert(CreateEntry(discovered));

    private void OnDiscoveryLost(string sessionId) => Remove(sessionId);

    // --- Backend directory feed --------------------------------------------------------------------------

    // Fold a directory snapshot into the aggregate. Three rules, in order:
    //
    //   1. Our own hosted session never comes back in through here. RefreshOwnHostedSession lists it from
    //      live metadata; the directory copy is up to a heartbeat stale and would fight it every poll.
    //   2. A session LAN discovery can see wins. Same session, and the LAN copy carries the address the
    //      announcer was actually reached on, which beats whatever the host advertised to the internet.
    //   3. Anything the previous snapshot gave us that is missing from this one is gone. That is the only
    //      "lost" signal the directory has - hosts stop heartbeating and the backend expires them.
    //
    // An empty snapshot (backend down, offline, nothing listed) is a legitimate answer and clears every
    // backend-sourced row. It never leaves stale entries behind and never invents one. -xlinka
    private void OnBackendResults(IReadOnlyList<SessionListingDto> listings)
    {
        var seen = new HashSet<string>();
        var fresh = new List<SessionListEntry>(listings.Count);

        for (int i = 0; i < listings.Count; i++)
        {
            var entry = BuildEntry(listings[i]);
            if (entry == null)
                continue;   // no session id or no dialable URL: nothing we could join

            string key = entry.SessionId.ToLowerInvariant();
            if (key == _ownSessionKey)
                continue;

            if (!seen.Add(key))
                continue;   // duplicate id in one snapshot

            fresh.Add(entry);
        }

        var stale = new List<string>();
        var updates = new List<SessionListEntry>(fresh.Count);

        lock (_sessionsLock)
        {
            foreach (var key in _backendKeys)
            {
                if (!seen.Contains(key))
                    stale.Add(key);
            }

            for (int i = 0; i < fresh.Count; i++)
            {
                string key = fresh[i].SessionId.ToLowerInvariant();
                if (_sessions.TryGetValue(key, out var existing) && existing.Source == SessionSource.Local)
                    continue;   // LAN copy wins
                updates.Add(fresh[i]);
            }
        }

        // Upsert/Remove take the lock themselves and raise events, so they run outside it.
        for (int i = 0; i < stale.Count; i++)
            Remove(stale[i]);

        for (int i = 0; i < updates.Count; i++)
        {
            Upsert(updates[i]);
            lock (_sessionsLock)
            {
                _backendKeys.Add(updates[i].SessionId.ToLowerInvariant());
            }
        }
    }

    // --- Own hosted session feed -------------------------------------------------------------------------

    private void RefreshOwnHostedSession()
    {
        var session = World?.Session;
        var m = session?.Metadata;

        // Not hosting (or metadata not ready) - drop our own entry if we had one listed.
        if (session == null || session.LANAnnouncerId == Guid.Empty || m == null || string.IsNullOrEmpty(m.SessionId))
        {
            if (_ownSessionKey != null)
            {
                Remove(_ownSessionKey);
                _ownSessionKey = null;
            }
            return;
        }

        var entry = BuildEntry(m);
        _ownSessionKey = entry.SessionId.ToLowerInvariant();
        Upsert(entry);
    }

    // --- Entry builders ----------------------------------------------------------------------------------

    private static SessionListEntry CreateEntry(DiscoveredSession discovered)
    {
        var entry = BuildEntry(discovered.Metadata);
        entry.JoinUrl = discovered.GetConnectionUrl();
        return entry;
    }

    private static SessionListEntry BuildEntry(SessionMetadata m)
    {
        return new SessionListEntry
        {
            SessionId = m.SessionId,
            Name = m.Name,
            Description = m.Description,
            HostUsername = m.HostUsername,
            ActiveUsers = m.ActiveUsers,
            MaxUsers = m.MaxUsers,
            Visibility = m.Visibility,
            JoinUrl = (m.SessionURLs != null && m.SessionURLs.Count > 0) ? m.SessionURLs[0] : null!,
            ThumbnailUrl = m.ThumbnailUrl,
            ThumbnailBase64 = m.ThumbnailBase64,
            Tags = m.Tags != null ? new List<string>(m.Tags) : new List<string>(),
            Source = SessionSource.Local
        };
    }

    // Map a directory listing onto the same entry the LAN path produces. Every field is copied straight
    // from what the backend sent; nothing is filled in from guesses, so ThumbnailBase64 stays null (the
    // directory carries no image data) and the description stays empty if the host never set one.
    // Returns null when the listing is unusable rather than surfacing an unjoinable row. -xlinka
    private static SessionListEntry? BuildEntry(SessionListingDto dto)
    {
        if (dto == null || string.IsNullOrEmpty(dto.SessionId))
            return null;

        Uri? joinUrl = null;
        if (dto.SessionUrls != null)
        {
            foreach (var raw in dto.SessionUrls)
            {
                if (Uri.TryCreate(raw, UriKind.Absolute, out var parsed))
                {
                    joinUrl = parsed;
                    break;
                }
            }
        }

        if (joinUrl == null)
            return null;

        return new SessionListEntry
        {
            SessionId = dto.SessionId,
            Name = dto.Name ?? "",
            Description = dto.Description ?? "",
            HostUsername = dto.HostUsername ?? "",
            ActiveUsers = dto.ActiveUsers,
            MaxUsers = dto.MaxUsers,
            Visibility = dto.AccessLevel,
            JoinUrl = joinUrl,
            ThumbnailUrl = dto.ThumbnailUrl ?? "",
            ThumbnailBase64 = null,
            Tags = dto.Tags != null ? new List<string>(dto.Tags) : new List<string>(),
            Source = SessionSource.Internet
        };
    }
}

// lets a user tell a machine on their own network from one on the internet, and lets join failures
// land where they belong
public enum SessionSource
{
    // or hosted by us
    Local,

    Internet
}

public class SessionListEntry
{
    public string SessionId { get; set; } = null!;

    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public string HostUsername { get; set; } = null!;

    public int ActiveUsers { get; set; }

    public int MaxUsers { get; set; }

    public SessionVisibility Visibility { get; set; }

    public Uri JoinUrl { get; set; } = null!;

    public string ThumbnailUrl { get; set; } = null!;

    public string? ThumbnailBase64 { get; set; }

    public List<string> Tags { get; set; } = new();

    public SessionSource Source { get; set; } = SessionSource.Local;

    public bool HasSpace => ActiveUsers < MaxUsers;

    public override string ToString()
    {
        return $"{Name} ({ActiveUsers}/{MaxUsers}) - {HostUsername}";
    }
}
