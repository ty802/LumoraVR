// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumora.Core;
using Lumora.Core.Networking.Discovery;
using Lumora.Core.Networking.LNL;
using Lumora.Core.Networking.Streams;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Session;

/// <summary>
/// Session wraps all networking for a World.
/// </summary>
public class Session : IDisposable
{
    public World World { get; private set; }
    public SessionConnectionManager Connections { get; private set; }
    public SessionSyncManager Sync { get; private set; } = null!;
    public SessionAssetTransferer AssetTransferer { get; private set; } = null!;

    /// <summary>
    /// Session metadata describing identity, settings, and state.
    /// </summary>
    public SessionMetadata Metadata { get; private set; } = null!;

    public bool IsDisposed { get; private set; }
    public TimeSpan Latency { get; set; }

    private LANAnnouncer _lanAnnouncer = null!;
    private SessionServerClient? _serverClient;
    private BackendSessionDirectoryClient? _directoryClient;

    /// <summary>
    /// Event triggered when session is disconnected.
    /// </summary>
    public event Action OnDisconnected = null!;

    /// <summary>
    /// Callback fired when a <see cref="RawFrameMessage"/> arrives, after sender
    /// validation and authority-side relay. Runs on the sync thread - keep the
    /// callback fast (push to a lock-free queue and return). The
    /// <paramref name="payload"/> memory is only valid for the duration of the
    /// invocation; copy bytes out if you need to retain them.
    /// </summary>
    public delegate void RawFrameHandler(User sender, RefID streamRefID, ushort sequence, ReadOnlyMemory<byte> payload);

    /// <summary>
    /// Subscribe to receive raw frames bound for any stream this peer can see.
    /// Audio / voice consumers typically filter by <c>streamRefID</c>.
    /// </summary>
    public event RawFrameHandler? RawFrameReceived;

    /// <summary>
    /// Gets the LAN announcer ID for filtering out own broadcasts during discovery.
    /// Returns Guid.Empty if not hosting or not announcing.
    /// </summary>
    public Guid LANAnnouncerId => _lanAnnouncer?.AnnouncerId ?? Guid.Empty;

    // Deployment endpoints are sourced from the config store (Settings) so a build can point at a real
    // server without code edits; the localhost/dev values are explicit fallbacks for local development
    // only - they are NOT a production default. An explicit setter still wins (host bootstrap / tests).
    // -xlinka
    private const string KeyServerAddress = "Network.SessionServer.Address";
    private const string KeyServerPort = "Network.SessionServer.Port";
    private const string KeyBackendUrl = "Network.BackendDirectory.Url";

    private static string? _sessionServerAddress;
    private static int? _sessionServerPort;
    private static string? _backendSessionDirectoryUrl;

    /// <summary>
    /// Session server address for public registration. Read from config key
    /// <c>Network.SessionServer.Address</c>; falls back to <c>localhost</c> for local dev.
    /// </summary>
    public static string SessionServerAddress
    {
        get => _sessionServerAddress ??= Settings.ReadValue(KeyServerAddress, "localhost");
        set => _sessionServerAddress = value;
    }

    /// <summary>
    /// Session server port for public registration. Read from config key
    /// <c>Network.SessionServer.Port</c>; falls back to <c>8000</c> for local dev.
    /// </summary>
    public static int SessionServerPort
    {
        get => _sessionServerPort ??= Settings.ReadValue(KeyServerPort, 8000);
        set => _sessionServerPort = value;
    }

    /// <summary>
    /// Backend API base URL used for authenticated public session directory registration. Read from
    /// config key <c>Network.BackendDirectory.Url</c>; falls back to a plain-http localhost URL for
    /// local dev only. Deployments must configure an https URL - do not ship the http fallback.
    /// </summary>
    public static string BackendSessionDirectoryUrl
    {
        get => _backendSessionDirectoryUrl ??= Settings.ReadValue(KeyBackendUrl, "http://localhost:5178/api");
        set => _backendSessionDirectoryUrl = value;
    }

