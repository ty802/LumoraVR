// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Net;
using LiteNetLib;
using LumoraLogger = Lumora.Nexus.Diagnostics.NexusLog;
using System.Threading;

namespace Lumora.Nexus.Transport.LNL;

public class LNLPeer : IConnection
{
    internal readonly NetManager Server;
    internal readonly NetPeer Peer;
    private readonly LNLCryptoSession _crypto = new(isClient: false);
    private readonly LinkEmulator? _emulator = LinkEmulator.Create();

    public bool IsOpen { get; private set; }
    public string FailReason { get; private set; } = null!;
    public IPAddress IP => (Peer?.Address) ?? null!;
    public Uri Address { get; private set; }
    public string Identifier { get; private set; }
    public ulong ReceivedBytes { get; private set; }
    private long _sentBytes;
    public ulong SentBytes => (ulong)Interlocked.Read(ref _sentBytes);
    public int Ping => Peer?.Ping ?? -1;
    public long PacketsSent => Peer?.Statistics.PacketsSent ?? 0;
    public long PacketsLost => Peer?.Statistics.PacketLoss ?? 0;
    public int QueuedPackets => LNLLanes.Queued(Peer);
    public long PendingSendBytes => (long)LNLLanes.Queued(Peer) * LNLLanes.PacketBytes + (_emulator?.QueuedBytes ?? 0);
    public bool IsEncrypted => _crypto.IsEstablished;
    public string TransportName => "LNL";

    public event Action<IConnection> Closed = null!;
    public event Action<IConnection> Connected
    {
        add { }
        remove { }
    }

    public event Action<IConnection> ConnectionFailed
    {
        add { }
        remove { }
    }
    public event Action<byte[], int> DataReceived = null!;
    internal event Action<LNLPeer> CryptoEstablished = null!;

    internal DateTime CreatedUtc { get; } = DateTime.UtcNow;
    internal bool IsCryptoEstablished => _crypto.IsEstablished;

    public LNLPeer(NetManager server, NetPeer peer)
    {
        Server = server;
        Peer = peer;
        IsOpen = true;
        Address = new Uri($"lnl://{peer.Address}:{peer.Port}");
        Identifier = $"LNL:{peer.Address}:{peer.Port}";

        LumoraLogger.Debug($"[lnl] LNL Peer created: {Identifier}");
    }

    public void Connect(Action<string> statusCallback)
    {
        throw new NotSupportedException("LNLPeer doesn't support Connect - already connected");
    }

    public void Close()
    {
        if (IsOpen && Peer != null)
        {
            Server.DisconnectPeer(Peer);
            InformClosed();
        }
    }

    private bool _loggedNotConnected;

    public void Send(byte[] data, int length, bool reliable, bool background)
    {
        if (Peer == null || Peer.ConnectionState != ConnectionState.Connected)
        {
            // Expected while a peer is leaving - the host keeps trying to stream to it until LNL times the peer
            // out (~30s). Don't Warn every frame; note it once at Debug. The Closed event is the real signal. -xlinka
            if (!_loggedNotConnected)
            {
                _loggedNotConnected = true;
                LumoraLogger.Debug($"[lnl] Send to {Identifier} skipped - peer not connected (leaving/timeout)");
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
            LumoraLogger.Warn($"[lnl] Send to {Identifier} failed before LNL crypto was ready ({ex.Message})");
            return;
        }

        SendRaw(encrypted, encrypted.Length, reliable, background);
    }

    private readonly LNLLanes _lanes = new();

    public void SendBulk(byte[] data, int length)
    {
        if (Peer == null || Peer.ConnectionState != ConnectionState.Connected)
            return;
        byte[] encrypted;
        try
        {
            encrypted = _crypto.Encrypt(data, length);
        }
        catch (Exception ex)
        {
            LumoraLogger.Warn($"[lnl] Bulk send to {Identifier} failed before LNL crypto was ready ({ex.Message})");
            return;
        }
        Interlocked.Add(ref _sentBytes, encrypted.Length);
        if (_emulator != null)
        {
            _emulator.Send(encrypted.Length, droppable: false, () =>
            {
                if (Peer.ConnectionState != ConnectionState.Connected)
                    return;
                _lanes.Send(Peer, encrypted, encrypted.Length);
                Server.TriggerUpdate();
            });
            return;
        }
        _lanes.Send(Peer, encrypted, encrypted.Length);
        Server.TriggerUpdate();
    }

    private void SendRaw(byte[] data, int length, bool reliable, bool background)
    {
        DeliveryMethod method = reliable
            ? DeliveryMethod.ReliableOrdered
            : DeliveryMethod.Sequenced;

        byte channel = (byte)(background ? 1 : 0);

        if (method == DeliveryMethod.Sequenced &&
            Peer.GetMaxSinglePacketSize(method) < length)
        {
            method = DeliveryMethod.ReliableUnordered;
        }

        Interlocked.Add(ref _sentBytes, length);
        if (_emulator != null)
        {
            _emulator.Send(length, droppable: !reliable, () =>
            {
                if (Peer.ConnectionState != ConnectionState.Connected)
                    return;
                Peer.Send(data, 0, length, channel, method);
                Server.TriggerUpdate();
            });
            return;
        }
        Peer.Send(data, 0, length, channel, method);
        Server.TriggerUpdate();
    }

    // No-op. LNLPeer shares the listener's NetManager; the listener polls
    // for both. - xlinka
    public void Poll() { }

    internal void InformOfNewData(byte[] data, int length)
    {
        ReceivedBytes += (ulong)length;
        bool wasEstablished = _crypto.IsEstablished;
        if (!_crypto.TryHandleIncoming(data, length, out var plaintext, out var response))
        {
            FailReason = "LNL crypto handshake failed";
            LumoraLogger.Warn($"[lnl] Closing {Identifier}: {FailReason}");
            Close();
            return;
        }

        if (response != null)
            SendRaw(response, response.Length, reliable: true, background: false);

        if (!wasEstablished && _crypto.IsEstablished)
        {
            LumoraLogger.Debug($"[lnl] Crypto established with {Identifier}");
            CryptoEstablished?.Invoke(this);
        }

        if (plaintext != null)
            DataReceived?.Invoke(plaintext, plaintext.Length);
    }

    internal void InformClosed()
    {
        if (IsOpen)
        {
            IsOpen = false;
            LumoraLogger.Debug($"[lnl] LNL Peer closed: {Identifier}");
            Closed?.Invoke(this);
        }
    }

    public void Dispose()
    {
        _emulator?.Stop();
        Close();
        _crypto.Dispose();
    }
}
