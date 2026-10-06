// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

// Takes another slot or component with it when it goes, for a part that has left its object's hierarchy but
// belongs to it all the same: a lid set down beside its bottle goes when the bottle does. Done by the
// authority, and not while the world itself is closing. -xlinka
[ComponentCategory("Utility/Spawning")]
public sealed class DestroyAlongside : Component
{
    public readonly SyncRef<IWorldElement> Target;

    public DestroyAlongside()
    {
        Target = new SyncRef<IWorldElement>(this);
    }

    public override void OnDestroy()
    {
        if (World is { IsAuthority: true, IsDestroyed: false, IsDisposed: false })
        {
            switch (Target.Target)
            {
                case Slot { IsDestroyed: false, IsRootSlot: false } slot:
                    slot.Destroy();
                    break;
                case Component { IsDestroyed: false } component when !ReferenceEquals(component, this):
                    component.Destroy();
                    break;
            }
        }
        base.OnDestroy();
    }
}
