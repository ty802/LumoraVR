// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Lumora.Core.Logging;

namespace Lumora.Core.Assets;

// Pixels of loaded textures, decoded again for whoever reads them.
//
// A loaded picture lets go of its decoded pixels once the renderer has its own copy: a world of three
// hundred textures held gigabytes of them otherwise, read by nothing. The few readers (script pixel
// nodes, the six-face cube assembler) come here instead. A read that finds nothing starts one decode
// of the file the picture came from, off the world thread, and answers null until it lands; a read
// that finds it keeps it warm. A copy nobody read for thirty seconds goes. Past the budget the longest
// unread go first, but never one read in the last two seconds, so a script sampling more than the
// budget every frame costs memory instead of a decode a frame. A decode that failed waits ten seconds
// before it is tried again.
//
// Procedural textures never come through here for long: they keep their pixels, having no file to
// decode them from. -xlinka
public static class TexturePixelCache
{
    private sealed class Entry
    {
        public TexturePixels? Pixels;
        public Task<TexturePixels?>? Pending;
        public int Version;
        public long LastRead;
        public long FailedAt = -1;
    }

    private static readonly Dictionary<TextureAsset, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private static readonly List<TextureAsset> _sweep = new();
    private static readonly object _lock = new();
    private static long _bytes;
    private static int _count;
    private static long _lastSweep;

    private static long _idleMs = 30_000;
    private static long _activeMs = 2_000;
    private static long _retryMs = 10_000;

    public static long BudgetBytes => Engine.UseConstrainedResources ? 64L << 20 : 256L << 20;

    public static long ResidentBytes
    {
        get { lock (_lock) return _bytes; }
    }

    public static int Count => System.Threading.Volatile.Read(ref _count);

    internal static TexturePixels? TryGet(TextureAsset texture)
    {
        var held = texture.HeldPixels();
        if (held != null)
            return held;
        if (!texture.CanDecodeAgain)
            return null;

        long now = Environment.TickCount64;
        lock (_lock)
        {
            var entry = Current(texture, now);
            if (entry?.Pixels != null)
            {
                entry.LastRead = now;
                return entry.Pixels;
            }
            if (entry == null)
                Start(texture, now);
            return null;
        }
    }

    internal static Task<TexturePixels?> GetAsync(TextureAsset texture)
    {
        var held = texture.HeldPixels();
        if (held != null)
            return Task.FromResult<TexturePixels?>(held);
        if (!texture.CanDecodeAgain)
            return Task.FromResult<TexturePixels?>(null);

        long now = Environment.TickCount64;
        lock (_lock)
        {
            var entry = Current(texture, now);
            if (entry?.Pixels != null)
            {
                entry.LastRead = now;
                return Task.FromResult<TexturePixels?>(entry.Pixels);
            }
            if (entry?.Pending != null)
                return entry.Pending;
            if (entry != null)
                return Task.FromResult<TexturePixels?>(null);
            return Start(texture, now).Pending!;
        }
    }

    // The entry still good for the texture as it is now, or null to start one. A failed one counts as
    // good until its retry time, so a missing file is not decoded again every frame.
    private static Entry? Current(TextureAsset texture, long now)
    {
        if (!_entries.TryGetValue(texture, out var entry))
            return null;
        if (entry.Version != texture.Version || (entry.FailedAt >= 0 && now - entry.FailedAt >= _retryMs))
        {
            RemoveLocked(texture, entry);
            return null;
        }
        return entry;
    }

    private static Entry Start(TextureAsset texture, long now)
    {
        var entry = new Entry { Version = texture.Version, LastRead = now };
        _entries[texture] = entry;
        _count = _entries.Count;
        entry.Pending = Task.Run(() => FetchAsync(texture, entry));
        return entry;
    }

    private static async Task<TexturePixels?> FetchAsync(TextureAsset texture, Entry entry)
    {
        TexturePixels? pixels = null;
        try
        {
            pixels = await texture.DecodeAgainAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"TexturePixelCache: reading {texture.AssetURL} back failed: {ex.Message}");
        }

        lock (_lock)
        {
            entry.Pending = null;
            bool current = _entries.TryGetValue(texture, out var now) && ReferenceEquals(now, entry);
            if (pixels == null || pixels.Version != entry.Version)
            {
                entry.FailedAt = Environment.TickCount64;
                return null;
            }
            if (current)
            {
                entry.Pixels = pixels;
                entry.LastRead = Environment.TickCount64;
                _bytes += pixels.Bytes;
                TrimLocked(entry);
            }
        }
        return pixels;
    }

    private static void TrimLocked(Entry keep)
    {
        long now = Environment.TickCount64;
        long budget = BudgetBytes;
        while (_bytes > budget)
        {
            TextureAsset? oldest = null;
            Entry? oldestEntry = null;
            foreach (var (texture, entry) in _entries)
            {
                if (entry == keep || entry.Pixels == null || now - entry.LastRead < _activeMs)
                    continue;
                if (oldestEntry == null || entry.LastRead < oldestEntry.LastRead)
                {
                    oldest = texture;
                    oldestEntry = entry;
                }
            }
            if (oldest == null)
                return;
            RemoveLocked(oldest, oldestEntry!);
        }
    }

    private static void RemoveLocked(TextureAsset texture, Entry entry)
    {
        _entries.Remove(texture);
        _count = _entries.Count;
        if (entry.Pixels != null)
            _bytes -= entry.Pixels.Bytes;
        entry.Pixels = null;
    }

    // Unloaded textures leave at once; anything else waits for its idle time.
    internal static void Forget(TextureAsset texture)
    {
        if (Count == 0)
            return;
        lock (_lock)
        {
            if (_entries.TryGetValue(texture, out var entry))
                RemoveLocked(texture, entry);
        }
    }

    // Once a second at most, from the asset manager's update. Free when nothing is cached.
    internal static void Tick()
    {
        if (Count == 0)
            return;
        long now = Environment.TickCount64;
        if (now - _lastSweep < 1000)
            return;
        _lastSweep = now;

        lock (_lock)
        {
            _sweep.Clear();
            foreach (var (texture, entry) in _entries)
            {
                if (entry.Pending == null && now - entry.LastRead >= _idleMs)
                    _sweep.Add(texture);
            }
            foreach (var texture in _sweep)
                RemoveLocked(texture, _entries[texture]);
            _sweep.Clear();
        }
    }
}
