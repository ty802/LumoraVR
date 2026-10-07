// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Net;
using System.Net.Sockets;

namespace Lumora.Core.Networking.Web;

// The addresses a script's web request may open a socket to.
//
// Public: everything but loopback, the private and carrier-grade NAT blocks, link-local, unique local,
// site-local, multicast, unspecified and broadcast, the reserved, benchmarking, discard and documentation
// blocks, and every IPv6 shape that carries an IPv4 address inside it (mapped, compatible, translated,
// NAT64, 6to4) when that IPv4 address is one of those.
//
// A local address is reached only when the script named a local host itself: an address written out, this
// machine, or a name only a local network resolves. The user at the machine allows that host like any other,
// and the prompt says it is local. A public name never reaches a local address, even after it is allowed:
// allowing a site is not allowing whatever it points its name at, the router behind the person included.
// The client applies this to the address a socket is about to connect to, after its own resolution, so a
// public name that points somewhere local, or starts to halfway through a session, never gets a connection.
// -xlinka
public static class WebAddressFilter
{
    private static readonly (uint Network, int Prefix)[] BlockedV4 =
    {
        (0x00000000, 8),  // 0.0.0.0/8, this network; holds the unspecified address
        (0x0A000000, 8),  // 10.0.0.0/8
        (0x64400000, 10), // 100.64.0.0/10, carrier-grade NAT
        (0x7F000000, 8),  // 127.0.0.0/8, loopback
        (0xA9FE0000, 16), // 169.254.0.0/16, link-local
        (0xAC100000, 12), // 172.16.0.0/12
        (0xC0000000, 24), // 192.0.0.0/24, protocol assignments
        (0xC0000200, 24), // 192.0.2.0/24, documentation
        (0xC0A80000, 16), // 192.168.0.0/16
        (0xC6120000, 15), // 198.18.0.0/15, benchmarking
        (0xC6336400, 24), // 198.51.100.0/24, documentation
        (0xCB007100, 24), // 203.0.113.0/24, documentation
        (0xE0000000, 4),  // 224.0.0.0/4, multicast
        (0xF0000000, 4),  // 240.0.0.0/4, reserved; holds 255.255.255.255
    };

    public static bool IsPublic(IPAddress? address)
    {
        if (address == null)
            return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> v4 = stackalloc byte[4];
            return address.TryWriteBytes(v4, out _) && IsPublicV4(ReadV4(v4, 0));
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Span<byte> v6 = stackalloc byte[16];
            return address.TryWriteBytes(v6, out _) && IsPublicV6(v6);
        }
        return false;
    }

    // An address a socket can be opened to at all: not unspecified, multicast, broadcast or reserved.
    public static bool IsReachable(IPAddress? address)
    {
        if (address == null)
            return false;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            Span<byte> v4 = stackalloc byte[4];
            if (!address.TryWriteBytes(v4, out _))
                return false;
            return v4[0] != 0 && v4[0] < 224;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Span<byte> v6 = stackalloc byte[16];
            return address.TryWriteBytes(v6, out _) && !AllZero(v6) && v6[0] != 0xFF;
        }
        return false;
    }

    // A host named as local: an address written out, this machine, or a name only a local network resolves
    // (one label, or under .local, .lan, .home, .home.arpa, .internal or .localdomain).
    public static bool IsLocalHost(string? host)
    {
        if (string.IsNullOrEmpty(host))
            return false;
        if (host.Length > 2 && host[0] == '[' && host[^1] == ']')
            host = host.Substring(1, host.Length - 2);
        if (IPAddress.TryParse(host, out _))
            return true;
        host = host.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0)
            return false;
        if (host.IndexOf('.') < 0)
            return true;
        foreach (var suffix in LocalSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static readonly string[] LocalSuffixes =
        { ".localhost", ".local", ".lan", ".home", ".home.arpa", ".internal", ".localdomain" };

    private static bool IsPublicV4(uint address)
    {
        foreach (var (network, prefix) in BlockedV4)
        {
            uint mask = uint.MaxValue << (32 - prefix);
            if ((address & mask) == network)
                return false;
        }
        return true;
    }

    private static bool IsPublicV6(ReadOnlySpan<byte> b)
    {
        // ::/96 covers the unspecified address, loopback and the IPv4-compatible form.
        if (AllZero(b.Slice(0, 12)))
            return IsPublicV4(ReadV4(b, 12));
        // ::ffff:0:0/96, IPv4-mapped.
        if (AllZero(b.Slice(0, 10)) && b[10] == 0xFF && b[11] == 0xFF)
            return IsPublicV4(ReadV4(b, 12));
        // ::ffff:0:0:0/96, IPv4-translated.
        if (AllZero(b.Slice(0, 8)) && b[8] == 0xFF && b[9] == 0xFF && b[10] == 0 && b[11] == 0)
            return IsPublicV4(ReadV4(b, 12));
        // 64:ff9b::/96, NAT64 with the well-known prefix.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && AllZero(b.Slice(4, 8)))
            return IsPublicV4(ReadV4(b, 12));
        // 64:ff9b:1::/48, NAT64 kept for local use.
        if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4] == 0x00 && b[5] == 0x01)
            return false;
        // 2002::/16, 6to4, with the IPv4 address in the next four bytes.
        if (b[0] == 0x20 && b[1] == 0x02)
            return IsPublicV4(ReadV4(b, 2));
        // 2001:2::/48, benchmarking.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x02 && b[4] == 0x00 && b[5] == 0x00)
            return false;
        // 2001:db8::/32 and 3fff::/20, documentation.
        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)
            return false;
        if (b[0] == 0x3F && b[1] == 0xFF && (b[2] & 0xF0) == 0x00)
            return false;
        // 100::/64, discard only.
        if (b[0] == 0x01 && b[1] == 0x00 && AllZero(b.Slice(2, 6)))
            return false;
        // fc00::/7, unique local.
        if ((b[0] & 0xFE) == 0xFC)
            return false;
        // fe80::/10 link-local and fec0::/10 site-local.
        if (b[0] == 0xFE && (b[1] & 0x80) == 0x80)
            return false;
        // ff00::/8, multicast.
        if (b[0] == 0xFF)
            return false;
        return true;
    }

    private static uint ReadV4(ReadOnlySpan<byte> bytes, int offset)
        => ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];

    private static bool AllZero(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            if (value != 0)
                return false;
        }
        return true;
    }
}
