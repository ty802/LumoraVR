// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// Keeps the held-object menu's Destroy away from the object it sits on and everything under it: a game piece
// or a fixture people pick up and are meant to put back. Like GrabSaveBlock it stops the ordinary route, not
// a tool or a script that removes things on purpose; who may destroy what at all is the permission gate's
// call. -xlinka
[ComponentCategory("Interaction/Grabbables")]
[SingleInstancePerSlot]
public sealed class DestroyBlock : Component
{
    public static bool Blocks(Slot? slot)
    {
        for (var current = slot; current != null && !current.IsDestroyed; current = current.Parent)
        {
            if (current.GetComponent<DestroyBlock>() is { IsDestroyed: false } block && block.Enabled.Value)
                return true;
        }
        return false;
    }
}
