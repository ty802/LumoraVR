// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Nexus.Cloud;
using Lumora.Nexus.Transport;
using LumoraLogger = Lumora.Nexus.Diagnostics.NexusLog;

namespace Lumora.Nexus.Discovery;

public class LANDiscovery : IDisposable
{
    public const int ListenPort = LANAnnouncer.BroadcastPort;

    public const int SessionTimeoutSeconds = 15;

    public const int CleanupIntervalMs = 10000;

    private UdpClient _listener = null!;
    private CancellationTokenSource _cts = null!;
    private readonly Dictionary<string, DiscoveredSession> _sessions = new();
    private readonly object _sessionsLock = new();
    private Guid _localAnnouncerId;
    private bool _isRunning;
    private bool _isDisposed;

    public event Action<DiscoveredSession> SessionFound = null!;

    public event Action<string> SessionLost = null!;

    public event Action<DiscoveredSession> SessionUpdated = null!;

    public IReadOnlyCollection<DiscoveredSession> Sessions
    {
        get
        {
            lock (_sessionsLock)
            {
                return _sessions.Values.ToList();
            }
        }
    }

    public int SessionCount
    {
        get
        {
            lock (_sessionsLock)
            {
                return _sessions.Count;
            }
        }
    }

    public bool IsRunning => _isRunning;

    // ignoreAnnouncerId filters out our own broadcasts.
    public void StartDiscovery(Guid? ignoreAnnouncerId = null)
    {
        if (_isRunning || _isDisposed)
            return;

        _localAnnouncerId = ignoreAnnouncerId ?? Guid.Empty;
        _cts = new CancellationTokenSource();

        try
        {
            _listener = new UdpClient();
            _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Client.Bind(new IPEndPoint(IPAddress.Any, ListenPort));
            _isRunning = true;

            Task.Run(() => ListenLoop(_cts.Token));
            Task.Run(() => CleanupLoop(_cts.Token));

            LumoraLogger.Log("[lnl] LAN discovery started");
        }
        catch (Exception ex)
        {
            LumoraLogger.Error($"[lnl] Failed to start LAN discovery: {ex.Message}");
            _isRunning = false;
        }
    }

    private async Task ListenLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isRunning)
        {
            try
            {
                var result = await _listener.ReceiveAsync(token);
                ProcessAnnouncement(result.Buffer, result.RemoteEndPoint);
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
                if (_isRunning)
                {
                    LumoraLogger.Warn($"[lnl] LAN discovery receive error: {ex.Message}");
                }
            }
        }
    }

    private void ProcessAnnouncement(byte[] data, IPEndPoint source)
    {
        // Ignore non-GZip packets - other apps may share the
        // same UDP discovery port and broadcast in their own binary format.
        if (data.Length < 2 || data[0] != 0x1F || data[1] != 0x8B)
            return;

        try
        {
            byte[] decompressed = DecompressData(data);
            string json = Encoding.UTF8.GetString(decompressed);

            var announcement = JsonSerializer.Deserialize<LANAnnouncement>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (announcement?.Metadata == null)
                return;

            if (announcement.AnnouncerId == _localAnnouncerId)
                return;

            var session = new DiscoveredSession
            {
                Metadata = announcement.Metadata,
                SourceIP = source.Address,
                LastSeen = DateTime.UtcNow,
                AnnouncerId = announcement.AnnouncerId
            };

            string key = announcement.Metadata.SessionId;
            if (string.IsNullOrEmpty(key))
                return;

            bool isNew;
            lock (_sessionsLock)
            {
                isNew = !_sessions.ContainsKey(key);
                _sessions[key] = session;
            }

            if (isNew)
            {
                LumoraLogger.Log($"[lnl] Discovered session: {session.Metadata.Name} at {source.Address}");
                SessionFound?.Invoke(session);
            }
            else
            {
                SessionUpdated?.Invoke(session);
            }
        }
        catch (Exception ex)
        {
            var preview = data.Length > 8
                ? BitConverter.ToString(data, 0, 8) + "..."
                : BitConverter.ToString(data);
            LumoraLogger.Warn($"[lnl] Failed to process announcement from {source}: {ex.Message} (first bytes={preview}, len={data.Length})");
        }
    }

    private static byte[] DecompressData(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        // Bound the decompressed size so a tiny crafted GZip payload cannot expand
        // into a multi-GB allocation (zip-bomb DoS). Anyone broadcasting on the LAN
        // discovery port can otherwise OOM every listener with one packet.
        var buffer = new byte[8192];
        int read;
        long total = 0;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > NetworkLimits.MaxLanAnnouncementBytes)
                throw new InvalidDataException($"LAN announcement decompressed past cap {NetworkLimits.MaxLanAnnouncementBytes} bytes.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private async Task CleanupLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isRunning)
        {
            try
            {
                await Task.Delay(CleanupIntervalMs, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var cutoff = DateTime.UtcNow.AddSeconds(-SessionTimeoutSeconds);
            List<string> expired;

            lock (_sessionsLock)
            {
                expired = _sessions
                    .Where(kv => kv.Value.LastSeen < cutoff)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var key in expired)
                {
                    _sessions.Remove(key);
                }
            }

            foreach (var key in expired)
            {
                LumoraLogger.Log($"[lnl] Session lost: {key}");
                SessionLost?.Invoke(key);
            }
        }
    }

    public DiscoveredSession GetSession(string sessionId)
    {
        lock (_sessionsLock)
        {
            return (_sessions.TryGetValue(sessionId, out var session) ? session : null) ?? null!;
        }
    }

    public void ClearSessions()
    {
        lock (_sessionsLock)
        {
            _sessions.Clear();
        }
    }

    public void StopDiscovery()
    {
        if (!_isRunning)
            return;

        _isRunning = false;

        try
        {
            _cts?.Cancel();
        }
        catch { }

        try
        {
            _listener?.Close();
            _listener?.Dispose();
        }
        catch { }

        _listener = null!;
        _cts = null!;

        LumoraLogger.Log("[lnl] LAN discovery stopped");
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        StopDiscovery();

        lock (_sessionsLock)
        {
            _sessions.Clear();
        }
    }
}

public class DiscoveredSession
{
    public SessionMetadata Metadata { get; set; } = null!;

    public IPAddress SourceIP { get; set; } = null!;

    public DateTime LastSeen { get; set; }

    public Guid AnnouncerId { get; set; }

    // Falls back to the source IP when the announced URLs do not include it.
    public Uri GetConnectionUrl()
    {
        var matchingUrl = Metadata?.SessionURLs?.FirstOrDefault(u =>
            u.Host == SourceIP?.ToString());

        if (matchingUrl != null)
            return matchingUrl;

        if (Metadata?.SessionURLs?.Count > 0)
            return Metadata.SessionURLs[0];

        if (SourceIP != null && !string.IsNullOrEmpty(Metadata?.SessionId))
        {
            return SessionUrlBuilder.BuildLNLUrl(
                SourceIP.ToString(),
                SessionUrlBuilder.DefaultPort,
                Metadata.SessionId);
        }

        return null!;
    }
}

