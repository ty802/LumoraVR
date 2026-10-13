// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Lumora.Nexus.Cloud.Cdn;

// Every transfer between this client and the content service, while it is in flight: what is
// coming down, what is going up, how far along each is. The client reports into it from its own
// fetch and upload loops, so a caller never has to wire a progress reporter to be seen. The Debug
// screen lists it and the in-world load readout sums it. Finished rows drop out on their own. -xlinka
public static class TransferRegistry
{
    public sealed class Entry
    {
        public string Hash = "";
        public bool IsUpload;
        public long TotalBytes;
        public long TransferredBytes;
        public DateTime StartedUtc = DateTime.UtcNow;
        public double Fraction => TotalBytes > 0 ? Math.Clamp((double)TransferredBytes / TotalBytes, 0, 1) : 0;
    }

    private static readonly ConcurrentDictionary<string, Entry> _active = new(StringComparer.Ordinal);
    private static long _completedDownloads;
    private static long _completedUploads;

    public static int ActiveCount => _active.Count;

    public static long CompletedDownloads => System.Threading.Interlocked.Read(ref _completedDownloads);
    public static long CompletedUploads => System.Threading.Interlocked.Read(ref _completedUploads);

    private static string Key(string hash, bool upload) => (upload ? "up|" : "down|") + hash;

    // Called by the client on every progress step. A terminal state removes the row.
    public static void Report(TransferProgress state, bool upload)
    {
        if (state == null || string.IsNullOrEmpty(state.Hash))
            return;
        var key = Key(state.Hash, upload);
        switch (state.State)
        {
            case TransferState.Queued:
            case TransferState.Active:
            {
                var entry = _active.GetOrAdd(key, _ => new Entry { Hash = state.Hash, IsUpload = upload });
                if (state.TotalBytes > 0)
                    entry.TotalBytes = state.TotalBytes;
                if (state.TransferredBytes > entry.TransferredBytes)
                    entry.TransferredBytes = state.TransferredBytes;
                break;
            }
            default:
            {
                if (_active.TryRemove(key, out _) && state.State == TransferState.Completed)
                {
                    if (upload) System.Threading.Interlocked.Increment(ref _completedUploads);
                    else System.Threading.Interlocked.Increment(ref _completedDownloads);
                }
                break;
            }
        }
    }

    // For paths that stream without byte progress: mark a download as in flight, then done.
    public static void BeginDownload(string hash, long totalBytes = 0)
        => Report(new TransferProgress { Hash = hash, TotalBytes = totalBytes, State = TransferState.Active }, upload: false);

    public static void EndDownload(string hash, bool ok)
        => Report(new TransferProgress { Hash = hash, State = ok ? TransferState.Completed : TransferState.Error }, upload: false);

    // One download, for a readout that follows particular assets: the key comes from DownloadKey with the hash
    // it is filed under (a content hash, or the hash of a link), worked out once by the follower. Takes no
    // lock and copies nothing.
    public static string DownloadKey(string hash) => Key(hash, upload: false);

    public static bool TryGetDownload(string downloadKey, out long transferred, out long total)
    {
        if (!string.IsNullOrEmpty(downloadKey) && _active.TryGetValue(downloadKey, out var entry))
        {
            transferred = entry.TransferredBytes;
            total = entry.TotalBytes;
            return true;
        }
        transferred = total = 0;
        return false;
    }

    public static List<Entry> Snapshot()
    {
        var list = new List<Entry>();
        foreach (var pair in _active)
            list.Add(pair.Value);
        list.Sort((a, b) => a.StartedUtc.CompareTo(b.StartedUtc));
        return list;
    }

    // Downloads in flight and the bytes still to come, for a readout in the world.
    // Read every frame by the world's load readout. Values and Count take every lock in the dictionary and
    // Values copies it besides; walking the pairs takes none. -xlinka
    public static (int downloads, int uploads, long remainingBytes, long totalBytes) Totals()
    {
        int downloads = 0, uploads = 0;
        long remaining = 0, total = 0;
        foreach (var pair in _active)
        {
            var entry = pair.Value;
            if (entry.IsUpload) uploads++; else downloads++;
            total += entry.TotalBytes;
            remaining += Math.Max(0, entry.TotalBytes - entry.TransferredBytes);
        }
        return (downloads, uploads, remaining, total);
    }
}
