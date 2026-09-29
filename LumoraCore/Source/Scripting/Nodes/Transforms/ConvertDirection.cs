// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Carries a direction from one slot's space into another's with its length kept: a line drawn along it in
// the one space points along the answer in the other, however the slots are scaled. Where a flattened slot
// loses the line, the rotations alone carry it. A direction that is not a finite number gives zero. From
// and to work as they do on Convert Point.
[Node("World/Spaces/Convert Direction", Capability.WorldRead)]
public sealed class ConvertDirection : ReadNode
{
    [return: Port("converted")]
    public static float3 Run(ScriptContext ctx, Slot? from, Slot? to, float3 direction)
        => ScriptSpaces.Direction(ScriptSpaces.Space(ctx, from), ScriptSpaces.Space(ctx, to), direction);
}
