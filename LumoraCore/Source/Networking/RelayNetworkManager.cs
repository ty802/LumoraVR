// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Networking.Session;

namespace Lumora.Core.Networking;

/// <summary>
/// <see cref="INetworkManager"/> for relay-routed client connections. A relay URI
/// is <c>lnlrelay://{sessionId}</c>; the session id is the relay target, dialed
/// through the configured session/relay server.
/// </summary>
// Client-side join transport: dialing a relay URI produces a RelayConnection that
// tunnels session traffic through the relay server. Hosting over relay (a
// listener) is a separate concern and not provided here. - xlinka
//
// EXTERNAL PREREQUISITES (do not exist in this repo yet):
//   1. A relay server that accepts the OP_RELAY_* protocol in SessionServerClient
//      and forwards packets between a joiner and a host by session id.
//   2. A host-side relay bridge that registers the session with that server and
//      feeds relayed frames into the host's Session (and answers the joiner's
//      end-to-end crypto handshake with a ServerHello - see RelayConnection).
// Until both exist, only the client join path is usable, and it will not complete
// the handshake because no host answers it. -xlinka
public sealed class RelayNetworkManager : INetworkManager
{
    public const string SCHEME = "lnlrelay";

    // Below direct UDP (priority 0) so a direct LNL path always wins when both are
    // offered; relay is the fallback.
    public int Priority => -100;

    public bool UsesPort => false;

    public IConnection CreateConnection(Uri uri)
    {
        if (uri == null) throw new ArgumentNullException(nameof(uri));
        var sessionId = ExtractSessionId(uri);
        return new RelayConnection(Session.Session.SessionServerAddress, Session.Session.SessionServerPort, sessionId, uri);
    }

    public IListener CreateListener(ushort port, string sessionId)
        => throw new NotSupportedException(
            "Relay hosting is not available: it requires an external relay server and a host-side relay " +
            "bridge, neither of which exists in this repository yet. Relay is client-join only today. " +
            "Host normally (direct/LNL listener) and let joiners fall back to relay once those pieces exist.");

    public void GetSupportedSchemes(List<string> schemes)
    {
        if (schemes == null) return;
        schemes.Add(SCHEME);
    }

    public bool SupportsScheme(string scheme)
        => string.Equals(scheme, SCHEME, StringComparison.OrdinalIgnoreCase);

    public List<Uri> GetPrioritizedUriList(IEnumerable<Uri> uris, out string expectedSessionId)
    {
        var result = new List<Uri>();
        expectedSessionId = null!;
        if (uris == null) return result;
        foreach (var uri in uris)
        {
            if (uri != null && SupportsScheme(uri.Scheme))
            {
                result.Add(uri);
                expectedSessionId ??= ExtractSessionId(uri);
            }
        }
        return result;
    }

    // Relay traffic rides the wrapped SessionServerClient's own poll loop, so the
    // manager has nothing to drive per frame.
    public void Update() { }

    public void Stop() { }

    public void Dispose() { }

    private static string ExtractSessionId(Uri uri)
    {
        // lnlrelay://relay/{sessionId} - carried in the path, which (unlike the host)
        // preserves case; session ids can be case-sensitive.
        var path = uri.AbsolutePath?.Trim('/');
        if (!string.IsNullOrEmpty(path))
            return Uri.UnescapeDataString(path);
        return uri.Host;
    }
}
