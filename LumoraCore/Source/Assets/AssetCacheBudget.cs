// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lumora.Core.Logging;
using Lumora.Nexus.Cloud.Cdn;

namespace Lumora.Core.Assets;

// One size for everything this machine keeps only because it came from elsewhere: the download cache's
// files and the received files of the local database. Both are trimmed together, the file wanted longest
// ago going first whichever side it is on, until the two fit the Asset Cache setting. Files made or
// imported here are never counted or touched. -xlinka
public static class AssetCacheBudget
{
    private static readonly SemaphoreSlim Turn = new(1, 1);
    private static readonly TimeSpan FirstTrim = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan TrimEvery = TimeSpan.FromMinutes(5);
    private static Timer? _timer;
    private static long _applied = -1;

    public static long CapacityBytes => (long)EngineSettings.AssetCacheGigabytes << 30;

    // With the engine: the size goes to the download cache now and whenever the setting moves, and both
    // stores are trimmed shortly after start and every few minutes after.
    public static void Start()
    {
        Stop();
        Interlocked.Exchange(ref _applied, -1);
        EngineSettings.Changed += OnSettingsChanged;
        OnSettingsChanged();
        _timer = new Timer(_ => _ = TrimNowAsync(), null, FirstTrim, TrimEvery);
    }

    public static void Stop()
    {
        EngineSettings.Changed -= OnSettingsChanged;
        _timer?.Dispose();
        _timer = null;
    }

    // Every setting change lands here; only a new size does anything, and a smaller one trims at once.
    private static void OnSettingsChanged()
    {
        long capacity = CapacityBytes;
        if (Interlocked.Exchange(ref _applied, capacity) == capacity)
            return;
        var cache = Engine.Current?.ContentCache;
        if (cache != null)
            cache.CapacityBytes = capacity;
        _ = TrimNowAsync();
    }

    // Off the calling thread: a trim after the size drops can delete thousands of files.
    public static Task<int> TrimNowAsync()
    {
        var engine = Engine.Current;
        var cache = engine?.ContentCache;
        var db = engine?.LocalDB;
        long capacity = CapacityBytes;
        return Task.Run(() => TrimAsync(cache, db, capacity));
    }

    // Trims both stores to fit capacity between them. Answers how many files went.
    public static async Task<int> TrimAsync(ContentCache? cache, LocalDB? db, long capacity)
    {
        await Turn.WaitAsync().ConfigureAwait(false);
        try
        {
            // Before its index is read the database knows no files, so its side waits for a later turn.
            if (db != null && !db.Ready.IsCompletedSuccessfully)
                db = null;

            var files = new List<(DateTime Used, long Size, string Key, bool Received)>();
            long used = 0;
            if (cache != null)
            {
                foreach (var file in cache.Snapshot())
                {
                    used += file.Size;
                    if (file.Evictable)
                        files.Add((file.LastUsedUtc, file.Size, file.Key, false));
                }
            }
            if (db != null)
            {
                foreach (var file in db.GetReceivedFiles())
                {
                    used += file.Size;
                    if (file.Evictable)
                        files.Add((file.LastUsedUtc, file.Size, file.Key, true));
                }
            }
            if (used <= capacity)
                return 0;

            files.Sort((a, b) => a.Used.CompareTo(b.Used));
            int dropped = 0;
            bool released = false;
            foreach (var file in files)
            {
                if (used <= capacity)
                    break;
                bool gone = file.Received ? db!.TryReleaseReceived(file.Key) : cache!.TryEvict(file.Key);
                if (!gone)
                    continue;
                used -= file.Size;
                dropped++;
                released |= file.Received;
            }
            if (released)
                await db!.SaveRecordsAsync().ConfigureAwait(false);
            if (dropped > 0)
                Logger.Log($"AssetCacheBudget: let go of {dropped} cached file(s); {used >> 20} MB of {capacity >> 20} MB in use");
            return dropped;
        }
        catch (Exception ex)
        {
            Logger.Warn($"AssetCacheBudget: trim failed: {ex.Message}");
            return 0;
        }
        finally
        {
            Turn.Release();
        }
    }
}
