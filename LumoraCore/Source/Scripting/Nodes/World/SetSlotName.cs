// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

[Node("World/Slots/Set Name", Capability.WorldWrite)]
public sealed class SetSlotName : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, string? name)
    {
        if (ScriptReads.Visible(ctx, slot))
            ScriptWrites.Write(ctx, slot!.Name, name ?? string.Empty);
    }
}