    /// <summary>
    /// Provides the current backend auth token for host-owned session registration.
    /// </summary>
    public static Func<string?>? BackendAuthTokenProvider { get; set; }

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
        session.Metadata.StartTime = DateTime.UtcNow;
        session.Metadata.LastUpdate = DateTime.UtcNow;
        session.Metadata.HostUsername ??= Environment.UserName;
        session.Metadata.ActiveUsers = 1;

        LumoraLogger.Log($"[lnl] Creating new session '{metadata!.Name}' on port {port}");
        LumoraLogger.Log($"[lnl] Session ID: {session.Metadata.SessionId}");

        // Start listener
        if (!session.Connections.StartListener(port))
        {
            throw new Exception($"Failed to start listener on port {port}");
        }

        // Advertise a URL per transport now that the listeners are up. The LAN/direct lnl:// URLs (one per local
        // interface IP) come from the URL builder, because the LNL listener's own URI is a 0.0.0.0 wildcard; every
        // OTHER transport (e.g. Steam) contributes its concrete dialable GlobalUri (steam://<id>/...). LAN URLs go
        // first so same-subnet joiners use cheap UDP and discovery's source-IP match still finds an lnl URL. -xlinka
        var urls = new List<Uri>(SessionUrlBuilder.GetLocalSessionUrls(port, session.Metadata.SessionId));
        foreach (var listener in session.Connections.StartedListeners)
        {
            var globalUri = listener.GlobalUri;
            if (globalUri == null)
                continue;
            // The concrete LAN IPs are already added above; skip the LNL wildcard (lnl://0.0.0.0:port).
            if (string.Equals(globalUri.Scheme, "lnl", StringComparison.OrdinalIgnoreCase))
                continue;
            urls.Add(globalUri);
        }
        session.Metadata.SessionURLs = urls;

        // Create sync manager with dedicated thread
        session.Sync = new SessionSyncManager(session);
        session.Sync.Start();

        // Create asset transferer and register it with the engine
        session.AssetTransferer = new SessionAssetTransferer(session);
        if (Engine.Current is { } e)
            e.ActiveSessionTransferer = session.AssetTransferer;

        // Subscribe to connection events
        session.Connections.OnHostDisconnected += () => session.OnDisconnected?.Invoke();

        // Start LAN announcer if visibility allows
        if (metadata.Visibility == SessionVisibility.LAN ||
            metadata.Visibility == SessionVisibility.Public)
        {
            session.StartLANAnnouncer();
        }

