// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Who is holding the slot's grabbable, or nobody.
[Node("World/Interaction/Held By", Capability.UsersRead)]
public sealed class SlotHolder : ReadNode
{
    [return: Port("user")]
    public static User? Run(ScriptContext ctx, Slot? slot)
    {
        if (!ScriptReads.Visible(ctx, slot) || slot!.GetComponent<Grabbable>() is not { IsDestroyed: false } grabbable)
            return null;
        var user = grabbable.Grabber?.OwningUser;
        return ScriptReads.Visible(ctx, user) ? user : null;
    }
}
