// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Nexus.Transport;

// Transport-agnostic server-side listener. Implementations are owned by an INetworkManager and polled
// indirectly via INetworkManager.Update.
public interface IListener : IDisposable
{
    bool IsActive { get; }

    // Null for relay-only transports whose only addressable URI is global.
    Uri LocalUri { get; }

    // Includes the session identifier so a single hub can host multiple worlds.
    Uri GlobalUri { get; }

    // Fired on the network thread.
    event Action<IConnection> PeerConnected;

    // Orderly close, timeout, or transport error.
    event Action<IConnection> PeerDisconnected;

    // Stops accepting and disconnects every active peer.
    void Close();
}
