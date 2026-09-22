// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

public static class Oscillation
{
    // 0 at the minimum and 1 at the maximum. The clock is wall-clock anchored, so seconds is a number
    // in the tens of billions; cast to float that has a resolution of thousands of seconds and the sine
    // freezes at one value. Reduce the angle in double first and only then drop to float.
    public static float Unit(double seconds, float speed, float phase)
    {
        double angle = (seconds * speed + phase) % (2.0 * System.Math.PI);
        return (float)((System.Math.Sin(angle) + 1.0) * 0.5);
    }
}
