// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// What the date and time math shares. A result past the first or last date there is, or past the
// longest span of time either way, holds at that end where DateTime and TimeSpan would throw; the sum
// is taken wide first, so nothing wraps on the way. -xlinka
internal static class ClampedTime
{
    public static DateTime Date(Int128 ticks, DateTimeKind kind)
        => new((long)Int128.Clamp(ticks, 0, DateTime.MaxValue.Ticks), kind);

    public static TimeSpan Span(Int128 ticks)
        => new((long)Int128.Clamp(ticks, long.MinValue, long.MaxValue));

    // Rounded to the nearest tick, halves to even. NaN has no length and gives zero.
    public static TimeSpan Span(double ticks)
    {
        ticks = Math.Round(ticks);
        if (double.IsNaN(ticks))
            return TimeSpan.Zero;
        if (ticks >= long.MaxValue)
            return TimeSpan.MaxValue;
        if (ticks <= long.MinValue)
            return TimeSpan.MinValue;
        return new TimeSpan((long)ticks);
    }
}
