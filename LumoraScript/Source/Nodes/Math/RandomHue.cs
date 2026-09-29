// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// A random hue at the saturation and brightness given, both 0 to 1 and full unless set; alpha 1.
[Node("Math/Random/Random Hue")]
public sealed class RandomHue : ReadNode
{
    [return: Port("color")]
    public static color Run(ScriptContextBase ctx, float saturation = 1f, float brightness = 1f)
        => ColorModels.FromHsv(ctx.Random.NextSingle(), saturation, brightness, 1f);
}
