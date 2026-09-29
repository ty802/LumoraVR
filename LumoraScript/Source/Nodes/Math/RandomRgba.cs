// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// Red, green, blue and alpha each random, from 0 up to but not including 1.
[Node("Math/Random/Random RGBA")]
public sealed class RandomRgba : ReadNode
{
    [return: Port("color")]
    public static color Run(ScriptContextBase ctx)
    {
        var random = ctx.Random;
        return new color(random.NextSingle(), random.NextSingle(), random.NextSingle(), random.NextSingle());
    }
}
