// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Destroys a slot and what is under it, judged as the author destroying it by hand. Anything under it
// the author may not destroy moves to the world root instead. Keep assets spares the asset providers that
// something outside it still uses: each moves on its own slot under a holder named after it, which a world
// script makes under the world root and a Local script under its own object, while a guest's script leaves
// them under the world root. Keeping assets needs everything under the slot to be the author's to destroy,
// or nothing changes.
[Node("World/Slots/Destroy", Capability.WorldStructure)]
public sealed class DestroySlot : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, bool keepAssets = false)
    {
        if (!keepAssets)
            ScriptWrites.Destroy(ctx, slot);
        else if (ScriptReads.Visible(ctx, slot))
            ScriptSlotWrites.DestroyKeepingAssets(ctx, slot!);
    }
}
