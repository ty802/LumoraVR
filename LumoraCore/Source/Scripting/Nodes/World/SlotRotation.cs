// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// World space.
[Node("World/Slots/Rotation", Capability.WorldRead)]
public sealed class SlotRotation : ReadNode
{
    [return: Port("rotation")]
    public static floatQ Run(ScriptContext ctx, Slot? slot)
        => ScriptReads.Visible(ctx, slot) ? slot!.GlobalRotation : floatQ.Identity;
}
