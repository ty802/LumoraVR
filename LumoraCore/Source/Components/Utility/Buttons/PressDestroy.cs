// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

// With DestroyWholeObject set the request climbs to the nearest slot above the target carrying a
// DestroyRootMarker and destroys that instead, so a delete button buried inside a prop removes the
// whole prop rather than the panel it happens to sit on. -xlinka
[ComponentCategory("Utility/Buttons")]
public class PressDestroy : ButtonAction
{
    // Defaults to this slot when empty.
    public readonly SyncRef<Slot> Target;

    public readonly Sync<bool> DestroyWholeObject;

    public PressDestroy()
    {
        Target = new SyncRef<Slot>(this);
        DestroyWholeObject = new Sync<bool>(this, false);
    }

    protected override void Pressed(User? actor)
    {
        var target = Target.Target ?? Slot;
        if (target == null || target.IsDestroyed)
            return;

        if (DestroyWholeObject.Value)
            target = DestroyRootMarker.FindRoot(target);

        // Never take the world's root slot down with a button press.
        if (target == null || target.IsDestroyed || target.IsRootSlot)
            return;

        target.Destroy();
    }
}
