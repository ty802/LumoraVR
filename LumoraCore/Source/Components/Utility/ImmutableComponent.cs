// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Helio.UI;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components;

// Marks its slot and everything under it as not-yours-to-edit. The subtree disappears from the scene
// inspector, refuses to open in one, refuses to be reparented into or out of, cannot be picked by the
// dev tool or the glue/duplicator tools, and shows read-only if a member of it is ever displayed.
//
// THIS IS A TOOLING PROTECTION, NOT A DATAMODEL ONE, and the distinction is the whole design. It is
// hung on machinery that a component composes every frame - a nameplate rewrites its own panel size,
// its rim colour and its badge row constantly - so the moment this gated WRITES, the thing it was
// protecting would stop working. Every check below sits on a path that starts with a PERSON: a tree
// row being built, a card being dropped, a tool trigger, an edit box. Code writing through the
// datamodel is not asked and must never be. If you want to stop a write, that is the permission gate
// (Lumora.Warden), not this. -xlinka
//
// Nearest marker does not win here - ANY enabled marker at or above the slot protects it - so marking
// a container covers everything inside it and a child cannot mark itself back out. Note the one-way
// door that follows: a slot you mark leaves the tree immediately and the inspector will not reopen it,
// so taking the mark off again is a job for code or an undo, not for the panel.
[ComponentCategory("Utility")]
[SingleInstancePerSlot]
public class ImmutableComponent : Component, IReparentBlock, ICustomInspectorUI
{
    // Every marked slot in the process. A registry rather than a GetComponentInParents walk because
    // the tree build asks this once per row: the walk allocates a component list per slot, this is a
    // dictionary probe per ancestor and, in the overwhelmingly common case of a world with no markers
    // at all, one volatile read and nothing else. Nothing here runs per frame - every caller is a
    // person doing something - so the lock costs nothing worth measuring. -xlinka
    private static readonly object Gate = new();
    private static readonly Dictionary<Slot, ImmutableComponent> Marked = new();
    private static volatile int _markedCount;

    public static int MarkedSlotCount => _markedCount;

    public override void OnAwake()
    {
        base.OnAwake();
        Register();
    }

    public override void OnStart()
    {
        base.OnStart();
        // A component attached before its slot was parented (the usual compose order) registers under
        // the slot it ended up on, not the one it was born on.
        Register();
    }

    public override void OnDestroy()
    {
        var slot = Slot;
        if (slot != null)
        {
            lock (Gate)
            {
                if (Marked.TryGetValue(slot, out var current) && ReferenceEquals(current, this))
                {
                    Marked.Remove(slot);
                    _markedCount = Marked.Count;
                }
            }
        }
        base.OnDestroy();
    }

    private void Register()
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed || IsDestroyed)
            return;
        lock (Gate)
        {
            Marked[slot] = this;
            _markedCount = Marked.Count;
        }
    }

    // The one question everything else asks. True when this slot is a marked one or hangs under one.
    public static bool IsProtected(Slot? slot)
    {
        if (slot == null || _markedCount == 0)
            return false;

        lock (Gate)
        {
            for (var current = slot; current != null && !current.IsDestroyed; current = current.Parent)
            {
                if (!Marked.TryGetValue(current, out var marker))
                    continue;
                if (marker != null && !marker.IsDestroyed && marker.Enabled.Value)
                    return true;
            }
        }
        return false;
    }

    // Same question asked of a member, a component or anything else that hangs off a slot. Climbs to
    // the owning slot the way the permission gate does, with the same depth stop.
    public static bool IsProtected(IWorldElement? element)
    {
        if (element == null || _markedCount == 0)
            return false;

        // Slot is qualified because inside a Component the bare name binds to the instance property,
        // not the type.
        IWorldElement? node = element;
        for (int depth = 0; node != null && node is not Lumora.Core.Slot && depth < 8; depth++)
            node = (node as Component)?.Slot ?? (node as SyncElement)?.Parent ?? (node as Worker)?.Parent;

        return IsProtected(node as Lumora.Core.Slot);
    }

    // Both directions, and nothing to decide: the guard only reaches this marker by climbing from the
    // moving slot or from where it would land, so being asked at all means one end of the move is
    // inside the protected subtree.
    public bool AllowsReparent(Slot target, Slot newParent) => false;

    public void BuildInspectorBody(UIBuilder ui)
    {
        InspectorStats.AddRow(ui, "Subtree", "protected from editing");
        InspectorStats.AddRow(ui, "Datamodel writes", "not gated");
        InspectorStats.AddRow(ui, "Marked slots in process", _markedCount.ToString());
    }
}
