// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Logging;

namespace Lumora.Core.Components.Gizmos;

public static class GizmoHelper
{
    public static SlotGizmo SpawnGizmoFor(Slot targetSlot)
    {
        if (targetSlot == null)
        {
            Logger.Warn("GizmoHelper.SpawnGizmoFor: Target slot is null");
            return null!;
        }

        if (targetSlot.IsRootSlot)
        {
            Logger.Warn("GizmoHelper.SpawnGizmoFor: Cannot create gizmo for root slot");
            return null!;
        }

        var world = targetSlot.World;
        var registry = WorldGizmos.For(world);

        var existing = registry?.Get(targetSlot);
        if (existing != null)
        {
            existing.Active.Value = true;
            return existing;
        }

        // A rig that was dismissed a moment ago is parked, not gone. Re-pointing it skips the whole
        // build - roughly thirty slots, seventeen meshes and their renderers - which is the entire
        // cost of a selection change. -xlinka
        var pooled = registry?.Rent();
        if (pooled != null)
        {
            pooled.Retarget(targetSlot, world?.LocalUser);
            return pooled;
        }

        var parent = registry?.Root(world) ?? world?.RootSlot;
        var gizmoSlot = parent?.AddSlot($"Gizmo_{targetSlot.Name.Value}");
        if (gizmoSlot == null)
        {
            Logger.Warn("GizmoHelper.SpawnGizmoFor: Failed to create gizmo slot");
            return null!;
        }

        // Editor chrome, not content: a selection gizmo must never serialize into world saves.
        gizmoSlot.Persistent.Value = false;

        var gizmo = gizmoSlot.AttachComponent<SlotGizmo>();
        gizmo.Setup(targetSlot, world?.LocalUser);
        return gizmo;
    }

    public static void DestroyGizmo(Slot targetSlot)
    {
        if (targetSlot == null) return;

        var gizmo = WorldGizmos.For(targetSlot.World)?.Get(targetSlot);
        if (gizmo != null)
            Dismiss(gizmo, targetSlot.World);
    }

    public static bool HasGizmo(Slot targetSlot)
        => targetSlot != null && WorldGizmos.For(targetSlot.World)?.Has(targetSlot) == true;

    public static SlotGizmo GetGizmo(Slot targetSlot)
    {
        if (targetSlot == null)
            return null!;
        return WorldGizmos.For(targetSlot.World)?.Get(targetSlot) ?? null!;
    }

    public static SlotGizmo ToggleGizmo(Slot targetSlot)
    {
        if (HasGizmo(targetSlot))
        {
            DestroyGizmo(targetSlot);
            return null!;
        }
        return SpawnGizmoFor(targetSlot);
    }

    // for callers that just need "is something selected"
    public static SlotGizmo? AnyGizmo(World? world) => WorldGizmos.For(world)?.Any();

    public static bool HasLocalGizmos(World? world)
    {
        var registry = WorldGizmos.For(world);
        if (registry == null)
            return false;
        var localUser = world?.LocalUser;
        return registry.HasAnyOwnedBy(localUser) || registry.HasAnyComponentGizmoOwnedBy(localUser);
    }

    // Another user's gizmo is theirs to dismiss; only the authority may pull one down for them, and a
    // peer that tries just has the mutation denied further down. So this removes what it is entitled
    // to remove and reports the number, rather than pretending it cleared the world. -xlinka
    public static int DeselectAll(World? world)
    {
        var registry = WorldGizmos.For(world);
        if (registry == null)
            return 0;

        var localUser = world?.LocalUser;
        bool authority = world?.IsAuthority == true;
        int removed = 0;
        foreach (var gizmo in registry.All())
        {
            if (!authority && !ReferenceEquals(gizmo.Owner?.Target, localUser))
                continue;
            Dismiss(gizmo, world);
            removed++;
        }
        foreach (var gizmo in registry.AllComponentGizmos())
        {
            if (!authority && !ReferenceEquals(gizmo.Owner?.Target, localUser))
                continue;
            gizmo.DestroySelf();
            removed++;
        }
        return removed;
    }

    public static int DeselectLocal(World? world)
    {
        var registry = WorldGizmos.For(world);
        var localUser = world?.LocalUser;
        if (registry == null || localUser == null)
            return 0;

        int removed = 0;
        foreach (var gizmo in registry.OwnedBy(localUser))
        {
            Dismiss(gizmo, world);
            removed++;
        }
        foreach (var gizmo in registry.ComponentGizmosOwnedBy(localUser))
        {
            gizmo.DestroySelf();
            removed++;
        }
        return removed;
    }

    // PER-COMPONENT GIZMOS
    //
    // Explicit only. Selecting a slot puts up the slot gizmo and nothing else; a component gizmo
    // appears when someone asks for it from that component's inspector header, because a slot carrying
    // a light, a collider and a renderer would otherwise stack three wireframes on one object the
    // moment it was clicked. -xlinka

    public static bool CanGizmo(Component? component)
        => component != null && !component.IsDestroyed
        && GizmoRegistry.HasComponentGizmo(component.GetType());

    public static ComponentGizmo? SpawnComponentGizmo(Component? component)
    {
        if (component == null || component.IsDestroyed || component.Slot == null)
            return null;

        var gizmoType = GizmoRegistry.GetComponentGizmoType(component.GetType());
        if (gizmoType == null)
            return null;

        var world = component.World;
        var registry = WorldGizmos.For(world);

        var existing = registry?.GetComponentGizmo(component);
        if (existing != null)
        {
            existing.Active.Value = true;
            return existing;
        }

        var parent = registry?.Root(world) ?? world?.RootSlot;
        var gizmoSlot = parent?.AddSlot($"Gizmo_{component.GetType().Name}");
        if (gizmoSlot == null)
        {
            Logger.Warn("GizmoHelper.SpawnComponentGizmo: Failed to create gizmo slot");
            return null;
        }

        // Editor chrome, not content: a gizmo must never serialize into world saves.
        gizmoSlot.Persistent.Value = false;

        if (gizmoSlot.AttachComponent(gizmoType) is not ComponentGizmo gizmo)
        {
            gizmoSlot.Destroy();
            Logger.Warn($"GizmoHelper.SpawnComponentGizmo: {gizmoType.Name} is not a ComponentGizmo");
            return null;
        }

        gizmo.Setup(component, world?.LocalUser);
        return gizmo;
    }

    public static void DestroyComponentGizmo(Component? component)
    {
        if (component == null)
            return;
        WorldGizmos.For(component.World)?.GetComponentGizmo(component)?.DestroySelf();
    }

    public static bool HasComponentGizmo(Component? component)
        => component != null && WorldGizmos.For(component.World)?.HasComponentGizmo(component) == true;

    public static ComponentGizmo? GetComponentGizmo(Component? component)
        => component == null ? null : WorldGizmos.For(component.World)?.GetComponentGizmo(component);

    public static ComponentGizmo? ToggleComponentGizmo(Component? component)
    {
        if (HasComponentGizmo(component))
        {
            DestroyComponentGizmo(component);
            return null;
        }
        return SpawnComponentGizmo(component);
    }

    // Park what we own so the next selection can rent it back; destroy anything else outright.
    private static void Dismiss(SlotGizmo gizmo, World? world)
    {
        if (gizmo == null || gizmo.IsDestroyed)
            return;

        var registry = WorldGizmos.For(world);
        if (ReferenceEquals(gizmo.Owner?.Target, world?.LocalUser))
        {
            gizmo.Park();
            if (registry != null && registry.Park(gizmo))
                return;
        }
        gizmo.DestroySelf();
    }
}
