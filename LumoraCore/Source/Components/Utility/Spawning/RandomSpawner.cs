// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

public sealed class SpawnEntry : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly SyncRef<Slot> Template = new();

    // Zero or less never comes up.
    public readonly Sync<float> Weight = new();

    public override void Initialize(World world, IWorldElement? parent)
    {
        base.Initialize(world, parent);
        SyncMemberDiscovery.DiscoverAndInitializeSyncMembers(this, world, this);
    }

    protected override void InternalEncodeFull(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeFull(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalEncodeDelta(BinaryWriter writer, BinaryMessageBatch outboundMessage) { }
    protected override void InternalDecodeDelta(BinaryReader reader, BinaryMessageBatch inboundMessage) { }
    protected override void InternalClearDirty() { }

    public override DataTreeNode Save(SaveControl control)
    {
        var dictionary = new DataTreeDictionary();
        dictionary.Add("Template", Template.Save(control));
        dictionary.Add("Weight", Weight.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Template") is { } templateNode)
            Template.Load(templateNode, control);
        if (dictionary.TryGetNode("Weight") is { } weightNode)
            Weight.Load(weightNode, control);
    }

    public override object? GetValueAsObject() => Weight.Value;
}

// Duplicates one of several template slots at a point, on demand or on a repeating random interval.
//
// Spawning happens on the authority only. It creates slots, and running it on every peer would produce
// one copy per person in the session, each with its own RefIDs, none of which the others know about.
// The clone then replicates the normal way. -xlinka
//
// Templates are duplicated, never moved, and the clone is activated on the way out so a template can
// sit disabled next to the spawner without showing up in the world.
//
// MaxAlive caps the pile. A repeating spawner with no cap is a slow leak that ends as a floor of a
// thousand beads, so the spawner keeps its own clones in Spawned and retires the OLDEST one whenever a
// new spawn would push it over. Zero keeps the old unlimited behaviour. -xlinka
[ComponentCategory("Utility/Spawning")]
public class RandomSpawner : Component
{
    public readonly SyncList<SpawnEntry> Templates;

    // Defaults to this slot's parent.
    public readonly SyncRef<Slot> SpawnParent;

    // Scatters the spawn point around this slot. Spawns exactly on this slot when empty.
    public readonly SyncRef<Component> PointGenerator;

    public readonly Sync<bool> Repeat;

    // In seconds.
    public readonly Sync<float> MinInterval;

    // In seconds.
    public readonly Sync<float> MaxInterval;

    // How many clones this spawner is allowed to keep alive at once. Zero is unlimited.
    public readonly Sync<int> MaxAlive;

    // Every clone this spawner still owns, oldest first. Entries whose slot died some other way read
    // back null and get swept on the next spawn.
    public readonly SyncRefList<Slot> Spawned;

    private double _nextSpawn;
    private readonly Random _random = new();

    public RandomSpawner()
    {
        Templates = new SyncList<SpawnEntry>();
        SpawnParent = new SyncRef<Slot>(this);
        PointGenerator = new SyncRef<Component>(this);
        Repeat = new Sync<bool>(this, false);
        MinInterval = new Sync<float>(this, 1f);
        MaxInterval = new Sync<float>(this, 5f);
        MaxAlive = new Sync<int>(this, 0);
        Spawned = new SyncRefList<Slot>(this);
    }

    public SpawnEntry AddTemplate(Slot? template, float weight = 1f)
    {
        var entry = Templates.Add();
        entry.Template.Target = template!;
        entry.Weight.Value = weight;
        return entry;
    }

    [SyncMethod]
    public void Spawn()
    {
        SpawnAt(NextPoint());
    }

    public Slot? SpawnAt(float3 point)
    {
        if (World?.IsAuthority != true)
            return null;

        var template = PickTemplate();
        if (template == null || template.IsDestroyed)
            return null;

        var parent = SpawnParent.Target ?? Slot?.Parent ?? World.RootSlot;

        // Trim BEFORE the duplicate so the cap is a ceiling on what exists rather than on what existed
        // last frame, and so a cap of one never briefly holds two.
        EnforceCap();

        var clone = template.Duplicate(parent);
        if (clone == null)
            return null;

        clone.GlobalPosition = point;
        clone.ActiveSelf.Value = true;
        Spawned.Add(clone);
        return clone;
    }

    // The tracked slot IS the destroy root here, so this deliberately does not run the entry through
    // DestroyRootMarker.FindRoot. That helper answers "a delete button somewhere inside this prop, what
    // did the builder mean" by walking UP for the nearest marker, and a spawner walking up from its own
    // clone would escalate straight past it into whatever scene slot it was spawned under. The spawner
    // created the clone and already knows exactly what it owns. -xlinka
    private void EnforceCap()
    {
        // A clone can die by any route (a delete tool, its parent going down), and the reference reads
        // back null once it does. Sweep those first or the cap counts ghosts and stops spawning.
        Spawned.RemoveAll(reference => reference.Target == null);

        int cap = MaxAlive.Value;
        if (cap <= 0)
            return;

        while (Spawned.Count >= cap)
        {
            var oldest = Spawned[0];
            Spawned.RemoveAt(0);
            if (oldest != null && !oldest.IsDestroyed && !oldest.IsRootSlot)
                oldest.Destroy();
        }
    }

    public override void OnUpdate(float delta)
    {
        if (World?.IsAuthority != true || !Repeat.Value)
            return;

        double now = UtilityClock.Seconds(World);
        if (_nextSpawn <= 0d)
        {
            ScheduleNext(now);
            return;
        }
        if (now < _nextSpawn)
            return;

        ScheduleNext(now);
        Spawn();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        // Re-arm whenever the interval is edited, so a long gap typed by mistake does not hold the
        // spawner silent until it elapses.
        if (World?.IsAuthority == true && Repeat.Value)
            ScheduleNext(UtilityClock.Seconds(World));
    }

    private void ScheduleNext(double now)
    {
        float min = System.Math.Max(0f, MinInterval.Value);
        float max = System.Math.Max(min, MaxInterval.Value);
        _nextSpawn = now + min + _random.NextDouble() * (max - min);
    }

    private float3 NextPoint()
    {
        var slot = Slot;
        if (slot == null)
            return float3.Zero;
        if (PointGenerator.Target is IPointGenerator generator)
            return slot.LocalPointToGlobal(generator.GeneratePoint(slot));
        return slot.GlobalPosition;
    }

    // Weighted pick over one pass: walk the entries subtracting weights from a roll in [0, total).
    // Building a cumulative table first would allocate on every spawn for a list that is usually three
    // entries long.
    private Slot? PickTemplate()
    {
        float total = 0f;
        foreach (var entry in Templates.Elements)
        {
            if (entry.Template.Target != null && entry.Weight.Value > 0f)
                total += entry.Weight.Value;
        }
        if (total <= 0f)
            return null;

        float roll = (float)(_random.NextDouble() * total);
        foreach (var entry in Templates.Elements)
        {
            var template = entry.Template.Target;
            float weight = entry.Weight.Value;
            if (template == null || weight <= 0f)
                continue;
            roll -= weight;
            if (roll <= 0f)
                return template;
        }
        return null;
    }
}
