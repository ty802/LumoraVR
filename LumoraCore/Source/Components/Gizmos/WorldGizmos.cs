// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Gizmos;

public enum GizmoMaterialKind
{
    AxisX,
    AxisY,
    AxisZ,
    Center,
    PlaneYZ,
    PlaneXZ,
    PlaneXY,
    FreeMove,
}

// Live selection gizmos of one world, indexed both ways: which gizmo is on a target, and which
// gizmos a user owns. Also owns the chrome every gizmo shares - the container slot they live under,
// the handle materials, and the park pool that lets a re-selection re-target an existing rig instead
// of building a new one.
//
// Keyed by World, not process-global. A gizmo table that outlives its world hands the next world
// entries pointing at dead slots, and with several worlds open at once (session plus the userspace
// overlay) a single flat table cannot answer "everything in THIS world" at all, which is exactly
// what deselect-all needs. Closing a world drops its entry with the world itself.
//
// Both indexes are dictionaries because both directions are hot: the inspector asks "does this slot
// have a gizmo" on every tree selection, and the deselect actions ask "what does this user own".
// -xlinka
public sealed class WorldGizmos
{
    private static readonly ConditionalWeakTable<World, WorldGizmos> Registries = new();

    private readonly Dictionary<Slot, SlotGizmo> _byTarget = new();
    private readonly Dictionary<User, List<SlotGizmo>> _byOwner = new();
    private readonly List<SlotGizmo> _pool = new();
    private readonly Dictionary<GizmoMaterialKind, OverlayUnlitMaterial> _materials = new();

    // Per-component gizmos get their own pair of indexes rather than sharing the slot ones: they are
    // keyed by component, several can sit on the same slot at once, and there is no pool because each
    // one is a different TYPE - a light gizmo cannot be re-pointed at a collider. -xlinka
    private readonly Dictionary<Component, ComponentGizmo> _byComponent = new();
    private readonly Dictionary<User, List<ComponentGizmo>> _byComponentOwner = new();

    private Slot? _root;
    private Slot? _chrome;

    // One parked rig per user is enough for the single-selection flow (click a row, click the next
    // row): the outgoing gizmo parks and the incoming one rents it back the same frame. Multi-select
    // still builds extras, and those just get destroyed. -xlinka
    private const int PoolLimit = 4;

    private WorldGizmos() { }

    // created on first use; null world yields null
    public static WorldGizmos? For(World? world)
        => world == null ? null : Registries.GetValue(world, static _ => new WorldGizmos());

    // CONTAINER

    // A shared, identity-transform container rather than a sibling of each target. Sibling parenting
    // made the handle rig inherit whatever scale the target's parent happened to carry, so handles on
    // anything inside a scaled hierarchy came out the wrong size, and it left the gizmo's lifetime
    // tangled with a branch the user is actively editing. -xlinka
    public Slot? Root(World? world)
    {
        if (_root != null && !_root.IsRemoved && !_root.IsDestroyed)
            return _root;

        var worldRoot = world?.RootSlot;
        if (worldRoot == null)
            return null;

        // Find before add: the host's container replicates to everyone, and a joining peer that made
        // its own would leave a second one sitting next to it for the rest of the session.
        _root = worldRoot.FindChild("Gizmos", recursive: false);
        if (_root == null)
        {
            _root = worldRoot.AddSlot("Gizmos");
            _root.Persistent.Value = false;
        }
        return _root;
    }

    // SHARED MATERIALS

    // every gizmo in the world points at the same eight providers instead of attaching its own set, so
    // a selection change stops allocating material components and the renderers batch
    public OverlayUnlitMaterial? GetMaterial(World? world, GizmoMaterialKind kind)
    {
        if (_materials.TryGetValue(kind, out var cached) && cached != null && !cached.IsDestroyed)
            return cached;

        var root = Root(world);
        if (root == null)
            return null;

        if (_chrome == null || _chrome.IsRemoved || _chrome.IsDestroyed)
        {
            _chrome = root.FindChild("Shared", recursive: false) ?? root.AddSlot("Shared");
            _chrome.Persistent.Value = false;
        }

        // One slot per tint so the lookup survives a peer that already received the host's set: an
        // unnamed pile of eight providers on one slot cannot be matched back to the tint it carries.
        string name = kind.ToString();
        var host = _chrome.FindChild(name, recursive: false) ?? _chrome.AddSlot(name);
        var material = host.GetComponent<OverlayUnlitMaterial>();
        if (material == null)
        {
            var tint = TintFor(kind);
            material = host.AttachComponent<OverlayUnlitMaterial>();
            material.FrontTintColor.Value = new colorHDR(tint.r, tint.g, tint.b, 1f);
            // Dimmed ghost when occluded, so handles stay findable behind the object they annotate.
            material.BehindTintColor.Value = new colorHDR(tint.r, tint.g, tint.b, 0.28f);
        }
        _materials[kind] = material;
        return material;
    }

