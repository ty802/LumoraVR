// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;

namespace Lumora.Core;

// Reaches personal machine/account state, so only the userspace world may hold it. Subclasses
// inherit it; it is type metadata, never a synced slot tag.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class UserspaceOnlyAttribute : Attribute { }

public static class ComponentExecutionPolicy
{
    private static readonly ConcurrentDictionary<Type, bool> RestrictedTypes = new();

    public static bool IsUserspaceOnly(Type type)
        => RestrictedTypes.GetOrAdd(type, static t => t.IsDefined(typeof(UserspaceOnlyAttribute), inherit: true));

    public static bool IsUserspace(World? world)
        => world is { IsUserspace: true, IsDestroyed: false, AuthorityID: -1, LocalID: -1, Session: null };

    public static bool CanAttach(Type type, World? world)
        => !IsUserspaceOnly(type) || IsUserspace(world);

    internal static void Demand(Type type, World? world)
    {
        if (!CanAttach(type, world))
            throw new InvalidOperationException($"{type.Name} is userspace-only and cannot run in a world.");
    }
}
