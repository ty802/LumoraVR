// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;
using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// The box Compute Bounds measures: each mesh renderer's mesh bounds under a slot carried through its slot
// into the space asked for, a skinned mesh in its bind pose, and where nothing draws, the colliders instead.
// Inactive branches below the slot and switched-off renderers and colliders count only when asked, and a
// tag keeps only what sits on slots carrying it. A mesh still loading counts as nothing yet. -xlinka
internal static class ScriptSlotBounds
{
    public static bool TryCompute(Slot slot, Slot? space, bool includeInactive, string? tag, out BoundingBox bounds)
    {
        bounds = BoundingBox.Empty;
        var toSpace = ScriptSpaces.FromWorld(space);
        if (Walk(slot, toSpace, includeInactive, tag, colliders: false, ref bounds, isRoot: true))
            return true;
        return Walk(slot, toSpace, includeInactive, tag, colliders: true, ref bounds, isRoot: true);
    }

    private static bool Walk(Slot slot, in float4x4 toSpace, bool includeInactive, string? tag, bool colliders,
        ref BoundingBox bounds, bool isRoot)
    {
        if (slot.IsDestroyed || (!isRoot && !includeInactive && !slot.ActiveSelf.Value))
            return false;
        bool found = false;
        if (string.IsNullOrEmpty(tag) || slot.Tag.Value == tag)
        {
            float4x4? into = null;
            foreach (var component in slot.Components)
            {
                if (component.IsDestroyed || (!includeInactive && !component.Enabled.Value))
                    continue;
                BoundingBox local;
                if (colliders)
                {
                    if (component is not Collider collider)
                        continue;
                    local = collider.GetLocalBounds();
                }
                else if (!SlotBoundsHelper.TryGetRendererLocalBounds(component, out local))
                {
                    continue;
                }
                if (!(local.Min.x <= local.Max.x && local.Min.y <= local.Max.y && local.Min.z <= local.Max.z))
                    continue;
                into ??= toSpace * slot.LocalToWorld;
                bounds.Encapsulate(local.Transformed(into.Value));
                found = true;
            }
        }
        foreach (var child in slot.Children)
            found |= Walk(child, toSpace, includeInactive, tag, colliders, ref bounds, isRoot: false);
        return found;
    }
}
