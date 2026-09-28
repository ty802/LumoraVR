// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// What the date and span nodes share. An amount worked out in floating point is rounded to the nearest
// tick and held inside the range a date or a span can take, where the runtime's own conversions would
// throw or cut a tick short; NaN counts as none. -xlinka
internal static class TimeTicks
{
    public static readonly long Epoch = DateTime.UnixEpoch.Ticks;
    public static readonly long LastDate = DateTime.MaxValue.Ticks;

    public static long Clamp(double ticks, long min, long max)
    {
        if (double.IsNaN(ticks))
            return Math.Clamp(0L, min, max);
        if (ticks <= min)
            return min;
        if (ticks >= max)
            return max;
        return (long)Math.Round(ticks);
    }

    public static TimeSpan Span(double ticks) => new(Clamp(ticks, long.MinValue, long.MaxValue));

    // The UTC date so many ticks from 1 January 1970.
    public static DateTime FromUnix(double ticks) => new(Epoch + Clamp(ticks, -Epoch, LastDate - Epoch), DateTimeKind.Utc);
}
