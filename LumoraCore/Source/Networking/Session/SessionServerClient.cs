// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Session;

/// <summary>
/// Client for connecting to the public session server for NAT traversal and discovery.
/// Uses LiteNetLib to communicate with the natserver.
/// </summary>
public class SessionServerClient : IDisposable, INetEventListener, INatPunchListener
{
    // Protocol opcodes.
    private const byte OP_HANDSHAKE = 0x01;
    private const byte OP_NAT_INTRO_NOTIFY = 0x02;
    private const byte OP_RELAY_REQUEST = 0x03;
    private const byte OP_RELAY_CONFIRM = 0x04;
    private const byte OP_RELAY_PACKET = 0x05;
    private const byte OP_RELAY_RECEIVE = 0x06;
    private const byte OP_SESSION_UPDATE = 0x07;

    // Relay wire format (client side). Strings are length-prefixed (the writer's Put(string)); payload is
    // raw bytes to the end of the packet (the writer's Put(byte[]) writes NO length prefix in this transport
    // version, so it is drained with GetRemainingBytes). The relay server is expected to bridge these
    // symmetrically; no such server exists in this repo yet (see RelayNetworkManager / Session host notes),
    // so this client is the source of truth for the format.
    //   client -> server  OP_RELAY_REQUEST : [byte op][string targetSessionId]
    //   server -> client  OP_RELAY_CONFIRM : [byte op][string targetSessionId]
    //   client -> server  OP_RELAY_PACKET  : [byte op][string targetSessionId][raw bytes... payload]
    //   server -> client  OP_RELAY_RECEIVE : [byte op][string sourceSessionId][raw bytes... payload]
    // sourceSessionId on OP_RELAY_RECEIVE mirrors targetSessionId on OP_RELAY_PACKET (same string type),
    // so a server that echoes the peer id round-trips without a type mismatch. -xlinka

    // Dev-only fallbacks, used only when a caller constructs without an explicit endpoint. Real callers
    // pass Session.SessionServerAddress / SessionServerPort, which come from config (Network.SessionServer.*).
    // These are NOT a production default. -xlinka
    public const string DEFAULT_SERVER = "127.0.0.1";
    public const int DEFAULT_PORT = 8000;

    // Connection state
    private NetManager _client = null!;
    private NetPeer _serverPeer = null!;
    private CancellationTokenSource _cts = null!;
    private Task _pollTask = null!;
    private SessionMetadata _pendingMetadata = null!;
    private Func<string[]> _getUserListFunc = null!;

    // Session state
    public string ServerSecret { get; private set; } = null!;
    public string AssignedSessionId { get; private set; } = null!;
    public bool IsConnected => _serverPeer != null && _serverPeer.ConnectionState == ConnectionState.Connected;
    public bool IsReady { get; private set; }

    // Events
    public event Action OnConnected = null!;
    public event Action<string> OnDisconnected = null!;
    public event Action OnNATIntroStarted = null!;
    public event Action<string> OnRelayConfirmed = null!;
    public event Action<byte[]> OnRelayDataReceived = null!;

    /// <summary>
    /// Event raised when NAT punch succeeds and we have an endpoint to connect to.
    /// </summary>
    public event Action<IPEndPoint> OnNATPunchSuccess = null!;

    private readonly string _serverAddress;
    private readonly int _serverPort;

    public SessionServerClient(string serverAddress = DEFAULT_SERVER, int serverPort = DEFAULT_PORT)
    {
        _serverAddress = serverAddress;
        _serverPort = serverPort;
    }

