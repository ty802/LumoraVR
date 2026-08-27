// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// Shared machinery for the dispensers: a template slot, a place to put copies, a ceiling on how
// many may exist, and the one call that stamps a fresh one out.
//
// The live count is kept as a plain local list rather than a replicated one. Every copy carries a
// GrabSpawnMark that registers itself here as it starts, and every peer instantiates the same
// components from the same replicated hierarchy, so all of them arrive at the same number without a
// single byte crossing the wire. A synced list would be the same answer, paid for twice, and it
// would need conflict handling the moment two people pulled at once.
//
// The template is duplicated rather than loaded from a saved graph on purpose: duplication remaps
// every reference that pointed inside the template so drives, colour drivers and field targets
// follow the copy instead of the original. Stamp one out with a saved graph and half the wiring
// still points at the thing on the shelf. -xlinka
public abstract class GrabSpawnerBase : Component
{
    // May be an inactive child of the dispenser.
    public readonly SyncRef<Slot> Template;

    // Empty puts them at the world root.
    public readonly SyncRef<Slot> SpawnParent;

    // 0 for no limit.
    public readonly Sync<int> MaxInstances;

    // Turn the copy on, for templates kept inactive on the shelf.
    public readonly Sync<bool> ActivateInstance;

    // Turn the copy's grabbable on, for templates whose grab is disabled on the shelf.
    public readonly Sync<bool> EnableGrabbable;

    // Put the copy at the dispenser's own pose instead of the template's.
    public readonly Sync<bool> ResetTemplateTransform;

    public readonly Sync<bool> PersistInstances;

    // Let go of a copy this close to the dispenser and it goes away again.
    public readonly Sync<bool> DestroyOnReturn;

    // In world units.
    public readonly Sync<float> ReturnRadius;

    private readonly List<GrabSpawnMark> _live = new();

    protected GrabSpawnerBase()
    {
        Template = new SyncRef<Slot>(this);
        SpawnParent = new SyncRef<Slot>(this);
        MaxInstances = new Sync<int>(this, 0);
        ActivateInstance = new Sync<bool>(this, true);
        EnableGrabbable = new Sync<bool>(this, true);
        ResetTemplateTransform = new Sync<bool>(this, true);
        // Dispensed props are litter. A world that saves every copy anyone ever pulled reloads
        // buried in them, so a copy stays out of the save unless the author asks for it.
        PersistInstances = new Sync<bool>(this, false);
        DestroyOnReturn = new Sync<bool>(this, true);
        ReturnRadius = new Sync<float>(this, 0.25f);
    }

    public int LiveInstances
    {
        get { Prune(); return _live.Count; }
    }

    public bool CanSpawn => DescribeBlock() == null;

    // Null when it would work.
    public string? DescribeBlock()
    {
        if (IsDestroyed || !Enabled.Value)
            return "disabled";

        var world = World;
        if (world == null)
            return "no world";
        if (!world.AllowsItemSpawning)
            return "spawning is off in this world";

        var template = Template.Target;
        if (template == null || template.IsDestroyed)
            return "no template";

        // A dispenser that lives inside its own template copies itself along with everything else,
        // and the copy is a working dispenser pointed at a template that no longer exists.
        var slot = Slot;
        if (slot != null && (ReferenceEquals(slot, template) || slot.IsDescendantOf(template)))
            return "template contains the dispenser";

        if (template.GetComponentInChildren<Grabbable>() == null)
            return "template has no grabbable";

        int max = MaxInstances.Value;
        if (max > 0 && LiveInstances >= max)
            return $"limit reached ({max})";

        return null;
    }

    // Null when the dispenser refused. position/rotation apply only when overridePose is set; false
    // keeps the template's own world pose.
    protected Grabbable? Spawn(in float3 position, in floatQ rotation, bool overridePose)
    {
        string? blocked = DescribeBlock();
        if (blocked != null)
        {
            Logging.Logger.Warn($"GrabSpawner on {ParentHierarchyToString()} refused: {blocked}");
            return null;
        }

        var template = Template.Target;
        var world = World!;
        var parent = SpawnParent.Target;
        // Copying into the template's own subtree would put the copy inside the thing being copied.
        if (parent == null || parent.IsDestroyed || ReferenceEquals(parent, template)
            || parent.IsDescendantOf(template))
            parent = world.RootSlot;
        if (parent == null)
            return null;

        Slot? copy;
        try
        {
            copy = template.Duplicate(parent, preserveGlobalTransform: true);
        }
        catch (Exception ex)
        {
            // A denied write is the normal outcome in a world this user may not build in, so it is
            // a refusal to report, not a crash to propagate into the grip handler.
            Logging.Logger.Warn($"GrabSpawner on {ParentHierarchyToString()} could not duplicate: {ex.Message}");
            return null;
        }
        if (copy == null || copy.IsDestroyed)
            return null;

        if (ActivateInstance.Value)
            copy.ActiveSelf.Value = true;
        copy.Persistent.Value = PersistInstances.Value;

        if (overridePose || ResetTemplateTransform.Value)
        {
            copy.GlobalPosition = position;
            copy.GlobalRotation = rotation;
        }

        var grabbable = copy.GetComponentInChildren<Grabbable>();
        if (grabbable == null)
        {
            // The template passed the check a moment ago, so this is a template that changed under
            // us. An ungrabbable copy would just be dropped in the world at the hand's position.
            copy.Destroy();
            return null;
        }
        if (EnableGrabbable.Value)
            grabbable.Enabled.Value = true;

        var markSlot = grabbable.Slot;
        var mark = markSlot.GetComponent<GrabSpawnMark>() ?? markSlot.AttachComponent<GrabSpawnMark>();
        mark.Source.Target = this;
        mark.Instance.Target = copy;

        InspectorUndo.Record(this, SlotExistenceUndoBatch.Created(world, new[] { copy }, "Spawn"));
        return grabbable;
    }

    internal void RegisterInstance(GrabSpawnMark mark)
    {
        if (mark == null || _live.Contains(mark))
            return;
        _live.Add(mark);
    }

    internal void UnregisterInstance(GrabSpawnMark mark)
    {
        if (mark != null)
            _live.Remove(mark);
    }

    private void Prune()
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var mark = _live[i];
            if (mark == null || mark.IsDestroyed || mark.Slot == null || mark.Slot.IsDestroyed)
                _live.RemoveAt(i);
        }
    }
}