    // Saturated axis RGB for the handles (the pastel inspector axis colors read washed-out in-world).
    private static color TintFor(GizmoMaterialKind kind) => kind switch
    {
        GizmoMaterialKind.AxisX => new color(0.9f, 0.1f, 0.1f, 1f),
        GizmoMaterialKind.AxisY => new color(0.1f, 0.8f, 0.1f, 1f),
        GizmoMaterialKind.AxisZ => new color(0.15f, 0.35f, 0.95f, 1f),
        GizmoMaterialKind.Center => new color(0.92f, 0.92f, 0.95f, 1f),
        GizmoMaterialKind.PlaneYZ => new color(0.5f, 1f, 1f, 0.73f),
        GizmoMaterialKind.PlaneXZ => new color(1f, 0.5f, 1f, 0.73f),
        GizmoMaterialKind.PlaneXY => new color(1f, 1f, 0.5f, 0.73f),
        _ => new color(0.75f, 0.75f, 0.8f, 0.73f),
    };

    // TARGET INDEX

    public void Track(Slot? target, SlotGizmo gizmo)
    {
        if (target == null || gizmo == null)
            return;
        _byTarget[target] = gizmo;
        IndexOwner(gizmo);
    }

    public void Untrack(Slot? target)
    {
        if (target == null)
            return;
        if (_byTarget.TryGetValue(target, out var gizmo))
            DropOwner(gizmo);
        _byTarget.Remove(target);
    }

    public SlotGizmo? Get(Slot? target)
    {
        if (target == null || !_byTarget.TryGetValue(target, out var gizmo))
            return null;
        if (IsLive(gizmo))
            return gizmo;
        Untrack(target);
        return null;
    }

    public bool Has(Slot? target) => Get(target) != null;

    // dead entries pruned on the way
    public SlotGizmo? Any()
    {
        List<Slot>? dead = null;
        SlotGizmo? found = null;
        foreach (var entry in _byTarget)
        {
            if (!IsLive(entry.Value))
            {
                (dead ??= new List<Slot>()).Add(entry.Key);
                continue;
            }
            found ??= entry.Value;
        }
        PruneTargets(dead);
        return found;
    }

    // safe to destroy while iterating
    public List<SlotGizmo> All()
    {
        var result = new List<SlotGizmo>(_byTarget.Count);
        List<Slot>? dead = null;
        foreach (var entry in _byTarget)
        {
            if (IsLive(entry.Value))
                result.Add(entry.Value);
            else
                (dead ??= new List<Slot>()).Add(entry.Key);
        }
        PruneTargets(dead);
        return result;
    }

    // OWNER INDEX

