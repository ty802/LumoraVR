// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// From min up to but not including max; min when the range is empty.
[Node("Math/Random/Random Int")]
public sealed class RandomInt : ReadNode
{
    public static int Run(ScriptContextBase ctx, int min = 0, int max = 100)
        => max <= min ? min : ctx.Random.Next(min, max);
}