        if (metadata.Visibility == SessionVisibility.Public)
        {
            // Detached on purpose - don't stall host creation on a public-server round-trip. The method
            // logs its own failures; the continuation catches anything that escapes so it can't go silent.
            _ = session.StartPublicServerRegistration().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    LumoraLogger.Warn($"[lnl] Session: Public server registration faulted - {t.Exception?.GetBaseException().Message}");
            }, TaskScheduler.Default);
        }

        LumoraLogger.Log($"[lnl] Session created as host with {session.Metadata.SessionURLs.Count} URLs");
        return session;
    }

    /// <summary>
    /// Join an existing session as client.
    /// </summary>
    public static Session JoinSession(World world, IEnumerable<Uri> addresses)
    {
        var session = JoinSessionAsync(world, addresses).GetAwaiter().GetResult();
        if (session == null)
        {
            throw new Exception("Failed to connect to session");
        }

        return session;
    }

    /// <summary>
    /// Join an existing session as client (async).
    /// </summary>
    public static async Task<Session?> JoinSessionAsync(World world, IEnumerable<Uri> addresses)
    {
        var session = new Session(world);

        LumoraLogger.Log($"[lnl] Joining session at {string.Join(", ", addresses)}");

        // Run the ENTIRE join - socket connect, handshake, and starting the sync threads - on a background
        // thread, never the main/update thread. Our network is polled from the engine update loop, so doing
        // ANY of this on the main thread (even the synchronous connect call) risks freezing the client mid-join.
        // ConfigureAwait(false) keeps every continuation off the main thread too. -xlinka
        return await Task.Run(async () =>
        {
            bool connected;
            try
            {
                connected = await session.Connections.ConnectToAsync(addresses).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LumoraLogger.Error($"[lnl] Failed to connect to session: {ex.Message}");
                connected = false;
            }

            if (!connected)
            {
                session.Dispose();
                return (Session?)null;
            }

            // Create sync manager with dedicated thread
            session.Sync = new SessionSyncManager(session);
            session.Sync.Start();

            // Create asset transferer and register it with the engine
            session.AssetTransferer = new SessionAssetTransferer(session);
            if (Engine.Current is { } e)
                e.ActiveSessionTransferer = session.AssetTransferer;

            LumoraLogger.Log("[lnl] Session joined as client");
            return (Session?)session;
        }).ConfigureAwait(false);
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
        _lanAnnouncer = null!;
    }

    /// <summary>
    /// Register with public session server for global discovery. Returns a Task so callers can
    /// observe faults; it runs detached (callers must not block session setup on a network round-trip).
    /// </summary>
    // NAT-host caveat: this only REGISTERS the session and its metadata with the server. The host does
    // NOT yet act on NAT punch-through: it never subscribes _serverClient.OnNATPunchSuccess /
    // OnNATIntroStarted to open the punched socket, and there is no relay server / host-side relay bridge
    // in this repo for the relay fallback. So public discovery works, but a joiner behind NAT cannot
    // currently be punched through to (or relayed to) this host - those server-side pieces are external
    // prerequisites that don't exist yet. Direct/LAN joins are unaffected. -xlinka
    private async Task StartPublicServerRegistration()
    {
        if (_serverClient != null || _directoryClient != null)
            return;

        var natRegistered = false;
        try
        {
            _serverClient = new SessionServerClient(SessionServerAddress, SessionServerPort);
            var connected = await _serverClient.ConnectAsync(
                Metadata,
                GetUserList);

            if (connected)
            {
                natRegistered = true;
                LumoraLogger.Log($"[lnl] Session registered with public server at {SessionServerAddress}:{SessionServerPort}");
            }
            else
            {
                LumoraLogger.Warn("[lnl] Session: Failed to register with public server");
                _serverClient?.Dispose();
                _serverClient = null;
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"[lnl] Session: Public server registration failed - {ex.Message}");
            _serverClient?.Dispose();
            _serverClient = null;
        }

        try
        {
            if (BackendAuthTokenProvider == null)
            {
                if (!natRegistered)
                    LumoraLogger.Warn("[lnl] Session: No backend auth token provider available for public directory registration");
                return;
            }

            var backendUrl = BackendSessionDirectoryUrl;
            // Auth tokens ride this URL; warn on plain http unless it's an explicit loopback dev target.
            if (backendUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !backendUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase)
                && !backendUrl.Contains("127.0.0.1", StringComparison.Ordinal))
            {
                LumoraLogger.Warn($"[lnl] Session: backend directory URL '{backendUrl}' is plain http; use https in deployment.");
            }

            _directoryClient = new BackendSessionDirectoryClient(
                backendUrl,
                () => BackendAuthTokenProvider?.Invoke());

            var registered = await _directoryClient.StartAsync(Metadata, GetUserList);
            if (registered)
            {
                LumoraLogger.Log($"[lnl] Session registered with backend directory at {BackendSessionDirectoryUrl}");
            }
            else
            {
                _directoryClient.Dispose();
                _directoryClient = null;
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"[lnl] Session: Backend directory registration failed - {ex.Message}");
            _directoryClient?.Dispose();
            _directoryClient = null;
        }
    }

    /// <summary>
    /// Get list of usernames in this session.
    /// </summary>
    private string[] GetUserList()
    {
        if (World == null)
            return new[] { Metadata?.HostUsername ?? "Host" };

        var users = World.GetAllUsers();
        if (users == null || users.Count == 0)
            return new[] { Metadata?.HostUsername ?? "Host" };

        var usernames = new List<string>();
        foreach (var user in users)
        {
            var name = user.UserName?.Value;
            if (!string.IsNullOrEmpty(name))
                usernames.Add(name);
        }

        return usernames.Count > 0 ? usernames.ToArray() : new[] { Metadata?.HostUsername ?? "Host" };
    }

    /// <summary>
    /// Disconnect from public session server.
    /// </summary>
    private void StopPublicServerRegistration()
    {
        _serverClient?.Dispose();
        _serverClient = null;
        _directoryClient?.Dispose();
        _directoryClient = null;
    }

    /// <summary>
    /// Poll network events for this session. Called every frame by World.Update().
    /// </summary>
    public void Poll()
    {
        Connections.Poll();
    }

    /// <summary>
    /// Send a raw frame on a stream owned by the local user (e.g. an Opus voice
    /// frame). The frame is routed through the same authority/relay path as
    /// <see cref="StreamMessage"/>, so sender-identity validation still applies. Sequence
    /// is opaque to the framework; use it for jitter buffering downstream.
    /// Returns false if the stream is not local, the payload is over the cap,
    /// or there are no eligible targets.
    /// </summary>
    public bool SendRawFrame(Stream stream, ushort sequence, ReadOnlySpan<byte> payload)
    {
        if (Sync == null || IsDisposed) return false;
        return Sync.EnqueueRawFrame(stream, sequence, payload);
    }

    /// <summary>
    /// Invoked by <see cref="SessionSyncManager"/> on the sync thread when a
    /// validated raw frame is ready to dispatch. Fires <see cref="RawFrameReceived"/>.
    /// </summary>
    internal void HandleIncomingRawFrame(User sender, RefID streamRefID, ushort sequence, ReadOnlyMemory<byte> payload)
    {
        RawFrameReceived?.Invoke(sender, streamRefID, sequence, payload);
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

        // Update session server
        _serverClient?.SendSessionUpdate(count, GetUserList());
        _directoryClient?.SendHeartbeat(count, GetUserList());
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

        // Handle LAN announcer state change
        bool shouldAnnounceLAN = visibility == SessionVisibility.LAN ||
                                 visibility == SessionVisibility.Public;

        bool wasAnnouncingLAN = oldVisibility == SessionVisibility.LAN ||
                                oldVisibility == SessionVisibility.Public;

        if (shouldAnnounceLAN && !wasAnnouncingLAN)
        {
            StartLANAnnouncer();
        }
        else if (!shouldAnnounceLAN && wasAnnouncingLAN)
        {
            StopLANAnnouncer();
        }
        else if (shouldAnnounceLAN)
        {
            _lanAnnouncer?.UpdateMetadata(Metadata);
        }

        // Handle public server registration
        bool shouldRegisterPublic = visibility == SessionVisibility.Public;
        bool wasRegisteredPublic = oldVisibility == SessionVisibility.Public;

        if (shouldRegisterPublic && !wasRegisteredPublic)
        {
            _ = StartPublicServerRegistration().ContinueWith(t =>
            {
                if (t.IsFaulted)
                    LumoraLogger.Warn($"[lnl] Session: Public server registration faulted - {t.Exception?.GetBaseException().Message}");
            }, TaskScheduler.Default);
        }
        else if (!shouldRegisterPublic && wasRegisteredPublic)
        {
            StopPublicServerRegistration();
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        LumoraLogger.Log("[lnl] Disposing session");

        // Stop LAN announcer
        StopLANAnnouncer();

        // Stop public server registration
        StopPublicServerRegistration();

        AssetTransferer?.Dispose();
        if (Engine.Current?.ActiveSessionTransferer == AssetTransferer)
            Engine.Current!.ActiveSessionTransferer = null!;

        Sync?.Dispose();
        Connections?.Dispose();

        World = null!;
        Metadata = null!;
    }
}

