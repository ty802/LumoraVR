// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// Turns and the axes they turn about, worked out in double so a tiny turn keeps its axis. A rotation is read
// the shorter way round: a quaternion and its negative turn a thing the same, and the one kept has w of 0 or
// more, which puts its angle between 0 and 180 degrees. -xlinka
internal static class RotationMath
{
    // A turn of radians about axis. An axis of no length, or none that is finite, turns nothing.
    public static floatQ FromAxis(double x, double y, double z, double radians)
    {
        double length = Math.Sqrt(x * x + y * y + z * z);
        if (!(length > 0d) || double.IsInfinity(length))
            return floatQ.Identity;
        double s = Math.Sin(radians * 0.5d) / length;
        return new floatQ((float)(x * s), (float)(y * s), (float)(z * s), (float)Math.Cos(radians * 0.5d));
    }

    // The axis, of length one, and the angle in radians, 0 to pi. No turn at all has no axis of its own and
    // gets +z, forward.
    public static void ToAxis(floatQ rotation, out double x, out double y, out double z, out double radians)
    {
        double qx = rotation.x, qy = rotation.y, qz = rotation.z, qw = rotation.w;
        if (qw < 0d)
        {
            qx = -qx;
            qy = -qy;
            qz = -qz;
            qw = -qw;
        }
        double sine = Math.Sqrt(qx * qx + qy * qy + qz * qz);
        if (!(sine > 0d) || double.IsInfinity(sine))
        {
            (x, y, z, radians) = (0d, 0d, 1d, 0d);
            return;
        }
        x = qx / sine;
        y = qy / sine;
        z = qz / sine;
        radians = 2d * Math.Atan2(sine, qw);
    }
}
