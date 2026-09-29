// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// One minus red, green and blue; alpha kept.
[Node("Color/Invert")]
public sealed class InvertColor : ValueNode
{
    [return: Port("color")]
    public static color Run(color value) => new(1f - value.r, 1f - value.g, 1f - value.b, value.a);
}
