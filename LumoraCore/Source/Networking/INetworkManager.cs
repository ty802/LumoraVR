// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Networking;

/// <summary>
/// Transport abstraction. A network manager owns a set of URI schemes it can
/// dial, creates client connections and server listeners for those schemes, and
/// is polled once per frame to drive its transport.
///
/// Multiple managers can coexist (e.g. LNL + Steam relays). The
/// <see cref="NetworkManagerRegistry"/> sorts them by <see cref="Priority"/>
/// when a URI could be served by more than one transport.
/// </summary>
public interface INetworkManager : IDisposable
{
    /// <summary>
    /// Higher priority managers are preferred when multiple managers support
    /// the same URI scheme. Allows a Steam relay to outrank direct UDP when
    /// both are available, for instance.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// True if this transport binds to a host port (e.g. UDP). False for
    /// relay-style transports whose listener identity is a user ID, not a port.
    /// </summary>
    bool UsesPort { get; }

    /// <summary>
    /// Create a client connection to the URI. Caller must call
    /// <see cref="IConnection.Connect"/> after wiring event handlers.
    /// </summary>
    IConnection CreateConnection(Uri uri);

    /// <summary>
    /// Open a server-side listener. <paramref name="port"/> is ignored when
    /// <see cref="UsesPort"/> is false. <paramref name="sessionId"/> seeds the
    /// listener's <see cref="IListener.GlobalUri"/>.
    /// </summary>
    IListener CreateListener(ushort port, string sessionId);

    /// <summary>
    /// Append every URI scheme this manager handles to <paramref name="schemes"/>.
    /// </summary>
    void GetSupportedSchemes(List<string> schemes);

    /// <summary>True if this manager handles a URI of the given scheme.</summary>
    bool SupportsScheme(string scheme);

    /// <summary>
    /// Filter and order the candidate URIs for this transport. Used when a
    /// session listing carries multiple URIs and we need to pick the one this
    /// transport can actually dial. <paramref name="expectedSessionId"/> is
    /// populated from the URI when applicable (relay transports embed it).
    /// </summary>
    List<Uri> GetPrioritizedUriList(IEnumerable<Uri> uris, out string expectedSessionId);

    /// <summary>
    /// Drive the transport. Must be called every frame by the engine update
    /// loop while connections or listeners are active.
    /// </summary>
    void Update();

    /// <summary>Shut down all connections and listeners owned by this manager.</summary>
    void Stop();
}
