// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Threading;

namespace Lumora.Core;

// Collections paced to the frame. Left alone the runtime lets tens of megabytes pile up before it collects,
// and with a world load's worth of new objects still alive, copying them out was a 30 to 90 ms stop. A young
// collection at the end of any frame that allocated a few megabytes since the last one keeps each to a few
// milliseconds; the total work is the same. Measured on saved world loads: worst pause 78 ms to 24 ms, heap
// less than half. -xlinka
public static class GcPacer
{
    public const long BudgetBytes = 4L * 1024 * 1024;

    private static long _allocatedAt = -1;
    private static int _youngSeen = -1;

    // ONE COLLECTION THAT GIVES MEMORY BACK, ONCE A WORLD HAS ARRIVED.
    //
    // A world's arrival leaves its big buffers dead: decoded textures handed to the renderer, gathered
    // files, maps grown past their size. A full collection frees them, but the runtime keeps the memory
    // they sat in, so after a big world the process held several times its live size, gigabytes for
    // nothing. Once a world has arrived, no world is still loading and no asset has loaded for a moment,
    // one aggressive collection compacts everything and returns what is free to the system. It runs only
    // after an arrival that allocated a lot, and never while anything is still arriving, so play does not
    // pay for it. Measured loading a three hundred texture world headless: 7.2 GB private to 0.4 GB, in one
    // pause of about half a second as the load finished. -xlinka
    private const long QuietMs = 1500;

    private static int _arrivalPending;
    private static long _arrivalAllocatedAt;
    private static long _quietSince = -1;

    private static long ArrivalWorthSettling => Engine.UseConstrainedResources ? 128L << 20 : 512L << 20;

    public static void WorldArriving()
    {
        if (Interlocked.Exchange(ref _arrivalPending, 1) == 0)
            Volatile.Write(ref _arrivalAllocatedAt, GC.GetTotalAllocatedBytes(false));
        Volatile.Write(ref _quietSince, -1);
    }

    private static void SettleAfterArrival()
    {
        var manager = Engine.Current?.WorldManager;
        if (Assets.Asset.LoadingCount > 0 || (manager != null && manager.AnyWorldLoading()))
        {
            _quietSince = -1;
            return;
        }
        long now = Environment.TickCount64;
        if (_quietSince < 0)
        {
            _quietSince = now;
            return;
        }
        if (now - _quietSince < QuietMs)
            return;

        Volatile.Write(ref _arrivalPending, 0);
        _quietSince = -1;
        if (GC.GetTotalAllocatedBytes(false) - Volatile.Read(ref _arrivalAllocatedAt) < ArrivalWorthSettling)
            return;
        var info = GC.GetGCMemoryInfo();

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        long ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000 / System.Diagnostics.Stopwatch.Frequency;
        var after = GC.GetGCMemoryInfo();
        Logging.Logger.Log($"GcPacer: a world arrived; returned {(info.TotalCommittedBytes - after.TotalCommittedBytes) >> 20} MB "
                         + $"({info.TotalCommittedBytes >> 20} MB held, {after.HeapSizeBytes >> 20} MB live) in {ms} ms");
    }

    public static void EndFrame()
    {
        if (Volatile.Read(ref _arrivalPending) != 0)
            SettleAfterArrival();

        long allocated = GC.GetTotalAllocatedBytes(false);
        int young = GC.CollectionCount(0);
        // The first frame, or the runtime collected on its own since the last look: count from here.
        if (_allocatedAt < 0 || young != _youngSeen)
        {
            _allocatedAt = allocated;
            _youngSeen = young;
            return;
        }
        if (allocated - _allocatedAt < BudgetBytes)
            return;
        GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: false);
        _allocatedAt = GC.GetTotalAllocatedBytes(false);
        _youngSeen = GC.CollectionCount(0);
    }
}
