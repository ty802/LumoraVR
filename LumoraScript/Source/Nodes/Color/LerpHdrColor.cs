// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

[Node("Color/Lerp HDR Color")]
public sealed class LerpHdrColor : ValueNode
{
    [return: Port("color")]
    public static colorHDR Run(colorHDR a, colorHDR b, float t) => colorHDR.Lerp(a, b, t);
}
