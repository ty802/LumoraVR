// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Nexus.Transport;

// A network manager owns a set of URI schemes it can dial, creates client connections and server
// listeners for those schemes, and is polled once per frame to drive its transport. Multiple managers
// can coexist (e.g. LNL + Steam relays); NetworkManagerRegistry sorts them by Priority when a URI could
// be served by more than one transport.
public interface INetworkManager : IDisposable
{
    // Higher wins when several managers support the same URI scheme, so a Steam relay can outrank
    // direct UDP.
    int Priority { get; }

    // False for relay-style transports whose listener identity is a user ID, not a port.
    bool UsesPort { get; }

    // Caller must call Connect after wiring event handlers.
    IConnection CreateConnection(Uri uri);

    // port is ignored when UsesPort is false; sessionId seeds the listener's GlobalUri.
    IListener CreateListener(ushort port, string sessionId);

    // Appends to the list rather than replacing it.
    void GetSupportedSchemes(List<string> schemes);

    bool SupportsScheme(string scheme);

    // Filters and orders the candidate URIs for this transport, for when a session listing carries
    // several. expectedSessionId is populated from the URI when applicable (relay transports embed it).
    List<Uri> GetPrioritizedUriList(IEnumerable<Uri> uris, out string expectedSessionId);

    // Must be called every frame by the engine update loop while connections or listeners are active.
    void Update();

    void Stop();
}
