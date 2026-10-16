// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;
using Godot;

namespace Lumora.Godot.Hooks;

// Where a renderer is drawn while a moving body places its instance on the render server directly, past the
// node it sits under (RigidBodyHook). Anything that copies a renderer's pose off its node, a mirror's private
// scene for one, asks here first. Kept only while something reads it.
public static class DirectPoses
{
    private static readonly Dictionary<ulong, Transform3D> Poses = new();
    private static int _readers;
    private static int _count;

    public static bool Wanted => Volatile.Read(ref _readers) > 0;

    // Counts renderers letting go of their instances. A body drawing an instance itself looks at it again only
    // when this moved, rather than asking every frame whether the instance still exists.
    internal static int InstancesFreed;

    public static bool Any => Volatile.Read(ref _count) > 0;

    public static void AddReader() => Interlocked.Increment(ref _readers);

    public static void RemoveReader()
    {
        if (Interlocked.Decrement(ref _readers) <= 0)
        {
            Volatile.Write(ref _readers, 0);
            lock (Poses)
            {
                Poses.Clear();
                Volatile.Write(ref _count, 0);
            }
        }
    }

    internal static void Set(ulong instanceId, in Transform3D pose)
    {
        lock (Poses)
        {
            Poses[instanceId] = pose;
            Volatile.Write(ref _count, Poses.Count);
        }
    }

    internal static void Clear(ulong instanceId)
    {
        lock (Poses)
        {
            Poses.Remove(instanceId);
            Volatile.Write(ref _count, Poses.Count);
        }
    }

    public static bool TryGet(ulong instanceId, out Transform3D pose)
    {
        lock (Poses)
            return Poses.TryGetValue(instanceId, out pose);
    }
}
