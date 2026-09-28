// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Whether anyone is holding the slot's grabbable.
[Node("World/Interaction/Is Grabbed", Capability.WorldRead)]
public sealed class SlotGrabbed : ReadNode
{
    [return: Port("grabbed")]
    public static bool Run(ScriptContext ctx, Slot? slot)
        => ScriptReads.Visible(ctx, slot) && slot!.GetComponent<Grabbable>() is { IsDestroyed: false, IsGrabbed: true };
}
