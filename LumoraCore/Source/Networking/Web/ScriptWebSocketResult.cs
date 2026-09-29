// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Networking.Web;

// What opening a websocket came to. Outcome is Response with the socket when it opened, Failed when
// nothing opened, Denied when this machine's user refused the host or never answered. Error is empty for
// an open socket and one short sentence otherwise.
public sealed record ScriptWebSocketResult(WebOutcome Outcome, ScriptWebSocket? Socket, string Url, string Error)
{
    internal static ScriptWebSocketResult Opened(string url, ScriptWebSocket socket)
        => new(WebOutcome.Response, socket, url, string.Empty);

    internal static ScriptWebSocketResult Failed(string url, string error)
        => new(WebOutcome.Failed, null, url, error);

    internal static ScriptWebSocketResult Denied(string url, string error)
        => new(WebOutcome.Denied, null, url, error);
}
