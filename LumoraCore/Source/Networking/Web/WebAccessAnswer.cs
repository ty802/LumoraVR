// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Networking.Web;

public enum WebAccessAnswer
{
    Allowed,
    Denied,

    // The prompt was dismissed or left alone. Never remembered, so the next request asks again.
    Ignored,
}
