// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

// Shared plumbing for the components that arrange their own children.
//
// The item list is structure, so only the authority edits it: the child-added event fires on every peer
// that receives the new slot, and a list every peer appends to would end up N entries long in an N-peer
// session. Everyone else gets the entries by replication and drives the same layout from them.
//
// Membership changes are deferred through the world's synchronous queue because they arrive mid-way
// through a hierarchy change - adding a list element there would mutate the data model underneath the
// walk that is still running. -xlinka
public abstract class ChildLayoutBase : Component
{
    public readonly Sync<bool> AutoAddChildren;

    // Children carrying one of these tags are left where they are.
    public readonly SyncFieldList<string> IgnoreTags;

    public readonly SyncList<LayoutItem> Items;

    private readonly List<LayoutItem> _ordered = new();
    private bool _orderDirty = true;

    protected ChildLayoutBase()
    {
        AutoAddChildren = new Sync<bool>(this, true);
        IgnoreTags = new SyncFieldList<string>();
        Items = new SyncList<LayoutItem>();
    }

    public int ItemCount => Items.Count;

    public Slot? GetItem(int index) => Items[index].Root.Target;

    // Hierarchy order, not list order: a person who drags a child up the tree expects it to move up the
    // row, and the list only ever grows in the order things were added.
    protected List<LayoutItem> OrderedItems
    {
        get
        {
            if (_orderDirty)
                RebuildOrder();
            return _ordered;
        }
    }

    public override void OnStart()
    {
        base.OnStart();
        var slot = Slot;
        if (slot == null)
            return;
        slot.OnChildAdded += SlotChildAdded;
        slot.OnChildRemoved += SlotChildRemoved;
        slot.ChildOrderChanged += SlotChildOrderChanged;
        Reconcile();
    }

    public override void OnDestroy()
    {
        var slot = Slot;
        if (slot != null)
        {
            slot.OnChildAdded -= SlotChildAdded;
            slot.OnChildRemoved -= SlotChildRemoved;
            slot.ChildOrderChanged -= SlotChildOrderChanged;
        }
        base.OnDestroy();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        _orderDirty = true;
    }

    // The slot's children changed places, here or on any machine: the order is read again.
    private void SlotChildOrderChanged(Slot parent) => _orderDirty = true;

    private void SlotChildAdded(Slot parent, Slot child)
    {
        _orderDirty = true;
        if (!AutoAddChildren.Value || World?.IsAuthority != true)
            return;
        World.RunSynchronously(() => AddChild(child));
    }

    private void SlotChildRemoved(Slot parent, Slot child)
    {
        _orderDirty = true;
        if (World?.IsAuthority != true)
            return;
        World.RunSynchronously(() => RemoveChild(child));
    }

    [SyncMethod]
    public void Reconcile()
    {
        _orderDirty = true;
        var slot = Slot;
        if (slot == null || slot.IsDestroyed || World?.IsAuthority != true)
            return;

        Items.RemoveAll(item =>
        {
            var root = item.Root.Target;
            return root == null || root.IsDestroyed || root.Parent != slot;
        });

        if (!AutoAddChildren.Value)
            return;

        foreach (var child in slot.Children)
            AddChild(child);
    }

    public void AddChild(Slot? child)
    {
        var slot = Slot;
        if (child == null || child.IsDestroyed || slot == null || child.Parent != slot)
            return;
        if (World?.IsAuthority != true || IsIgnored(child) || IndexOf(child) >= 0)
            return;

        var item = Items.Add();
        item.Root.Target = child;
        item.Position.DriveTarget(child.LocalPosition);
        _orderDirty = true;
    }

    public void RemoveChild(Slot? child)
    {
        if (World?.IsAuthority != true)
            return;
        Items.RemoveAll(item => item.Root.Target == child || item.Root.Target == null);
        _orderDirty = true;
    }

    protected int IndexOf(Slot child)
    {
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Root.Target == child)
                return i;
        }
        return -1;
    }

    private bool IsIgnored(Slot child)
    {
        if (IgnoreTags.Count == 0)
            return false;
        string tag = child.Tag.Value;
        if (string.IsNullOrEmpty(tag))
            return false;
        for (int i = 0; i < IgnoreTags.Count; i++)
        {
            if (IgnoreTags[i] == tag)
                return true;
        }
        return false;
    }

    private void RebuildOrder()
    {
        _orderDirty = false;
        _ordered.Clear();
        var slot = Slot;
        if (slot == null || slot.IsDestroyed || Items.Count == 0)
            return;

        foreach (var child in slot.Children)
        {
            int index = IndexOf(child);
            if (index >= 0)
                _ordered.Add(Items[index]);
        }
    }

    // Every peer computes the same arrangement from the same replicated inputs, so the results stay off
    // the wire.
    protected static void Push(FieldDrive<float3> drive, in float3 value)
    {
        drive.LocalValueOnly = true;
        drive.SetValue(value);
    }

    protected static void Push(FieldDrive<floatQ> drive, in floatQ value)
    {
        drive.LocalValueOnly = true;
        drive.SetValue(value);
    }

    protected static float3 AxisVector(LayoutAxis axis, float distance) => axis switch
    {
        LayoutAxis.XPositive => new float3(distance, 0f, 0f),
        LayoutAxis.XNegative => new float3(-distance, 0f, 0f),
        LayoutAxis.YPositive => new float3(0f, distance, 0f),
        LayoutAxis.YNegative => new float3(0f, -distance, 0f),
        LayoutAxis.ZPositive => new float3(0f, 0f, distance),
        _ => new float3(0f, 0f, -distance),
    };

    protected static int AxisIndex(LayoutAxis axis) => (int)axis / 2;
}
