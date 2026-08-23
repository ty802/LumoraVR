// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;

namespace Lumora.Godot.Hooks;

public abstract class ComponentHook<D> : ComponentHook<D, IHook> where D : ImplementableComponent<IHook>
{
}

// Automatically requests a Node3D from the Slot when initialized. Component hooks can attach
// Godot child nodes to attachedNode.
public abstract class ComponentHook<D, C> : Hook<D> where D : ImplementableComponent<C> where C : class, IHook
{
    protected SlotHook slotHook { get; private set; } = null!;

    // Godot equivalent of GameObject
    protected Node3D attachedNode { get; private set; } = null!;

    public override void Initialize()
    {
        slotHook = (SlotHook)Owner.Slot.Hook;
        attachedNode = slotHook.RequestNode3D();
    }

    public override void Destroy(bool destroyingWorld)
    {
        if (slotHook != null && !destroyingWorld)
        {
            slotHook.FreeNode3D();
        }
        slotHook = null!;
        attachedNode = null!;
    }
}

