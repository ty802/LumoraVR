// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using LumoraLogger = Lumora.Nexus.Diagnostics.NexusLog;
using System.Threading;

namespace Lumora.Nexus.Transport.LNL;

public class LNLConnection : IConnection, INetEventListener
{
    private readonly NetManager _client;
    private NetPeer _peer = null!;
    private readonly string _appId;
    private readonly LNLCryptoSession _crypto = new(isClient: true);
    private readonly object _lockObj = new();
    private bool _hasClosed;

    public bool IsOpen { get; private set; }
    public string FailReason { get; private set; } = null!;
    public IPAddress IP => (_peer?.Address) ?? null!;
    public Uri Address { get; private set; }
    public string Identifier { get; private set; }
    public ulong ReceivedBytes { get; private set; }
    private long _sentBytes;
    public ulong SentBytes => (ulong)Interlocked.Read(ref _sentBytes);
    public int Ping => _peer?.Ping ?? -1;
    public long PacketsSent => _peer?.Statistics.PacketsSent ?? 0;
    public long PacketsLost => _peer?.Statistics.PacketLoss ?? 0;
    public int QueuedPackets => LNLLanes.Queued(_peer);
    public long PendingSendBytes => (long)LNLLanes.Queued(_peer) * LNLLanes.PacketBytes;
    public bool IsEncrypted => _crypto.IsEstablished;
    public string TransportName => "LNL";

    public event Action<IConnection> Closed = null!;
    public event Action<IConnection> Connected = null!;
    public event Action<IConnection> ConnectionFailed = null!;
    public event Action<byte[], int> DataReceived = null!;

    public LNLConnection(string appId, Uri address, bool dontRoute, IPAddress bindIP)
    {
        _appId = appId;
        Address = address;
        Identifier = address.Host;

        _client = new NetManager(this);
        // Channel 0 foreground, 1 background, the rest bulk lanes.
        LNLLanes.Configure(_client);
        _client.UseNativeSockets = false;
        _client.UnsyncedEvents = true;
        _client.DisconnectTimeout = LNLLanes.SilenceTimeoutMs;
        _client.AutoRecycle = false;
        _client.UnconnectedMessagesEnabled = true;
        _client.EnableStatistics = true;
        _client.DontRoute = dontRoute;

        bool startSuccess = _client.Start(bindIP, IPAddress.IPv6Any, 0);
        if (!startSuccess)
        {
            throw new Exception($"Failed to start LNL Connection for {address}");
        }

        LumoraLogger.Debug($"[lnl] LNL Connection created for {address} on local port {_client.LocalPort}");
    }

    public void Connect(Action<string> statusCallback)
    {
        statusCallback?.Invoke($"Connecting to {Address.Host}:{Address.Port}");

        LumoraLogger.Debug($"[lnl] Establishing connection to {Address}");
        _peer = _client.Connect(Address.Host, Address.Port, _appId);

        if (_peer == null)
        {
            FailReason = "Failed to create connection peer";
            ConnectionFailed?.Invoke(this);
        }
    }

    public void Close()
    {
        if (_peer != null)
        {
            _client.DisconnectPeer(_peer);
            _peer = null!;
        }
        _client.Stop();
        InformClosed();
    }

    private bool _loggedNotConnected;

    public void Send(byte[] data, int length, bool reliable, bool background)
    {
        if (_peer == null || _peer.ConnectionState != ConnectionState.Connected)
        {
            // Expected while the peer is gone/disconnecting (e.g. a user left - LNL only times the peer out
            // after DisconnectTimeout, ~30s, so callers keep trying to stream to it until then). Sending to a
            // closing connection is a no-op, not an error worth a Warn every single frame. Note it ONCE per
            // disconnect at Debug; the Closed event is the real disconnect signal. -xlinka
            if (!_loggedNotConnected)
            {
                _loggedNotConnected = true;
                LumoraLogger.Debug("[lnl] Send skipped - peer not connected (teardown/disconnect)");
            }
            return;
        }
        _loggedNotConnected = false;

        byte[] encrypted;
        try
        {
            encrypted = _crypto.Encrypt(data, length);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"[lnl] Send failed before LNL crypto was ready ({ex.Message})");
            return;
        }

