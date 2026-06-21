// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Net;
using LiteNetLib;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.LNL;

/// <summary>
/// LiteNetLib server-side peer wrapper.
/// Represents a connected client from the server's perspective.
/// </summary>
public class LNLPeer : IConnection
{
    internal readonly NetManager Server;
    internal readonly NetPeer Peer;

    public bool IsOpen { get; private set; }
    public string FailReason { get; private set; } = null!;
    public IPAddress IP => (Peer?.Address) ?? null!;
    public Uri Address { get; private set; }
    public string Identifier { get; private set; }
    public ulong ReceivedBytes { get; private set; }

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

    public LNLPeer(NetManager server, NetPeer peer)
    {
        Server = server;
        Peer = peer;
        IsOpen = true;
        Address = new Uri($"lnl://{peer.Address}:{peer.Port}");
        Identifier = $"LNL:{peer.Address}:{peer.Port}";

        LumoraLogger.Log($"[lnl] LNL Peer created: {Identifier}");
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

        DeliveryMethod method = reliable
            ? DeliveryMethod.ReliableOrdered
            : DeliveryMethod.Sequenced;

        byte channel = (byte)(background ? 1 : 0);

        if (method == DeliveryMethod.Sequenced &&
            Peer.GetMaxSinglePacketSize(method) < length)
        {
            method = DeliveryMethod.ReliableUnordered;
        }

        Peer.Send(data, 0, length, channel, method);
    }

    /// <summary>
    /// No-op. LNLPeer shares the listener's NetManager; the listener polls
    /// for both. - xlinka
    /// </summary>
    public void Poll() { }

    internal void InformOfNewData(byte[] data, int length)
    {
        ReceivedBytes += (ulong)length;
        DataReceived?.Invoke(data, length);
    }

    internal void InformClosed()
    {
        if (IsOpen)
        {
            IsOpen = false;
            LumoraLogger.Log($"[lnl] LNL Peer closed: {Identifier}");
            Closed?.Invoke(this);
        }
    }

    public void Dispose()
    {
        Close();
    }
}

