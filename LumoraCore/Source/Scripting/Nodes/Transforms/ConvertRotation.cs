// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Carries a rotation from one slot's space into another's: the same turn, as the other slot sees it. From
// and to work as they do on Convert Point.
[Node("World/Spaces/Convert Rotation", Capability.WorldRead)]
public sealed class ConvertRotation : ReadNode
{
    [return: Port("converted")]
    public static floatQ Run(ScriptContext ctx, Slot? from, Slot? to, floatQ rotation)
        => ScriptSpaces.Rotation(ScriptSpaces.Space(ctx, from), ScriptSpaces.Space(ctx, to), rotation);
}
