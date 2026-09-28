// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// A date from its parts, marked UTC or local. A part out of range holds at the nearer end, the day at
// the last of its month, so 31 February 2024 is the 29th.
[Node("Time/Dates/Construct Date")]
public sealed class ConstructDate : ValueNode
{
    [return: Port("date")]
    public static DateTime Run(int year, int month, int day, int hour, int minute, int second, int millisecond, bool utc = true)
    {
        year = Math.Clamp(year, 1, 9999);
        month = Math.Clamp(month, 1, 12);
        day = Math.Clamp(day, 1, DateTime.DaysInMonth(year, month));
        return new DateTime(year, month, day, Math.Clamp(hour, 0, 23), Math.Clamp(minute, 0, 59), Math.Clamp(second, 0, 59),
            Math.Clamp(millisecond, 0, 999), utc ? DateTimeKind.Utc : DateTimeKind.Local);
    }
}
