// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Net;
using System.Threading.Tasks;
using Lumora.Core.Networking.LNL;
using Lumora.Core.Networking.Session;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking;

/// <summary>
/// An <see cref="IConnection"/> that carries session traffic through the relay
/// server instead of a direct socket. Used as the fallback when NAT punchthrough
/// can't open a direct path. Wraps a <see cref="SessionServerClient"/>: outgoing
/// frames go out as relay packets, incoming relay packets surface as received data.
/// </summary>
// Client-side only: this connects the joiner to the relay server and routes the
// normal session handshake/sync frames over it. The host bridging relayed frames
// into its own session, and the relay server itself, are separate pieces. - xlinka
//
// Encryption: relayed payloads are wrapped in the SAME ECDH+AES-GCM session the
// direct LNL path uses (LNLCryptoSession), so the relay server only ever sees
// ciphertext - it cannot read session traffic it forwards. The handshake runs
// end-to-end between this joiner and the SESSION HOST (the relay is a dumb
// forwarder), exactly like the direct path. See the handshake-gap note below. -xlinka
public sealed class RelayConnection : IConnection
{
    private readonly string _serverAddress;
    private readonly int _serverPort;
    private readonly string _sessionId;
    private readonly Uri _address;

    // Mirror the direct LNL path: the joiner is the client side of the handshake.
    private readonly LNLCryptoSession _crypto = new(isClient: true);

    private SessionServerClient _client = null!;
    private bool _confirmed;

    public RelayConnection(string serverAddress, int serverPort, string sessionId, Uri address)
    {
        _serverAddress = serverAddress;
        _serverPort = serverPort;
        _sessionId = sessionId;
        _address = address;
    }

    public bool IsOpen { get; private set; }
    public string FailReason { get; private set; } = string.Empty;
    public IPAddress IP { get; private set; } = IPAddress.None;
    public Uri Address => _address;
    public string Identifier => $"relay:{_sessionId}";
    public ulong ReceivedBytes { get; private set; }
    public int Ping => _client?.IsConnected == true ? -1 : -1; // relay RTT not surfaced
    public bool IsEncrypted => _crypto.IsEstablished;
    public string TransportName => "Relay";

    public event Action<IConnection> Closed = null!;
    public event Action<IConnection> Connected = null!;
    public event Action<IConnection> ConnectionFailed = null!;
    public event Action<byte[], int> DataReceived = null!;

    public void Connect(Action<string> statusCallback)
    {
        if (IPAddress.TryParse(_serverAddress, out var ip))
            IP = ip;

        _client = new SessionServerClient(_serverAddress, _serverPort);
        _client.OnRelayConfirmed += OnRelayConfirmed;
        _client.OnRelayDataReceived += OnRelayData;
        _client.OnDisconnected += OnRelayDisconnected;

        statusCallback?.Invoke($"Relaying via {_serverAddress}:{_serverPort}");
        _ = ConnectAndRequestRelay();
    }

    private async Task ConnectAndRequestRelay()
    {
        try
        {
            bool ok = await _client.ConnectForRelayAsync();
            if (!ok)
            {
                FailReason = "relay server unreachable";
                ConnectionFailed?.Invoke(this);
                return;
            }
            // The OP_RELAY_CONFIRM reply opens the relay tunnel (OnRelayConfirmed),
            // at which point we start the end-to-end crypto handshake.
            _client.RequestRelay(_sessionId);
        }
        catch (Exception ex)
        {
            FailReason = ex.Message;
            ConnectionFailed?.Invoke(this);
        }
    }

