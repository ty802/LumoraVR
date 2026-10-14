// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// The lumora:rt functions a module imports, by function index, or -1 for one it does not need.
internal readonly record struct RuntimeImports(int Timer, int Now, int EachCount, int EachItem, int EachQuery, int Live = -1, int Word = -1,
    int CallOpen = -1, int CallEnd = -1, int Argument = -1, int FailClear = -1, int FailCheck = -1)
{
    public static readonly RuntimeImports None = new(-1, -1, -1, -1, -1);
}
