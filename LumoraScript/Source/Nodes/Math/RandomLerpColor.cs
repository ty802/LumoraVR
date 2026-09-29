// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// A random color on the straight line from a to b: every channel mixed by the same random amount, from 0
// up to but not including 1. For an even mix of light, To Linear both ends first and To sRGB the result.
[Node("Math/Random/Random Lerp Color")]
public sealed class RandomLerpColor : ReadNode
{
    [return: Port("color")]
    public static color Run(ScriptContextBase ctx, color a, color b) => color.Lerp(a, b, ctx.Random.NextSingle());

    [return: Port("color")]
    public static colorHDR Run(ScriptContextBase ctx, colorHDR a, colorHDR b)
        => colorHDR.Lerp(a, b, ctx.Random.NextSingle());
}
