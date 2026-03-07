// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core;

/// <summary>
/// Delegate for intercepting reference set operations.
/// </summary>
public delegate void RefSetHookDelegate<T>(SyncRef<T> syncRef, T value) where T : class, IWorldElement;

/// <summary>
/// Hook for intercepting SyncRef modifications.
/// </summary>
public class RefHook<T> : FieldHook<RefID> where T : class, IWorldElement
{
    /// <summary>
    /// Hook called when Target is set.
    /// </summary>
    public RefSetHookDelegate<T> RefSetHook { get; set; }

    public RefHook(World world) : base(world)
    {
    }
}
