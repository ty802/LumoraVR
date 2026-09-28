// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// World space: each axis times every parent's.
[Node("World/Slots/Scale", Capability.WorldRead)]
public sealed class SlotScale : ReadNode
{
    [return: Port("scale")]
    public static float3 Run(ScriptContext ctx, Slot? slot)
        => ScriptReads.Visible(ctx, slot) ? slot!.GlobalScale : default;
}
