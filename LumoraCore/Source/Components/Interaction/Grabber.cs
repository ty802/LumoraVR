// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Components.Interaction;

// owns a holder slot and a list of currently-grabbed IGrabbables. one per hand.
// candidate selection (sphere/raycast) is driven externally by InteractionLaser. - xlinka
// TODO - xlinka: release receivers, scaling reference, externally-held items
[ComponentCategory("Interaction")]
public class Grabber : Component
{
    private readonly List<IGrabbable> _grabbed = new();
    private Slot? _holderSlot;

    // lazy: create a child "Holder" slot the first time something is grabbed. - xlinka
    public Slot? HolderSlot
    {
        get
        {
            if (_holderSlot != null && !_holderSlot.IsRemoved) return _holderSlot;
            if (Slot == null || Slot.IsRemoved) return null;
            _holderSlot = Slot.AddSlot("Holder");
            return _holderSlot;
        }
    }

    public IReadOnlyList<IGrabbable> GrabbedObjects => _grabbed;

    public bool IsHoldingObjects => _grabbed.Count > 0;

    public bool TryGrab(IGrabbable target)
    {
        if (target == null || !target.CanGrab(this)) return false;

        var holder = HolderSlot;
        if (holder == null) return false;

        target.Grab(this, holder);
        if (!_grabbed.Contains(target)) _grabbed.Add(target);
        return true;
    }

    public void Release(IGrabbable target)
    {
        if (target == null) return;
        if (!_grabbed.Remove(target)) return;
        target.Release(this);
    }

    public void ReleaseAll()
    {
        for (int i = _grabbed.Count - 1; i >= 0; i--)
        {
            _grabbed[i].Release(this);
        }
        _grabbed.Clear();
    }

    public override void OnDestroy()
    {
        ReleaseAll();
        base.OnDestroy();
    }
}
