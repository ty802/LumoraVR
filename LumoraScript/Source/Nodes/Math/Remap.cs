// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// A value in one range carried to the same place in another, unclamped.
[Node("Math/Remap")]
public sealed class Remap : ValueNode
{
    public static float Run(float value, float fromMin, float fromMax, float toMin, float toMax)
        => fromMin == fromMax ? toMin : toMin + (value - fromMin) / (fromMax - fromMin) * (toMax - toMin);
    public static double Run(double value, double fromMin, double fromMax, double toMin, double toMax)
        => fromMin == fromMax ? toMin : toMin + (value - fromMin) / (fromMax - fromMin) * (toMax - toMin);
}
