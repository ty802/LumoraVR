// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Networking.Web;

namespace Lumora.Core.Scripting;

// A web host as a script names it, a bare host name or any address on it, turned into the key web access
// decisions are kept under: the host alone, lowercased, an international name in punycode. Scheme, port
// and path play no part. Null for text that names no host.
internal static class ScriptWebHosts
{
    public static string? KeyOf(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (text.Contains("://", StringComparison.Ordinal))
            return Uri.TryCreate(text, UriKind.Absolute, out var address) ? WebAccess.HostKey(address) : null;
        return Uri.TryCreate("http://" + text, UriKind.Absolute, out var bare) ? WebAccess.HostKey(bare) : null;
    }

    public static string? KeyOf(Uri? url) => url is { IsAbsoluteUri: true } ? WebAccess.HostKey(url) : null;
}
