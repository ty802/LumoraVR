// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// Keeps the held-object menu's Duplicate away from the object it sits on and everything under it, for the one
// of a kind: a key, a cartridge a machine plays, a piece a game counts. The ordinary route only, as with
// DestroyBlock. -xlinka
[ComponentCategory("Interaction/Grabbables")]
[SingleInstancePerSlot]
public sealed class DuplicateBlock : Component
{
    public static bool Blocks(Slot? slot)
    {
        for (var current = slot; current != null && !current.IsDestroyed; current = current.Parent)
        {
            if (current.GetComponent<DuplicateBlock>() is { IsDestroyed: false } block && block.Enabled.Value)
                return true;
        }
        return false;
    }
}
