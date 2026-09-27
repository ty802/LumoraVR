// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// The lumora:rt functions a module imports, by function index, or -1 for one it does not need.
internal readonly record struct RuntimeImports(int Timer, int Now, int EachCount, int EachItem, int EachQuery)
{
    public static readonly RuntimeImports None = new(-1, -1, -1, -1, -1);
}
