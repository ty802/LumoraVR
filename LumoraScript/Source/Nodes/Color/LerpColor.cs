// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

[Node("Color/Lerp Color")]
public sealed class LerpColor : ValueNode
{
    [return: Port("color")]
    public static color Run(color a, color b, float t) => color.Lerp(a, b, t);
}
