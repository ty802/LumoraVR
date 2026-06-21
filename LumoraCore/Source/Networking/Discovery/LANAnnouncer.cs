// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core.Networking.Session;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Networking.Discovery;

/// <summary>
/// Broadcasts session metadata on the local network for discovery.
/// </summary>
public class LANAnnouncer : IDisposable
{
    /// <summary>
    /// Default UDP port for session announcements.
    /// </summary>
    public const int BroadcastPort = 12101;

    /// <summary>
    /// Interval between broadcast packets in milliseconds.
    /// </summary>
    public const int BroadcastIntervalMs = 5000;

    // One broadcast socket BOUND to each local interface IP (so a 255.255.255.255 broadcast egresses that
    // interface, not just the default route) + one unbound catch-all. -xlinka
    private readonly List<UdpClient> _announcers = new();
    private CancellationTokenSource _cts = null!;
    private SessionMetadata _metadata = null!;
    private Guid _announcerId;
    private bool _isRunning;
    private bool _isDisposed;

    /// <summary>
    /// Unique identifier for this announcer instance.
    /// Used to filter out own announcements during discovery.
    /// </summary>
    public Guid AnnouncerId => _announcerId;

    /// <summary>
    /// Whether the announcer is currently broadcasting.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Start broadcasting session metadata on the local network.
    /// </summary>
    /// <param name="metadata">Session metadata to broadcast</param>
    public void StartAnnouncing(SessionMetadata metadata)
    {
        if (_isRunning || _isDisposed)
            return;

        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _announcerId = Guid.NewGuid();
        _cts = new CancellationTokenSource();

        try
        {
            RebuildAnnouncers();
            _isRunning = true;

            Task.Run(() => BroadcastLoop(_cts.Token));

            LumoraLogger.Log($"[lnl] LAN announcer started for session: {metadata.Name} ({_announcers.Count} broadcast sockets)");
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"[lnl] Failed to start LAN announcer: {ex.Message}");
            _isRunning = false;
        }
    }

    // (Re)create one broadcast socket bound to each up, non-loopback IPv4 interface, plus an unbound catch-all.
    // Rebuilt periodically so connecting Wi-Fi / unplugging Ethernet mid-session is handled. -xlinka
    private void RebuildAnnouncers()
    {
        foreach (var a in _announcers)
        {
            try { a.Close(); a.Dispose(); } catch { }
        }
        _announcers.Clear();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    try
                    {
                        _announcers.Add(new UdpClient(new IPEndPoint(ua.Address, 0)) { EnableBroadcast = true });
                    }
                    catch (Exception ex)
                    {
                        LumoraLogger.Debug($"[lnl] LAN announcer bind to {ua.Address} failed: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LumoraLogger.Debug($"[lnl] LAN announcer interface enumeration failed: {ex.Message}");
        }

        // Catch-all unbound socket (default route) for the case enumeration found nothing usable.
        try { _announcers.Add(new UdpClient { EnableBroadcast = true }); }
        catch (Exception ex) { LumoraLogger.Warn($"[lnl] LAN announcer fallback socket failed: {ex.Message}"); }
    }

    private async Task BroadcastLoop(CancellationToken token)
    {
        int ticks = 0;
        while (!token.IsCancellationRequested && _isRunning)
        {
            try
            {
                // Refresh the socket set every ~30s so a network change (Wi-Fi connects, Ethernet unplugged) is
                // picked up without restarting the session. Cheap - a handful of sockets.
                if (_announcers.Count == 0 || (++ticks % 6) == 0)
                {
                    RebuildAnnouncers();
                }

                var packet = CreateAnnouncementPacket();
                var targets = GetBroadcastEndpoints();

                // Send the announcement from EVERY interface-bound socket to the limited broadcast AND each
                // subnet's directed broadcast. A plain unbound socket only egresses ONE interface (the default
                // route), which on a typical machine is a Hyper-V / WSL / VPN virtual adapter - so the packet
                // never reaches the real LAN and nobody discovers the host. This blankets every attached subnet.
                // -xlinka
                foreach (var socket in _announcers)
                {
                    foreach (var endpoint in targets)
                    {
                        try
                        {
                            await socket.SendAsync(packet, packet.Length, endpoint);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (ObjectDisposedException) { throw; }
                        catch (Exception sendEx)
                        {
                            // One bad socket/interface shouldn't stop the others.
                            LumoraLogger.Debug($"[lnl] LAN broadcast to {endpoint} failed: {sendEx.Message}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"[lnl] LAN broadcast error: {ex.Message}");
            }

            try
            {
                await Task.Delay(BroadcastIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // Build the set of broadcast targets: the limited broadcast (255.255.255.255) PLUS each up, non-loopback
    // IPv4 interface's directed subnet broadcast. Deduped. This is what makes discovery reach every LAN the host
    // is actually on, regardless of which interface owns the default route. -xlinka
    private static List<IPEndPoint> GetBroadcastEndpoints()
    {
        var seen = new HashSet<string>();
        var endpoints = new List<IPEndPoint>();

        void Add(IPAddress address)
        {
            if (address == null) return;
            var key = address.ToString();
            if (seen.Add(key))
                endpoints.Add(new IPEndPoint(address, BroadcastPort));
        }

        Add(IPAddress.Broadcast);

        // Also target loopback so a SECOND process on the SAME machine reliably receives the announcement. A
        // broadcast egresses the NIC and isn't guaranteed to loop back to another local process's socket on
        // Windows; 127.0.0.1 always is delivered locally. Only the unbound catch-all socket can actually reach
        // loopback (the interface-bound sockets' loopback sends just fail+log harmlessly), which is enough. This
        // makes the standard "two clients, one PC" local test discoverable. -xlinka
        Add(IPAddress.Loopback);

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var directed = ComputeDirectedBroadcast(ua.Address, ua.IPv4Mask);
                    if (directed != null) Add(directed);
                }
            }
        }
        catch (Exception ex)
        {
            // Fall back to just the limited broadcast we already added.
            LumoraLogger.Debug($"[lnl] LAN broadcast: interface enumeration failed: {ex.Message}");
        }

        return endpoints;
    }

    // Directed broadcast address for a subnet = host IP OR'd with the inverted mask (e.g. 192.168.1.42 /
    // 255.255.255.0 -> 192.168.1.255). Returns null for unusable inputs (no/empty mask). -xlinka
    private static IPAddress? ComputeDirectedBroadcast(IPAddress address, IPAddress mask)
    {
        if (mask == null) return null;
        var ipBytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        if (ipBytes.Length != 4 || maskBytes.Length != 4) return null;

        bool maskUsable = false;
        var broadcast = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            broadcast[i] = (byte)(ipBytes[i] | (~maskBytes[i] & 0xFF));
            if (maskBytes[i] != 0) maskUsable = true;
        }

        return maskUsable ? new IPAddress(broadcast) : null;
    }

    private byte[] CreateAnnouncementPacket()
    {
        var announcement = new LANAnnouncement
        {
            AnnouncerId = _announcerId,
            Metadata = _metadata
        };

        string json = JsonSerializer.Serialize(announcement, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        byte[] data = Encoding.UTF8.GetBytes(json);
        return CompressData(data);
    }

    private static byte[] CompressData(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            gzip.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Update the metadata being broadcast.
    /// </summary>
    /// <param name="metadata">New metadata to broadcast</param>
    public void UpdateMetadata(SessionMetadata metadata)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    /// <summary>
    /// Stop broadcasting.
    /// </summary>
    public void StopAnnouncing()
    {
        if (!_isRunning)
            return;

        _isRunning = false;

        try
        {
            _cts?.Cancel();
        }
        catch { }

        foreach (var a in _announcers)
        {
            try { a.Close(); a.Dispose(); } catch { }
        }
        _announcers.Clear();

        _cts = null!;

        LumoraLogger.Log("[lnl] LAN announcer stopped");
    }

    /// <summary>
    /// Dispose and stop announcing.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        StopAnnouncing();
    }
}

/// <summary>
/// Data structure for LAN session announcements.
/// </summary>
public class LANAnnouncement
{
    /// <summary>
    /// Unique ID of the announcer for self-filtering.
    /// </summary>
    public Guid AnnouncerId { get; set; }

    /// <summary>
    /// Session metadata being announced.
    /// </summary>
    public SessionMetadata Metadata { get; set; } = null!;

    /// <summary>
    /// Protocol version for compatibility.
    /// </summary>
    public int ProtocolVersion { get; set; } = 1;
}