    public List<SlotGizmo> OwnedBy(User? owner)
    {
        var result = new List<SlotGizmo>();
        if (owner == null || !_byOwner.TryGetValue(owner, out var list))
            return result;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (IsLive(list[i]))
                result.Add(list[i]);
            else
                list.RemoveAt(i);
        }
        if (list.Count == 0)
            _byOwner.Remove(owner);
        return result;
    }

    public bool HasAnyOwnedBy(User? owner)
    {
        if (owner == null || !_byOwner.TryGetValue(owner, out var list))
            return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (IsLive(list[i]))
                return true;
        }
        return false;
    }

    // after a late-resolving owner ref on a joining peer
    public void ReindexOwner(SlotGizmo gizmo)
    {
        DropOwner(gizmo);
        IndexOwner(gizmo);
    }

    private void IndexOwner(SlotGizmo gizmo)
    {
        var owner = gizmo.Owner.Target;
        if (owner == null)
            return;
        if (!_byOwner.TryGetValue(owner, out var list))
            _byOwner[owner] = list = new List<SlotGizmo>();
        if (!list.Contains(gizmo))
            list.Add(gizmo);
    }

    private void DropOwner(SlotGizmo gizmo)
    {
        // Scan rather than looking the owner up: by the time a gizmo is dropped its owner ref can
        // already read null (the user is being torn down), and the entry would be orphaned.
        List<User>? empty = null;
        foreach (var entry in _byOwner)
        {
            if (entry.Value.Remove(gizmo) && entry.Value.Count == 0)
                (empty ??= new List<User>()).Add(entry.Key);
        }
        if (empty == null)
            return;
        foreach (var user in empty)
            _byOwner.Remove(user);
    }

    // PARK POOL

    // null when the pool is empty; building the handle rig is the expensive part of a selection
    // change, so the outgoing gizmo keeps its slots
    public SlotGizmo? Rent()
    {
        for (int i = _pool.Count - 1; i >= 0; i--)
        {
            var gizmo = _pool[i];
            _pool.RemoveAt(i);
            if (IsLive(gizmo))
                return gizmo;
        }
        return null;
    }

    // false when the pool is full; the caller should destroy it then
    public bool Park(SlotGizmo gizmo)
    {
        if (!IsLive(gizmo) || _pool.Count >= PoolLimit || _pool.Contains(gizmo))
            return false;
        _pool.Add(gizmo);
        return true;
    }

    // for when it's being destroyed outright
    public void Unpark(SlotGizmo gizmo) => _pool.Remove(gizmo);

    private void PruneTargets(List<Slot>? dead)
    {
        if (dead == null)
            return;
        foreach (var target in dead)
            Untrack(target);
    }

    private static bool IsLive(SlotGizmo? gizmo)
        => gizmo != null && !gizmo.IsDestroyed && gizmo.Slot is { IsDestroyed: false, IsRemoved: false };

    // COMPONENT GIZMO INDEX

    public void TrackComponent(Component? target, ComponentGizmo gizmo)
    {
        if (target == null || gizmo == null)
            return;
        _byComponent[target] = gizmo;
        IndexComponentOwner(gizmo);
    }

    public void UntrackComponent(Component? target)
    {
        if (target == null)
            return;
        if (_byComponent.TryGetValue(target, out var gizmo))
            DropComponentOwner(gizmo);
        _byComponent.Remove(target);
    }

    public ComponentGizmo? GetComponentGizmo(Component? target)
    {
        if (target == null || !_byComponent.TryGetValue(target, out var gizmo))
            return null;
        if (IsLive(gizmo))
            return gizmo;
        UntrackComponent(target);
        return null;
    }

    public bool HasComponentGizmo(Component? target) => GetComponentGizmo(target) != null;

    // safe to destroy while iterating
    public List<ComponentGizmo> AllComponentGizmos()
    {
        var result = new List<ComponentGizmo>(_byComponent.Count);
        List<Component>? dead = null;
        foreach (var entry in _byComponent)
        {
            if (IsLive(entry.Value))
                result.Add(entry.Value);
            else
                (dead ??= new List<Component>()).Add(entry.Key);
        }
        if (dead != null)
        {
            foreach (var target in dead)
                UntrackComponent(target);
        }
        return result;
    }

    public List<ComponentGizmo> ComponentGizmosOwnedBy(User? owner)
    {
        var result = new List<ComponentGizmo>();
        if (owner == null || !_byComponentOwner.TryGetValue(owner, out var list))
            return result;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (IsLive(list[i]))
                result.Add(list[i]);
            else
                list.RemoveAt(i);
        }
        if (list.Count == 0)
            _byComponentOwner.Remove(owner);
        return result;
    }

    public bool HasAnyComponentGizmoOwnedBy(User? owner)
    {
        if (owner == null || !_byComponentOwner.TryGetValue(owner, out var list))
            return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (IsLive(list[i]))
                return true;
        }
        return false;
    }

    // after a joining peer resolves the owner ref
    public void ReindexComponentOwner(ComponentGizmo gizmo)
    {
        DropComponentOwner(gizmo);
        IndexComponentOwner(gizmo);
    }

    private void IndexComponentOwner(ComponentGizmo gizmo)
    {
        var owner = gizmo.Owner.Target;
        if (owner == null)
            return;
        if (!_byComponentOwner.TryGetValue(owner, out var list))
            _byComponentOwner[owner] = list = new List<ComponentGizmo>();
        if (!list.Contains(gizmo))
            list.Add(gizmo);
    }

    private void DropComponentOwner(ComponentGizmo gizmo)
    {
        // Scan rather than looking the owner up: by the time a gizmo is dropped its owner ref can
        // already read null (the user is being torn down), and the entry would be orphaned.
        List<User>? empty = null;
        foreach (var entry in _byComponentOwner)
        {
            if (entry.Value.Remove(gizmo) && entry.Value.Count == 0)
                (empty ??= new List<User>()).Add(entry.Key);
        }
        if (empty == null)
            return;
        foreach (var user in empty)
            _byComponentOwner.Remove(user);
    }

    private static bool IsLive(ComponentGizmo? gizmo)
        => gizmo != null && !gizmo.IsDestroyed && gizmo.Slot is { IsDestroyed: false, IsRemoved: false };
}