        SendRaw(encrypted, encrypted.Length, reliable, background);
    }

    private readonly LNLLanes _lanes = new();

    public void SendBulk(byte[] data, int length)
    {
        if (_peer == null || _peer.ConnectionState != ConnectionState.Connected)
            return;
        byte[] encrypted;
        try
        {
            encrypted = _crypto.Encrypt(data, length);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"[lnl] Bulk send failed before LNL crypto was ready ({ex.Message})");
            return;
        }
        Interlocked.Add(ref _sentBytes, encrypted.Length);
        _lanes.Send(_peer, encrypted, encrypted.Length);
        _client.TriggerUpdate();
    }

    private void SendRaw(byte[] data, int length, bool reliable, bool background)
    {
        Interlocked.Add(ref _sentBytes, length);
        DeliveryMethod method = reliable
            ? DeliveryMethod.ReliableOrdered
            : DeliveryMethod.Sequenced;

        // 0 = foreground, 1 = background
        byte channel = (byte)(background ? 1 : 0);

        // If sequenced packet is too large, use reliable unordered
        if (method == DeliveryMethod.Sequenced &&
            _peer.GetMaxSinglePacketSize(method) < length)
        {
            method = DeliveryMethod.ReliableUnordered;
        }

        _peer.Send(data, 0, length, channel, method);
        // Out now rather than at the next tick, up to 15 ms later.
        _client.TriggerUpdate();
    }

    // Must be called every frame.
    public void Poll()
    {
        _client?.PollEvents();
    }

    private void InformClosed()
    {
        lock (_lockObj)
        {
            if (_hasClosed) return;
            _hasClosed = true;
            IsOpen = false;
        }

        Closed?.Invoke(this);
    }

    public void Dispose()
    {
        Close();
        _crypto.Dispose();
    }

    // INetEventListener implementation

    public void OnPeerConnected(NetPeer peer)
    {
        LumoraLogger.Debug($"[lnl] Transport connected to {peer.Address}:{peer.Port}; starting crypto handshake");
        _peer = peer;
        var hello = _crypto.CreateClientHello();
        SendRaw(hello, hello.Length, reliable: true, background: false);
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        // Pull out everything LNL hands us, not just the bare reason. socketErrorCode explains OS-level failures,
        // and AdditionalData carries any human-readable reason the host attached when it rejected us. -xlinka
        var extra = "";
        if (disconnectInfo.SocketErrorCode != System.Net.Sockets.SocketError.Success)
            extra += $", socketError={disconnectInfo.SocketErrorCode}";
        if (disconnectInfo.AdditionalData != null && disconnectInfo.AdditionalData.AvailableBytes > 0)
        {
            try { extra += $", hostReason='{disconnectInfo.AdditionalData.GetString()}'"; }
            catch { extra += $", +{disconnectInfo.AdditionalData.AvailableBytes}B rejectData"; }
        }

        // Spell out what the LNL reason actually MEANS so the log explains itself instead of just "ConnectionFailed".
        var hint = disconnectInfo.Reason switch
        {
            DisconnectReason.ConnectionFailed => "host never answered the connect request - wrong address/port, host not listening there, or a firewall is dropping the UDP. NOT a version mismatch (that would be ConnectionRejected/InvalidProtocol)",
            DisconnectReason.ConnectionRejected => "host actively rejected the connect request - app-id mismatch (mismatched build/protocol versions) or the host declined",
            DisconnectReason.InvalidProtocol => "LNL connect-key/protocol mismatch - almost always mismatched build versions",
            DisconnectReason.Timeout => "connection went silent past the timeout - network dropped or the peer froze",
            DisconnectReason.HostUnreachable => "OS reported host unreachable - routing/firewall",
            DisconnectReason.NetworkUnreachable => "OS reported network unreachable - wrong subnet/interface",
            DisconnectReason.RemoteConnectionClose => "the remote peer closed the connection deliberately (e.g. join rejected)",
            DisconnectReason.DisconnectPeerCalled => "we closed this connection locally",
            _ => "see LiteNetLib DisconnectReason"
        };

        LumoraLogger.Warn($"[lnl] Disconnected from {peer.Address}:{peer.Port} - {disconnectInfo.Reason}{extra} ({hint})");
        FailReason = disconnectInfo.Reason.ToString();
        InformClosed();
    }

    public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
    {
        LumoraLogger.Error($"[lnl] Network error: {socketError} at {endPoint}");
        FailReason = socketError.ToString();
        ConnectionFailed?.Invoke(this);
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
    {
        int length = reader.AvailableBytes;
        byte[] data = new byte[length];
        reader.GetBytes(data, length);

        // The ack for what just arrived goes now, not at the next tick: a reliable window only moves on acks.
        if (deliveryMethod is DeliveryMethod.ReliableOrdered or DeliveryMethod.ReliableUnordered)
            _client.TriggerUpdate();

        ReceivedBytes += (ulong)length;
        bool wasEstablished = _crypto.IsEstablished;
        if (!_crypto.TryHandleIncoming(data, length, out var plaintext, out var response))
        {
            FailReason = "LNL crypto handshake failed";
            LumoraLogger.Warn($"[lnl] Closing {Identifier}: {FailReason}");
            Close();
            reader.Recycle();
            return;
        }

        if (response != null)
            SendRaw(response, response.Length, reliable: true, background: false);

        if (!wasEstablished && _crypto.IsEstablished)
        {
            LumoraLogger.Debug($"[lnl] Crypto established with {peer.Address}:{peer.Port}");
            IsOpen = true;
            Connected?.Invoke(this);
        }

        if (plaintext != null)
            DataReceived?.Invoke(plaintext, plaintext.Length);

        reader.Recycle();
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        // Not used for client connections
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        // Can be used to update ping statistics
    }

    public void OnMessageDelivered(NetPeer peer, object userData) => LNLLanes.Delivered(userData);

    public void OnConnectionRequest(ConnectionRequest request)
    {
        // Not used for client connections
    }
}
