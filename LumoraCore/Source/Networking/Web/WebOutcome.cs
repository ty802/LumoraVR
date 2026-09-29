// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Networking.Web;

public enum WebOutcome
{
    // The server answered. Every status counts, 4xx and 5xx included.
    Response,

    // Nothing usable came back: the address was refused, the connection or the timeout failed, a
    // limit was hit.
    Failed,

    // This machine's user said no to the host, or never answered.
    Denied,
}
