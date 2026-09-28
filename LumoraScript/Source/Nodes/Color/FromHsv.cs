// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Hue goes round 0 to 1; saturation, brightness and alpha 0 to 1.
[Node("Color/From HSV")]
public sealed class FromHsv : ValueNode
{
    [return: Port("color")]
    public static color Run(float hue, float saturation, float brightness, float alpha = 1f)
        => color.FromHSV(hue, saturation, brightness, alpha);
}
