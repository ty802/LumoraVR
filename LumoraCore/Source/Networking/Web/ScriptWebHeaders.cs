// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Lumora.Core.Networking.Web;

// Header text in and out of a script: one "Name: value" per line.
internal static class ScriptWebHeaders
{
    // Framing and connection headers belong to the client. A script that could set them could desync
    // the connection or smuggle a second request down it. User-Agent is fixed too, so script traffic
    // always says what it is.
    private static readonly HashSet<string> Refused = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Transfer-Encoding", "Connection", "Keep-Alive", "Upgrade", "TE",
        "Trailer", "Expect", "User-Agent",
    };

    // These describe the body, so they ride on the content object rather than the request.
    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", "Content-Language", "Content-Encoding", "Content-Disposition", "Content-MD5",
        "Content-Range", "Expires", "Last-Modified", "Allow",
    };

    public static bool IsRefused(string name)
        => Refused.Contains(name) || name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase);

    public static bool IsContentHeader(string name) => ContentHeaders.Contains(name);

    // Blank lines, lines with no colon, names that are not HTTP tokens and values with control or
    // non-ASCII characters are skipped rather than failing the request, and so is every refused name.
    public static List<KeyValuePair<string, string>> Parse(string? text)
    {
        var headers = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(text))
            return headers;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            int colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            var name = line.Substring(0, colon).Trim();
            var value = line.Substring(colon + 1).Trim(' ', '\t');
            if (!IsToken(name) || !IsValue(value) || IsRefused(name))
                continue;
            headers.Add(new KeyValuePair<string, string>(name, value));
        }
        return headers;
    }

    // Response and content headers as the server sent them, one line per value. Stops early once past
    // limit, so the caller can refuse an oversized block without building all of it.
    public static string Format(HttpResponseMessage response, int limit)
    {
        var builder = new StringBuilder();
        Append(builder, response.Headers, limit);
        if (builder.Length <= limit)
            Append(builder, response.Content.Headers, limit);
        if (builder.Length > 0 && builder[builder.Length - 1] == '\n')
            builder.Length--;
        return builder.ToString();
    }

    private static void Append(StringBuilder builder, HttpHeaders headers, int limit)
    {
        foreach (var header in headers.NonValidated)
        {
            foreach (var value in header.Value)
            {
                builder.Append(header.Key).Append(": ").Append(value).Append('\n');
                if (builder.Length > limit)
                    return;
            }
        }
    }

    private static bool IsToken(string name)
    {
        if (name.Length == 0)
            return false;
        foreach (char c in name)
        {
            bool ok = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
                or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
            if (!ok)
                return false;
        }
        return true;
    }

    private static bool IsValue(string value)
    {
        foreach (char c in value)
        {
            if (c != '\t' && (c < 0x20 || c > 0x7E))
                return false;
        }
        return true;
    }
}
