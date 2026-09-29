// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Networking.Web;

// Thrown from the client's connect step when a host resolves only to addresses the filter refuses. The
// HTTP stack wraps it; the client digs it back out to report the refusal in plain words.
internal sealed class WebAddressBlockedException : Exception
{
    public WebAddressBlockedException(string message) : base(message)
    {
    }
}
