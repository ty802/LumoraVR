// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Components;

// One adapter per value type, made once.
public static class ClipTrackAdapters
{
    private static readonly Dictionary<Type, IClipTrackAdapter> Cache = new();

    public static IClipTrackAdapter For(Type valueType)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(valueType, out var adapter))
            {
                adapter = (IClipTrackAdapter)Activator.CreateInstance(typeof(ClipTrackAdapter<>).MakeGenericType(valueType))!;
                Cache[valueType] = adapter;
            }
            return adapter;
        }
    }
}
