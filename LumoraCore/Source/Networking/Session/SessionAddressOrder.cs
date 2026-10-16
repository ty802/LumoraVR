// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Lumora.Core.Networking.Session;

// The order a joiner dials a session's addresses in.
//
// A host advertises every address it listens on: one direct address per network card, then Steam. A direct
// address on a private range only answers from inside the host's own network, and each one that does not
// answer costs a full connect timeout, so dialling them in advertised order left a remote joiner waiting out
// every dead LAN card before Steam was ever tried, or never trying it at all where only the first address was
// kept. Same network first (cheap direct UDP), then public direct addresses, then Steam, then the relay, and
// private addresses from some other network last. Ties keep the host's order. -xlinka
public static class SessionAddressOrder
{
    public static List<Uri> Order(IEnumerable<Uri> addresses)
    {
        var networks = LocalNetworks();
        return addresses
            .Where(a => a != null)
            .Distinct()
            .Select((address, index) => (address, index, rank: Rank(address, networks)))
            .OrderBy(t => t.rank)
            .ThenBy(t => t.index)
            .Select(t => t.address)
            .ToList();
    }

    // How long one address gets before the next is tried. A host on the same network answers within
    // milliseconds, so a private address still silent after a few seconds belongs to some other network
    // (home routers share the same ranges, so it can even look like ours); the margin left is for a host
    // stalled mid-load, which polls its network from the frame. Steam gets its whole first-connect window:
    // finding a relay route can take most of it.
    public static int ConnectTimeoutMs(Uri address)
    {
        if (string.Equals(address.Scheme, "lnl", StringComparison.OrdinalIgnoreCase)
            && IPAddress.TryParse(address.Host, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork
            && (IPAddress.IsLoopback(ip) || IsPrivate(ToUInt(ip))))
            return 6000;
        if (string.Equals(address.Scheme, "steam", StringComparison.OrdinalIgnoreCase))
            return 16000;
        return 10000;
    }

    private static int Rank(Uri address, List<(uint Address, uint Mask)> networks)
    {
        if (string.Equals(address.Scheme, "lnlrelay", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (!string.Equals(address.Scheme, "lnl", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (!IPAddress.TryParse(address.Host, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return 1;
        if (IPAddress.IsLoopback(ip))
            return 0;
        uint value = ToUInt(ip);
        foreach (var (local, mask) in networks)
        {
            if ((value & mask) == (local & mask))
                return 0;
        }
        return IsPrivate(value) ? 4 : 1;
    }

    // 10/8, 172.16/12, 192.168/16, link-local 169.254/16 and carrier NAT 100.64/10.
    private static bool IsPrivate(uint ip)
        => (ip & 0xFF000000) == 0x0A000000
        || (ip & 0xFFF00000) == 0xAC100000
        || (ip & 0xFFFF0000) == 0xC0A80000
        || (ip & 0xFFFF0000) == 0xA9FE0000
        || (ip & 0xFFC00000) == 0x64400000;

    private static List<(uint Address, uint Mask)> LocalNetworks()
    {
        var networks = new List<(uint, uint)>();
        try
        {
            foreach (var card in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (card.OperationalStatus != OperationalStatus.Up)
                    continue;
                foreach (var unicast in card.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(unicast.Address))
                        continue;
                    var mask = unicast.IPv4Mask;
                    networks.Add((ToUInt(unicast.Address),
                        mask != null && mask.AddressFamily == AddressFamily.InterNetwork ? ToUInt(mask) : 0xFFFFFFFF));
                }
            }
        }
        catch
        {
            // No card list means no address counts as same-network; the order still puts Steam ahead of
            // private addresses.
        }
        return networks;
    }

    private static uint ToUInt(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }
}
