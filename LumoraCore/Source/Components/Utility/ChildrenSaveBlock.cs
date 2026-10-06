// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Components;

// Keeps whatever is put under this slot out of world saves for as long as it stays there: a box things are
// dropped into for now, a platform something rides on. A child's own setting comes back when it leaves, and
// every one of them when this goes or is switched off. Only children it turned off are turned back on, and
// only the authority writes them. -xlinka
[ComponentCategory("Utility")]
[SingleInstancePerSlot]
public sealed class ChildrenSaveBlock : Component
{
    private readonly HashSet<Slot> _held = new();
    private Slot? _hooked;

    public override void OnStart()
    {
        base.OnStart();
        Hook();
    }

    public override void OnEnabled()
    {
        base.OnEnabled();
        Hook();
    }

    public override void OnDisabled()
    {
        Unhook();
        base.OnDisabled();
    }

    public override void OnDestroy()
    {
        Unhook();
        base.OnDestroy();
    }

    private void Hook()
    {
        var slot = Slot;
        if (_hooked != null || slot == null || slot.IsDestroyed || !Enabled.Value || World?.IsAuthority != true)
            return;
        _hooked = slot;
        slot.OnChildAdded += Added;
        slot.OnChildRemoved += Removed;
        foreach (var child in slot.Children)
            Added(slot, child);
    }

    private void Unhook()
    {
        if (_hooked != null)
        {
            _hooked.OnChildAdded -= Added;
            _hooked.OnChildRemoved -= Removed;
            _hooked = null;
        }
        foreach (var child in _held)
            Restore(child);
        _held.Clear();
    }

    private void Added(Slot parent, Slot child)
    {
        if (child == null || child.IsDestroyed || !child.Persistent.Value || !_held.Add(child))
            return;
        child.Persistent.Value = false;
    }

    private void Removed(Slot parent, Slot child)
    {
        if (child != null && _held.Remove(child))
            Restore(child);
    }

    private static void Restore(Slot child)
    {
        if (!child.IsDestroyed && !child.Persistent.Value)
            child.Persistent.Value = true;
    }
}
