// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// Motion slowed by drag in proportion to its speed, drag k a second, over t seconds, with u = k * t. Moving
// on its own it covers v * t * Coast(u), and a steady pull g adds g * t * t * Fall(u). Both are written so
// that no drag at all is the plain v * t and g * t * t / 2 rather than 0 over 0, and so that a small drag
// keeps its digits. -xlinka
internal static class DragMath
{
    // (1 - e^-u) / u, 1 at u = 0.
    public static double Coast(double u) => u == 0d ? 1d : -double.ExpM1(-u) / u;

    // (u - 1 + e^-u) / u^2, 1/2 at u = 0. Close to 0 the terms cancel, so the series takes over near 0.
    public static double Fall(double u)
    {
        if (Math.Abs(u) < 1e-4d)
            return 0.5d - u / 6d + u * u / 24d;
        return (u + double.ExpM1(-u)) / (u * u);
    }
}
