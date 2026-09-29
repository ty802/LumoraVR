// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Script;

// A random rotation on the shorter turn from a to b, every angle along it as likely. A zero rotation
// counts as no turn.
[Node("Math/Random/Random Slerp")]
public sealed class RandomSlerp : ReadNode
{
    [return: Port("rotation")]
    public static floatQ Run(ScriptContextBase ctx, floatQ a, floatQ b) => SphericalBlend.Rotations(a, b, ctx.Random.NextSingle());
}