    /// <summary>
    /// Connect to the session server as a host (registers session).
    /// </summary>
    public async Task<bool> ConnectAsync(SessionMetadata metadata, Func<string[]> getUserListFunc = null!)
    {
        if (IsConnected)
            return true;

        _pendingMetadata = metadata;
        _getUserListFunc = getUserListFunc;

        try
        {
            _client = new NetManager(this)
            {
                NatPunchEnabled = true,
                AutoRecycle = true
            };
            _client.NatPunchModule.Init(this);

            if (!_client.Start())
            {
                LumoraLogger.Error("SessionServerClient: Failed to start NetManager");
                return false;
            }

            _cts = new CancellationTokenSource();

            // Start poll loop
            _pollTask = Task.Run(PollLoop);

            // Connect to server with "Public" connection type
            var writer = new NetDataWriter();
            writer.Put("Public");
            _serverPeer = _client.Connect(_serverAddress, _serverPort, writer);

            if (_serverPeer == null)
            {
                LumoraLogger.Error("SessionServerClient: Failed to initiate connection");
                Disconnect();
                return false;
            }

            // Wait for connection
            var startTime = DateTime.UtcNow;
            while (!IsConnected && (DateTime.UtcNow - startTime).TotalSeconds < 5)
            {
                await Task.Delay(50);
            }

            if (!IsConnected)
            {
                LumoraLogger.Warn("SessionServerClient: Connection timeout");
                Disconnect();
                return false;
            }

            LumoraLogger.Log($"SessionServerClient: Connected to {_serverAddress}:{_serverPort}");
            return true;
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"SessionServerClient: Connection failed - {ex.Message}");
            Disconnect();
            return false;
        }
    }

    /// <summary>
    /// Connect to session server as a client and request NAT punch to join a session.
    /// </summary>
    public async Task<bool> RequestNATPunchAsync(string targetSessionId, string? joinTicket = null)
    {
        if (_client != null && _client.IsRunning)
        {
            // Already running, just send the NAT punch request
            SendNATPunchRequest(targetSessionId, joinTicket);
            return true;
        }

        try
        {
            _client = new NetManager(this)
            {
                NatPunchEnabled = true,
                AutoRecycle = true
            };
            _client.NatPunchModule.Init(this);

            if (!_client.Start())
            {
                LumoraLogger.Error("SessionServerClient: Failed to start NetManager for NAT punch");
                return false;
            }

            _cts = new CancellationTokenSource();

            // Start poll loop
            _pollTask = Task.Run(PollLoop);

            // Send NAT punch request
            SendNATPunchRequest(targetSessionId, joinTicket);

            LumoraLogger.Log($"SessionServerClient: NAT punch request sent for session {targetSessionId}");
            return true;
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"SessionServerClient: NAT punch request failed - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Connect to the session server as a plain relay client (no session registration).
    /// Used by the joining peer so it can request a relay and exchange relay packets.
    /// </summary>
    public async Task<bool> ConnectForRelayAsync()
    {
        if (IsConnected)
            return true;

        try
        {
            _client = new NetManager(this)
            {
                NatPunchEnabled = true,
                AutoRecycle = true
            };
            _client.NatPunchModule.Init(this);

            if (!_client.Start())
            {
                LumoraLogger.Error("SessionServerClient: Failed to start NetManager for relay");
                return false;
            }

            _cts = new CancellationTokenSource();
            _pollTask = Task.Run(PollLoop);

            // Connect as a relay client; no metadata is sent, so the OP_HANDSHAKE
            // reply's SendSessionReady() no-ops (it requires pending metadata).
            var writer = new NetDataWriter();
            writer.Put("Relay");
            _serverPeer = _client.Connect(_serverAddress, _serverPort, writer);

            if (_serverPeer == null)
            {
                LumoraLogger.Error("SessionServerClient: Failed to initiate relay connection");
                Disconnect();
                return false;
            }

            var startTime = DateTime.UtcNow;
            while (!IsConnected && (DateTime.UtcNow - startTime).TotalSeconds < 5)
                await Task.Delay(50);

            if (!IsConnected)
            {
                LumoraLogger.Warn("SessionServerClient: Relay connection timeout");
                Disconnect();
                return false;
            }

            LumoraLogger.Log($"SessionServerClient: Relay-connected to {_serverAddress}:{_serverPort}");
            return true;
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"SessionServerClient: Relay connection failed - {ex.Message}");
            Disconnect();
            return false;
        }
    }

    /// <summary>
    /// Send NAT punch request to join a session.
    /// </summary>
    private void SendNATPunchRequest(string targetSessionId, string? joinTicket)
    {
        // Send NAT punch request with client token
        string token = string.IsNullOrWhiteSpace(joinTicket)
            ? $"client:{targetSessionId}"
            : $"client:{targetSessionId}:{joinTicket}";
        _client.NatPunchModule.SendNatIntroduceRequest(_serverAddress, _serverPort, token);
        LumoraLogger.Log($"SessionServerClient: Sent NAT punch request for session {targetSessionId}");
    }

    /// <summary>
    /// Respond to NAT introduction as host (called when server notifies us of incoming client).
    /// </summary>
    private void RespondToNATIntro()
    {
        if (string.IsNullOrEmpty(ServerSecret))
        {
            LumoraLogger.Warn("SessionServerClient: Cannot respond to NAT intro - no server secret");
            return;
        }

        // Send NAT punch response with server token
        string token = $"server:{ServerSecret}";
        _client.NatPunchModule.SendNatIntroduceRequest(_serverAddress, _serverPort, token);
        LumoraLogger.Log($"SessionServerClient: Sent NAT punch response with token: {token}");
    }

    private async Task PollLoop()
    {
        while (_cts != null && !_cts.Token.IsCancellationRequested)
        {
            try
            {
                _client?.PollEvents();
                _client?.NatPunchModule.PollEvents();
                await Task.Delay(15);
            }
            catch (Exception ex)
            {
                if (_cts != null && !_cts.Token.IsCancellationRequested)
                {
                    LumoraLogger.Warn($"SessionServerClient: Poll error - {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Disconnect from the session server.
    /// </summary>
    public void Disconnect()
    {
        IsReady = false;

        _cts?.Cancel();

        if (_serverPeer != null)
        {
            _serverPeer.Disconnect();
            _serverPeer = null!;
        }

        _client?.Stop();
        _client = null!;

        OnDisconnected?.Invoke("Disconnected");
        LumoraLogger.Log("SessionServerClient: Disconnected");
    }

    /// <summary>
    /// Request relay to another session.
    /// </summary>
    public void RequestRelay(string targetSessionId)
    {
        if (!IsConnected || _serverPeer == null)
            return;

        var writer = new NetDataWriter();
        writer.Put(OP_RELAY_REQUEST);
        writer.Put(targetSessionId);
        _serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Send a relay packet to a target session.
    /// </summary>
    public void SendRelayPacket(string targetSessionId, byte[] data)
    {
        if (!IsConnected || _serverPeer == null)
            return;

        var writer = new NetDataWriter();
        writer.Put(OP_RELAY_PACKET);
        writer.Put(targetSessionId);
        writer.Put(data); // raw bytes to end (no length prefix); drained with GetRemainingBytes on receive
        _serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Send session ready message with full metadata.
    /// </summary>
    private void SendSessionReady()
    {
        if (_serverPeer == null || _pendingMetadata == null)
            return;

        var metadata = _pendingMetadata;
        var userList = _getUserListFunc?.Invoke() ?? Array.Empty<string>();

        var writer = new NetDataWriter();
        writer.Put(OP_HANDSHAKE);
        writer.Put(metadata.Name ?? "Unnamed Session");
        writer.Put(metadata.SessionId ?? "");
        writer.Put(metadata.HostUsername ?? "Unknown");
        writer.Put(metadata.ActiveUsers);
        writer.Put(metadata.MaxUsers);
        writer.Put(metadata.IsHeadless);
        writer.Put(metadata.VersionHash ?? "1.0.0");

        // Write tags
        var tags = metadata.Tags ?? new System.Collections.Generic.List<string>();
        writer.Put(tags.Count);
        foreach (var tag in tags)
            writer.Put(tag ?? "");

        // Write user list
        writer.Put(userList.Length);
        foreach (var user in userList)
            writer.Put(user ?? "");

        _serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);

        IsReady = true;
        LumoraLogger.Log($"SessionServerClient: Sent session ready - Name: {metadata.Name}, SessionId: {metadata.SessionId}");
    }

    /// <summary>
    /// Send session update (user count changed, etc.).
    /// </summary>
    public void SendSessionUpdate(int activeUsers, string[] userList)
    {
        if (_serverPeer == null || !IsReady)
            return;

        var writer = new NetDataWriter();
        writer.Put(OP_SESSION_UPDATE);
        writer.Put(activeUsers);
        writer.Put(userList?.Length ?? 0);
        if (userList != null)
        {
            foreach (var user in userList)
                writer.Put(user ?? "");
        }
        _serverPeer.Send(writer, DeliveryMethod.ReliableOrdered);

        LumoraLogger.Log($"SessionServerClient: Sent session update - Users: {activeUsers}");
    }

    // INetEventListener implementation

    public void OnPeerConnected(NetPeer peer)
    {
        LumoraLogger.Log($"SessionServerClient: Connected to server");
        OnConnected?.Invoke();
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        LumoraLogger.Log($"SessionServerClient: Disconnected - {disconnectInfo.Reason}");
        _serverPeer = null!;
        IsReady = false;
        OnDisconnected?.Invoke(disconnectInfo.Reason.ToString());
    }

    public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError socketError)
    {
        LumoraLogger.Warn($"SessionServerClient: Network error - {socketError}");
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod)
    {
        try
        {
            byte opcode = reader.GetByte();

            switch (opcode)
            {
                case OP_HANDSHAKE:
                    // Server sends secret after connection
                    ServerSecret = reader.GetString();
                    LumoraLogger.Log($"SessionServerClient: Received server secret");

                    // Now send our session ready message
                    SendSessionReady();
                    break;

                case OP_NAT_INTRO_NOTIFY:
                    LumoraLogger.Log("SessionServerClient: NAT introduction requested - responding");
                    // Respond to the NAT intro request
                    RespondToNATIntro();
                    OnNATIntroStarted?.Invoke();
                    break;

                case OP_RELAY_CONFIRM:
                    var targetId = reader.GetString();
                    LumoraLogger.Log($"SessionServerClient: Relay confirmed for {targetId}");
                    OnRelayConfirmed?.Invoke(targetId);
                    break;

                case OP_RELAY_RECEIVE:
                    // Source id is a length-prefixed string, mirroring the target id we write in
                    // SendRelayPacket - NOT an int (the old GetInt() here desynced the reader and ate
                    // the first 4 payload bytes). The payload follows as raw bytes-to-end, matching the
                    // unprefixed Put(byte[]) on the send side, so GetRemainingBytes drains it. -xlinka
                    _ = reader.GetString(); // sourceSessionId - consumed to advance the reader; not surfaced yet
                    var relayData = reader.GetRemainingBytes();
                    OnRelayDataReceived?.Invoke(relayData);
                    break;

                default:
                    LumoraLogger.Warn($"SessionServerClient: Unknown opcode 0x{opcode:X2}");
                    break;
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"SessionServerClient: Error processing message - {ex.Message}");
        }
        finally
        {
            reader.Recycle();
        }
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        // Not used
    }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency)
    {
        // Not used
    }

    public void OnConnectionRequest(ConnectionRequest request)
    {
        // We don't accept incoming connections through this client
        request.Reject();
    }

    // INatPunchListener implementation

    public void OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint, string token)
    {
        LumoraLogger.Log($"SessionServerClient: NAT introduction request received - Local: {localEndPoint}, Remote: {remoteEndPoint}, Token: {token}");
    }

    public void OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type, string token)
    {
        // NAT punch succeeded! We can now connect directly to this endpoint
        LumoraLogger.Log($"SessionServerClient: NAT punch SUCCESS! Endpoint: {targetEndPoint}, Type: {type}, Token: {token}");
        OnNATPunchSuccess?.Invoke(targetEndPoint);
    }

    public void Dispose()
    {
        Disconnect();
        _cts?.Dispose();
    }
}

