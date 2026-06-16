// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Networking;

/// <summary>
/// Transport-agnostic server-side listener. Emits <see cref="PeerConnected"/>
/// when a remote attaches and <see cref="PeerDisconnected"/> when one drops;
/// each callback delivers an <see cref="IConnection"/> the host can read/write
/// over.
///
/// Implementations are owned by an <see cref="INetworkManager"/> and polled
/// indirectly via <see cref="INetworkManager.Update"/>.
/// </summary>
public interface IListener : IDisposable
{
    /// <summary>True between <see cref="Close"/> and a successful start.</summary>
    bool IsActive { get; }

    /// <summary>
    /// URI clients on the same machine/LAN can use to dial this listener.
    /// Null for relay-only transports whose only addressable URI is global.
    /// </summary>
    Uri LocalUri { get; }

    /// <summary>
    /// URI for global discovery (e.g. published to a session directory).
    /// Includes the session identifier so a single hub can host multiple worlds.
    /// </summary>
    Uri GlobalUri { get; }

    /// <summary>Fired on the network thread when a remote becomes connected.</summary>
    event Action<IConnection> PeerConnected;

    /// <summary>Fired when a peer drops (orderly close, timeout, or transport error).</summary>
    event Action<IConnection> PeerDisconnected;

    /// <summary>Stop accepting and disconnect every active peer.</summary>
    void Close();
}
