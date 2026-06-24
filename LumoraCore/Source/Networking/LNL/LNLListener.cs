// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.LNL;

/// <summary>
/// LiteNetLib server listener. Listens for incoming connections and creates
/// LNLPeer instances.
/// </summary>
public class LNLListener : IListener, INetEventListener
{
    private readonly NetManager _server;
    private readonly string _appId;
    private readonly ushort _port;
    private readonly IPAddress _bindIP;
    private readonly Dictionary<NetPeer, LNLPeer> _peers = new();
    private readonly string _sessionId;
    private static readonly TimeSpan CryptoHandshakeTimeout = TimeSpan.FromSeconds(10);

    public bool IsInitialized { get; private set; }
    public bool IsActive => IsInitialized && _server != null && _server.IsRunning;
    public int PeerCount => _peers.Count;

    public Uri LocalUri { get; }
    public Uri GlobalUri { get; }

    public event Action<IConnection> PeerConnected = null!;
    public event Action<IConnection> PeerDisconnected = null!;

    public LNLListener(string appId, ushort port, IPAddress bindIP, string sessionId = null!)
    {
        _appId = appId;
        _port = port;
        _bindIP = bindIP;
        _sessionId = sessionId ?? string.Empty;

        // For UDP transports the local URI dials this exact bind address; the
        // global URI uses the wildcard host so a directory listing can replace
        // it with the host's externally-resolvable address at publish time. - xlinka
        var bindHost = bindIP.Equals(IPAddress.Any) ? "0.0.0.0" : bindIP.ToString();
        LocalUri = new Uri($"lnl://{bindHost}:{port}/{_sessionId}");
        GlobalUri = new Uri($"lnl://0.0.0.0:{port}/{_sessionId}");

        _server = new NetManager(this);
        _server.UpdateTime = 15;
        _server.UseNativeSockets = false;
        _server.UnsyncedEvents = true;
        _server.DisconnectTimeout = 30000;
        _server.ChannelsCount = 2;
        _server.AutoRecycle = false;
        _server.UnconnectedMessagesEnabled = true;
        _server.EnableStatistics = true;

        try
        {
            IsInitialized = _server.Start(bindIP, IPAddress.IPv6Any, port);

            if (IsInitialized)
            {
                LumoraLogger.Log($"[lnl] LNL Listener started on {bindIP}:{port}");
            }
            else
            {
                LumoraLogger.Error($"[lnl] Failed to start LNL Listener on {bindIP}:{port}");
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"[lnl] Exception starting LNL Listener: {ex.Message}");
            IsInitialized = false;
        }
    }

    /// <summary>
    /// Poll for network events. Must be called every frame.
    /// </summary>
    public void Poll()
    {
        _server?.PollEvents();
        ReapCryptoHandshakeTimeouts();
    }

    /// <summary>
    /// Stop the listener and disconnect all peers.
    /// </summary>
    public void Stop()
    {
        if (_server != null && _server.IsRunning)
        {
            foreach (var peer in _peers.Values)
            {
                peer.Close();
            }
            _peers.Clear();

            _server.Stop();
            LumoraLogger.Log($"[lnl] LNL Listener stopped on {_bindIP}:{_port}");
        }
    }

    public void Close() => Stop();

    public void Dispose()
    {
        Stop();
    }

    // INetEventListener implementation

    public void OnPeerConnected(NetPeer peer)
    {
        LumoraLogger.Log($"[lnl] Peer connected: {peer.Address}:{peer.Port}");

        var lnlPeer = new LNLPeer(_server, peer);
        lnlPeer.CryptoEstablished += OnPeerCryptoEstablished;
        _peers[peer] = lnlPeer;
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        LumoraLogger.Log($"[lnl] Peer disconnected: {peer.Address}:{peer.Port} - {disconnectInfo.Reason}");

        if (_peers.TryGetValue(peer, out var lnlPeer))
        {
            _peers.Remove(peer);
            lnlPeer.CryptoEstablished -= OnPeerCryptoEstablished;
            lnlPeer.InformClosed();
            PeerDisconnected?.Invoke(lnlPeer);
        }
    }

    public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
    {
        LumoraLogger.Error($"[lnl] Network error: {socketError} at {endPoint}");
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        if (_peers.TryGetValue(peer, out var lnlPeer))
        {
            int length = reader.AvailableBytes;
            byte[] data = new byte[length];
            reader.GetBytes(data, length);

            lnlPeer.InformOfNewData(data, length);
        }

        reader.Recycle();
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        // Could be used for discovery/punchthrough
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        // Can be used to update peer statistics
    }

    public void OnConnectionRequest(ConnectionRequest request)
    {
        // Read the key once (GetString consumes the reader). If this log never appears for a client that's
        // reporting ConnectionFailed, the host never even received their connect packet -> it's reachability
        // (wrong address/port, firewall, AP isolation), not an app-id problem. -xlinka
        string received;
        try { received = request.Data.GetString(); }
        catch { received = "<unreadable>"; }

        // Accept connection if app ID matches
        if (received == _appId)
        {
            request.Accept();
            LumoraLogger.Log($"[lnl] Accepted connection request from {request.RemoteEndPoint}");
        }
        else
        {
            request.Reject();
            // Log BOTH sides so an app-id/version mismatch (different builds) is unmistakable. -xlinka
            LumoraLogger.Warn($"[lnl] Rejected connection request from {request.RemoteEndPoint} - app ID mismatch (got '{received}', expected '{_appId}')");
        }
    }

    private void OnPeerCryptoEstablished(LNLPeer peer)
    {
        peer.CryptoEstablished -= OnPeerCryptoEstablished;
        PeerConnected?.Invoke(peer);
    }

    private void ReapCryptoHandshakeTimeouts()
    {
        List<LNLPeer>? expired = null;
        var now = DateTime.UtcNow;
        foreach (var peer in _peers.Values)
        {
            if (!peer.IsCryptoEstablished && now - peer.CreatedUtc > CryptoHandshakeTimeout)
            {
                expired ??= new List<LNLPeer>();
                expired.Add(peer);
            }
        }

        if (expired == null) return;
        foreach (var peer in expired)
        {
            LumoraLogger.Warn($"[lnl] Closing {peer.Identifier}: LNL crypto handshake timed out");
            peer.Close();
        }
    }
}
