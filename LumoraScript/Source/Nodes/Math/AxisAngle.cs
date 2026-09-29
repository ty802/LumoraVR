// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Script;

// A turn of degrees about axis, the same way From Euler turns about each of its axes. The axis's length does
// not matter; one of no length turns nothing.
[Node("Math/Rotations/Axis Angle")]
public sealed class AxisAngle : ValueNode
{
    [return: Port("rotation")]
    public static floatQ Run(float3 axis, float degrees) => RotationMath.FromAxis(axis.x, axis.y, axis.z, degrees * (Math.PI / 180d));
}
