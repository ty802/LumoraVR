// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Lumora.Source.Godot.Bootstrap;

public static class DiscordJoinLink
{
    public const string AppId = "1330400493054464092";
    public const string Scheme = "discord-" + AppId;
    public const string LaunchFlag = "--discord-launch";

    public static string? LaunchActivation(IEnumerable<string> arguments)
    {
        bool launched = false;
        foreach (string arg in arguments)
        {
            if (arg.Equals(LaunchFlag, StringComparison.OrdinalIgnoreCase)) launched = true;
            if (arg.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase)) return arg;
            if (arg.StartsWith("--discord-uri=", StringComparison.OrdinalIgnoreCase)) return arg[14..].Trim('"');
        }
        return launched ? string.Empty : null;
    }

    public static bool TryParse(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > 4096
            || !Uri.TryCreate(value, UriKind.Absolute, out var candidate)) return false;
        if (candidate.Scheme == Scheme)
        {
            foreach (string pair in candidate.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');
                if (equals < 0) continue;
                string key = pair[..equals];
                if (key.Equals("secret", StringComparison.OrdinalIgnoreCase) || key.Equals("joinSecret", StringComparison.OrdinalIgnoreCase))
                    return TrySessionUri(Uri.UnescapeDataString(pair[(equals + 1)..]), out uri);
            }
            if (candidate.Host == "join")
                return TrySessionUri(Uri.UnescapeDataString(candidate.AbsolutePath.TrimStart('/')), out uri);
            return false;
        }
        return TrySessionUri(value, out uri);
    }

    public static bool TrySessionUri(string value, out Uri uri)
    {
        uri = null!;
        if (Encoding.UTF8.GetByteCount(value) > 128 || value.Any(char.IsControl)
            || !Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            || string.IsNullOrEmpty(candidate.Host) || candidate.UserInfo.Length != 0
            || candidate.Query.Length != 0 || candidate.Fragment.Length != 0) return false;
        string host = candidate.Host.Trim('[', ']');
        if (IPAddress.TryParse(host, out var ip) && (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))) return false;
        bool valid = candidate.Scheme switch
        {
            "lnl" => candidate.Port is > 0 and <= 65535,
            "steam" => ulong.TryParse(candidate.Host, out ulong id) && id > 0 && candidate.Segments.Length >= 2
                && int.TryParse(candidate.Segments[1].Trim('/'), out int channel) && channel >= 0,
            "lnlrelay" => true,
            _ => false,
        };
        if (valid) uri = candidate;
        return valid;
    }

    // A Discord friend is usually outside the host's LAN. Prefer a Steam address, then an internet
    // address, before a LAN-only address. Never shorten a secret: that changes the session it names.
    public static Uri? BestAddress(IEnumerable<Uri> addresses)
        => addresses.Where(a => a != null && a.IsAbsoluteUri && TrySessionUri(a.AbsoluteUri, out _))
            .OrderBy(a => a.Scheme == "steam" ? 0 : a.Scheme == "lnlrelay" ? 2 : IsPrivate(a) ? 3 : 1)
            .FirstOrDefault();

    private static bool IsPrivate(Uri uri)
    {
        if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address)) return uri.Host == "localhost";
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
        byte[] b = address.GetAddressBytes();
        if (b.Length == 16) return (b[0] & 0xFE) == 0xFC;
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168
            || b[0] == 169 && b[1] == 254 || b[0] == 100 && b[1] is >= 64 and <= 127;
    }

    // The RPC library writes this command verbatim on Windows and appends %u on Linux.
    public static string LaunchCommand(string executable, string? project, bool windows, bool frame)
    {
        string Quote(string value) => "\"" + (windows ? value : value.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%")) + "\"";
        string command = Quote(executable);
        if (!string.IsNullOrEmpty(project)) command += " --path " + Quote(project);
        if (frame) command += " --rendering-method mobile --xr-mode on";
        command += " -- " + LaunchFlag;
        if (frame) command += " --steam-frame --vr";
        if (windows) command += " \"%1\"";
        return command;
    }
}