    private void OnRelayConfirmed(string targetId)
    {
        if (_confirmed)
            return;
        _confirmed = true;

        // The relay tunnel is up, but the link is NOT open yet: like the direct LNL
        // path, the connection only counts as connected once the crypto handshake
        // completes (OnRelayData below fires Connected). Kick off the ClientHello.
        LumoraLogger.Log($"[lnl] RelayConnection: relay tunnel confirmed for session '{_sessionId}'; starting crypto handshake");
        try
        {
            var hello = _crypto.CreateClientHello();
            SendThroughRelay(hello);
        }
        catch (Exception ex)
        {
            FailReason = $"relay crypto handshake start failed: {ex.Message}";
            LumoraLogger.Warn($"[lnl] RelayConnection: {FailReason}");
            ConnectionFailed?.Invoke(this);
        }
    }

    private void OnRelayData(byte[] data)
    {
        if (data == null || data.Length == 0)
            return;
        ReceivedBytes += (ulong)data.Length;

        // Feed every relayed packet through the same crypto state machine the direct
        // path uses: handshake frames are absorbed (and may produce a reply to send
        // back through the relay); encrypted frames are decrypted to plaintext.
        bool wasEstablished = _crypto.IsEstablished;
        if (!_crypto.TryHandleIncoming(data, data.Length, out var plaintext, out var response))
        {
            FailReason = "relay crypto handshake failed";
            LumoraLogger.Warn($"[lnl] RelayConnection: closing '{_sessionId}': {FailReason}");
            Close();
            return;
        }

        if (response != null)
            SendThroughRelay(response);

        if (!wasEstablished && _crypto.IsEstablished)
        {
            IsOpen = true;
            LumoraLogger.Log($"[lnl] RelayConnection: crypto established for session '{_sessionId}'");
            Connected?.Invoke(this);
        }

        if (plaintext != null)
            DataReceived?.Invoke(plaintext, plaintext.Length);
    }

    private void OnRelayDisconnected(string reason)
    {
        if (!IsOpen && !_confirmed)
            return;
        IsOpen = false;
        FailReason = reason;
        Closed?.Invoke(this);
    }

    public void Send(byte[] data, int length, bool reliable, bool background)
    {
        if (_client == null || !IsOpen)
            return;

        // Encrypt with the established session before it touches the relay, so the
        // relay server forwards ciphertext only. Encrypt throws if crypto isn't
        // established, but IsOpen already gates on that.
        byte[] encrypted;
        try
        {
            encrypted = _crypto.Encrypt(data, length);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"[lnl] RelayConnection: send failed before crypto was ready ({ex.Message})");
            return;
        }
        SendThroughRelay(encrypted);
    }

    // Push raw bytes (a handshake frame or an encrypted frame) onto the relay tunnel.
    // reliable/background are not distinguished here - the relay tunnel itself runs a
    // single reliable-ordered channel to the relay server.
    private void SendThroughRelay(byte[] payload)
    {
        if (_client == null)
            return;
        _client.SendRelayPacket(_sessionId, payload);
    }

    // The wrapped SessionServerClient runs its own poll loop, so there's nothing
    // to drive from the session's per-frame poll.
    public void Poll() { }

    public void Close()
    {
        IsOpen = false;
        _client?.Dispose();
        _client = null!;
        _crypto.Dispose();
    }

    public void Dispose() => Close();
}

// HANDSHAKE GAP (relay is client-only today):
// The client-side encryption above is complete and correct - outgoing frames are
// AES-GCM sealed and incoming frames are opened with the same ECDH session the
// direct LNL path uses, so nothing rides the relay in cleartext. BUT the handshake
// is end-to-end with the SESSION HOST, and completing it requires the host to:
//   (a) receive our relayed ClientHello (the host-side relay bridge),
//   (b) answer with a ServerHello back through the relay.
// Neither the host-side relay bridge nor the relay server itself exists in this
// repo yet (see RelayNetworkManager.CreateListener and Session host notes). Until
// they do, OnRelayConfirmed starts the handshake but no ServerHello returns, so
// IsEncrypted stays false, Connected never fires, and the connection simply does
// not open. That is the honest current state - the wrapping is real; the peer that
// answers it is the missing external/host-side piece. -xlinka
