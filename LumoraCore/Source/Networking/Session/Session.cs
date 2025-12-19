using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Networking.Discovery;
using Lumora.Core.Networking.LNL;
using AquaLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Session;

/// <summary>
/// Session wraps all networking for a World.
/// </summary>
public class Session : IDisposable
{
    public World World { get; private set; }
    public SessionConnectionManager Connections { get; private set; }
    public SessionSyncManager Sync { get; private set; }

    /// <summary>
    /// Session metadata describing identity, settings, and state.
    /// </summary>
    public SessionMetadata Metadata { get; private set; }

    public bool IsDisposed { get; private set; }
    public TimeSpan Latency { get; set; }

    private LANAnnouncer _lanAnnouncer;

    private Session(World world)
    {
        World = world;
        Connections = new SessionConnectionManager(this);
    }

    /// <summary>
    /// Create a new session as host (authority) with default metadata.
    /// </summary>
    public static Session NewSession(World world, ushort port = 7777)
    {
        var metadata = new SessionMetadata
        {
            Name = world.Name,
            Visibility = SessionVisibility.Private,
            MaxUsers = 16
        };

        return NewSession(world, port, metadata);
    }

    /// <summary>
    /// Create a new session as host (authority) with specified metadata.
    /// </summary>
    public static Session NewSession(World world, ushort port, SessionMetadata metadata)
    {
        var session = new Session(world);

        // Initialize metadata
        session.Metadata = metadata ?? new SessionMetadata();
        session.Metadata.SessionId = SessionIdentifier.Generate();
        session.Metadata.SessionURLs = SessionUrlBuilder.GetLocalSessionUrls(port, session.Metadata.SessionId);
        session.Metadata.StartTime = DateTime.UtcNow;
        session.Metadata.LastUpdate = DateTime.UtcNow;

        AquaLogger.Log($"Creating new session '{metadata.Name}' on port {port}");
        AquaLogger.Log($"Session ID: {session.Metadata.SessionId}");

        // Start listener
        if (!session.Connections.StartListener(port))
        {
            throw new Exception($"Failed to start listener on port {port}");
        }

        // Create sync manager with dedicated thread
        session.Sync = new SessionSyncManager(session);
        session.Sync.Start();

        // Start LAN announcer if visibility allows
        if (metadata.Visibility == SessionVisibility.LAN ||
            metadata.Visibility == SessionVisibility.Public)
        {
            session.StartLANAnnouncer();
        }

        AquaLogger.Log($"Session created as host with {session.Metadata.SessionURLs.Count} URLs");
        return session;
    }

    /// <summary>
    /// Join an existing session as client.
    /// </summary>
    public static Session JoinSession(World world, IEnumerable<Uri> addresses)
    {
        var session = new Session(world);

        AquaLogger.Log($"Joining session at {string.Join(", ", addresses)}");

        // Connect to host
        var connected = session.Connections.ConnectToAsync(addresses).Result;
        if (!connected)
        {
            session.Dispose();
            throw new Exception("Failed to connect to session");
        }

        // Create sync manager with dedicated thread
        session.Sync = new SessionSyncManager(session);
        session.Sync.Start();

        AquaLogger.Log("Session joined as client");
        return session;
    }

    /// <summary>
    /// Start the LAN announcer to broadcast session availability.
    /// </summary>
    private void StartLANAnnouncer()
    {
        if (_lanAnnouncer != null)
            return;

        _lanAnnouncer = new LANAnnouncer();
        _lanAnnouncer.StartAnnouncing(Metadata);
    }

    /// <summary>
    /// Stop the LAN announcer.
    /// </summary>
    private void StopLANAnnouncer()
    {
        _lanAnnouncer?.StopAnnouncing();
        _lanAnnouncer?.Dispose();
        _lanAnnouncer = null;
    }

    /// <summary>
    /// Update session metadata with a modifier action.
    /// </summary>
    /// <param name="update">Action to modify the metadata</param>
    public void UpdateMetadata(Action<SessionMetadata> update)
    {
        if (Metadata == null)
            return;

        update?.Invoke(Metadata);
        Metadata.LastUpdate = DateTime.UtcNow;

        // Update the announcer with new metadata
        _lanAnnouncer?.UpdateMetadata(Metadata);
    }

    /// <summary>
    /// Called when user count changes to update metadata.
    /// </summary>
    internal void OnUserCountChanged(int count)
    {
        if (Metadata == null)
            return;

        Metadata.ActiveUsers = count;
        Metadata.LastUpdate = DateTime.UtcNow;

        // Update announcer
        _lanAnnouncer?.UpdateMetadata(Metadata);
    }

    /// <summary>
    /// Change the session visibility and start/stop announcer as needed.
    /// </summary>
    public void SetVisibility(SessionVisibility visibility)
    {
        if (Metadata == null)
            return;

        var oldVisibility = Metadata.Visibility;
        Metadata.Visibility = visibility;
        Metadata.LastUpdate = DateTime.UtcNow;

        // Handle announcer state change
        bool shouldAnnounce = visibility == SessionVisibility.LAN ||
                              visibility == SessionVisibility.Public;

        bool wasAnnouncing = oldVisibility == SessionVisibility.LAN ||
                             oldVisibility == SessionVisibility.Public;

        if (shouldAnnounce && !wasAnnouncing)
        {
            StartLANAnnouncer();
        }
        else if (!shouldAnnounce && wasAnnouncing)
        {
            StopLANAnnouncer();
        }
        else if (shouldAnnounce)
        {
            _lanAnnouncer?.UpdateMetadata(Metadata);
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        AquaLogger.Log("Disposing session");

        // Stop LAN announcer
        StopLANAnnouncer();

        Sync?.Dispose();
        Connections?.Dispose();

        World = null;
        Metadata = null;
    }
}
