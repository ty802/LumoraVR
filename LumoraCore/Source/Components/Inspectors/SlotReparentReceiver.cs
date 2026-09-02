// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// drop target on a hierarchy tree row: releasing a held SLOT reference card over the row reparents
// that slot under the row's slot, keeping its world pose, undoably. cards for anything that isn't a
// slot fall through untouched so the row doesn't eat drops meant for reference fields.
[ComponentCategory("Utility/Inspectors")]
public class SlotReparentReceiver : Component, IProxyReceiver
{
    public readonly SyncRef<Slot> NewParent;

    public SlotReparentReceiver()
    {
        NewParent = new SyncRef<Slot>(this);
    }

    public bool TryReceiveProxy(IReadOnlyList<IGrabbable> held, Grabber grabber)
    {
        var parent = NewParent.Target;
        if (parent == null || parent.IsDestroyed)
            return false;

        for (int i = 0; i < held.Count; i++)
        {
            if (held[i] is not Component component || component.Slot == null)
                continue;
            var proxy = component.Slot.GetComponent<ReferenceProxy>();
            if (proxy?.Target.Target is not Slot target || target.IsDestroyed)
                continue;

            // One guard for cycles, the root, no-op drops and every system that has claimed the
            // slot - a socket holding it, a holster, a pinned assembly. Shared with the grab release
            // handlers so a tree drag and a drop reach the same answer.
            if (ReferenceEquals(target.Parent, parent))
                continue; // already there - nothing to do, but the drop is understood
            if (!ReparentGuard.CanReparent(target, parent))
                continue;

            var undo = SlotTransformUndoBatch.Begin(target, UndoLocale.Reparent(target.SlotName.Value));
            target.SetParent(parent, preserveGlobalTransform: true);
            InspectorUndo.Record(this, undo?.Commit());

            proxy.Consume(held[i], grabber);
            return true;
        }
        return false;
    }
}
