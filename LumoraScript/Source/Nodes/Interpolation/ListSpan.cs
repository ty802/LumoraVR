// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Where a position falls along a run of spans: which span, and how far through it from 0 to 1. Whole
// numbers land on span ends, so position 2 is the start of span 2. Held to the ends, and NaN is the
// start, so a list lerp never reaches past its items or hands back a NaN of its own making.
internal static class ListSpan
{
    public static void Locate(int spans, float position, out int span, out float t)
    {
        span = 0;
        t = 0f;
        if (spans <= 0 || !(position > 0f))
            return;
        if (position >= spans)
        {
            span = spans - 1;
            t = 1f;
            return;
        }
        float whole = MathF.Floor(position);
        span = (int)whole;
        t = position - whole;
    }

    // The two items of a list position falls between and how far from the first. At the last item both
    // are the last item, so the end is the item exactly. False for no list or an empty one.
    public static bool Pair<T>(ScriptList<T>? list, float position, out T a, out T b, out float t)
    {
        a = default!;
        b = default!;
        t = 0f;
        int count = list?.Count ?? 0;
        if (count == 0)
            return false;
        Locate(count - 1, position, out int first, out t);
        int second = Math.Min(first + 1, count - 1);
        if (t >= 1f)
        {
            first = second;
            t = 0f;
        }
        var items = list!.Items;
        a = items[first];
        b = items[second];
        return true;
    }
}
