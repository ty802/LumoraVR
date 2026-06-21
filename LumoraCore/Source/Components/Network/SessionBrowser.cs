// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using Lumora.Core.Networking.Discovery;
using Lumora.Core.Networking.Session;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Components.Network;

/// <summary>
/// Component for discovering and browsing available sessions on the local network.
/// </summary>
[ComponentCategory("Network")]
public class SessionBrowser : Component
{
    private LANDiscovery _discovery = null!;

    // Single aggregate of every session we know about, keyed by SessionId (lowercased). LAN discovery and our
    // own hosted session both funnel through ONE upsert path (Upsert) into this one SessionId-keyed collection -
    // so the same session can never list twice and an entry seen from two angles just merges. -xlinka
    private readonly Dictionary<string, SessionListEntry> _sessions = new();
    private readonly object _sessionsLock = new();

    // Tracks the key of our own hosted session so we can keep it fresh and drop it when we stop hosting.
    private string? _ownSessionKey;

    // Re-publish our own session into the list on a light timer (the host re-announces continuously; we mirror
    // that by re-reading live metadata instead of relying on our own filtered-out broadcast). -xlinka
    private float _ownRefreshTimer;
    private const float OwnRefreshInterval = 1f; // seconds

    /// <summary>
    /// Whether the browser is currently scanning for sessions.
    /// </summary>
    public readonly Sync<bool> IsScanning;

    /// <summary>
    /// Number of sessions currently discovered.
    /// </summary>
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

    /// <summary>
    /// Event raised when a new session is discovered.
    /// </summary>
    public event Action<SessionListEntry> OnSessionFound = null!;

    /// <summary>
    /// Event raised when a session is no longer available.
    /// </summary>
    public event Action<string> OnSessionLost = null!;

    /// <summary>
    /// Event raised when a session's info is updated.
    /// </summary>
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

    /// <summary>
    /// Start scanning for sessions on the local network.
    /// </summary>
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
        IsScanning.Value = true;

        // Show our own hosted session immediately, then OnUpdate keeps it fresh.
        _ownRefreshTimer = 0f;
        RefreshOwnHostedSession();

        LumoraLogger.Log("SessionBrowser: Started scanning for sessions");
    }

    /// <summary>
    /// Stop scanning for sessions.
    /// </summary>
    public void StopScanning()
    {
        if (_discovery == null)
            return;

        _discovery.SessionFound -= OnDiscoveryFound;
        _discovery.SessionLost -= OnDiscoveryLost;
        _discovery.SessionUpdated -= OnDiscoveryUpdated;
        _discovery.StopDiscovery();
        _discovery.Dispose();
        _discovery = null!;

        IsScanning.Value = false;

        LumoraLogger.Log("SessionBrowser: Stopped scanning");
    }

    /// <summary>
    /// Clear all discovered sessions.
    /// </summary>
    public void ClearSessions()
    {
        lock (_sessionsLock)
        {
            _sessions.Clear();
        }
        _ownSessionKey = null;
        _discovery?.ClearSessions();
    }

    /// <summary>
    /// Get all currently discovered sessions.
    /// </summary>
    public List<SessionListEntry> GetSessions()
    {
        lock (_sessionsLock)
        {
            return _sessions.Values.ToList();
        }
    }

    /// <summary>
    /// Get a specific session by ID.
    /// </summary>
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

    /// <summary>
    /// Insert or update a session in the aggregate, keyed by SessionId. Fires OnSessionFound for a brand-new
    /// session and OnSessionUpdated for one we already had, so the same session never lists twice no matter how
    /// many sources report it. -xlinka
    /// </summary>
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
            Tags = m.Tags != null ? new List<string>(m.Tags) : new List<string>()
        };
    }
}

/// <summary>
/// Entry representing a discovered session for display in UI.
/// </summary>
public class SessionListEntry
{
    /// <summary>
    /// Unique session identifier.
    /// </summary>
    public string SessionId { get; set; } = null!;

    /// <summary>
    /// Display name of the session.
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Session description.
    /// </summary>
    public string Description { get; set; } = null!;

    /// <summary>
    /// Name of the session host.
    /// </summary>
    public string HostUsername { get; set; } = null!;

    /// <summary>
    /// Current number of users in the session.
    /// </summary>
    public int ActiveUsers { get; set; }

    /// <summary>
    /// Maximum users allowed in the session.
    /// </summary>
    public int MaxUsers { get; set; }

    /// <summary>
    /// Session visibility level.
    /// </summary>
    public SessionVisibility Visibility { get; set; }

    /// <summary>
    /// URL to join this session.
    /// </summary>
    public Uri JoinUrl { get; set; } = null!;

    /// <summary>
    /// URL to session thumbnail image.
    /// </summary>
    public string ThumbnailUrl { get; set; } = null!;

    /// <summary>
    /// Base64-encoded thumbnail image data.
    /// </summary>
    public string? ThumbnailBase64 { get; set; }

    /// <summary>
    /// Tags for filtering.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Whether the session has room for more users.
    /// </summary>
    public bool HasSpace => ActiveUsers < MaxUsers;

    public override string ToString()
    {
        return $"{Name} ({ActiveUsers}/{MaxUsers}) - {HostUsername}";
    }
}
