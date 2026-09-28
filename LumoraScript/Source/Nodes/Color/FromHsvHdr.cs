// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Hue goes round 0 to 1; saturation and alpha 0 to 1. Brightness runs from 0 up, and past 1 is
// brighter than white.
[Node("Color/From HSV HDR")]
public sealed class FromHsvHdr : ValueNode
{
    [return: Port("color")]
    public static colorHDR Run(float hue, float saturation, float brightness, float alpha = 1f)
        => colorHDR.FromHSV(hue, saturation, brightness, alpha);
}
