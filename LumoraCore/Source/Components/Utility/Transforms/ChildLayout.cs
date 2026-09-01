// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

// One laid-out child: the slot, and the drives that hold its transform.
//
// The drives live here rather than being made on the fly because a drive has to be a MEMBER to
// replicate and to survive a save - a layout rebuilt from loose runtime links would come back from a
// load with every child's position free again. -xlinka
public sealed class LayoutItem : SyncElement
{
    public override SyncMemberType MemberType => SyncMemberType.Object;

    public readonly SyncRef<Slot> Root = new();

    public readonly FieldDrive<float3> Position = new();

    public readonly FieldDrive<floatQ> Rotation = new();

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
        dictionary.Add("Root", Root.Save(control));
        dictionary.Add("Position", Position.Save(control));
        dictionary.Add("Rotation", Rotation.Save(control));
        return dictionary;
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        if (node is not DataTreeDictionary dictionary)
            return;
        if (dictionary.TryGetNode("Root") is { } rootNode)
            Root.Load(rootNode, control);
        if (dictionary.TryGetNode("Position") is { } positionNode)
            Position.Load(positionNode, control);
        if (dictionary.TryGetNode("Rotation") is { } rotationNode)
            Rotation.Load(rotationNode, control);
    }

    public override object? GetValueAsObject() => Root.Target;
}

public enum LayoutAxis
{
    XPositive,
    XNegative,
    YPositive,
    YNegative,
    ZPositive,
    ZNegative,
}

public enum LayoutAlign
{
    Negative,
    Center,
    Positive,
}

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
        Reconcile();
    }

    public override void OnDestroy()
    {
        var slot = Slot;
        if (slot != null)
        {
            slot.OnChildAdded -= SlotChildAdded;
            slot.OnChildRemoved -= SlotChildRemoved;
        }
        base.OnDestroy();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        _orderDirty = true;
    }

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

// Stacks children along one axis, either on a fixed pitch or packed against each other's bounds.
//
// Bounds are measured on a slow clock: the walk visits every renderer under a child, and a row of props
// that are not changing shape does not need re-measuring sixty times a second. -xlinka
[ComponentCategory("Utility/Transforms")]
public class AxisAligner : ChildLayoutBase
{
    private const double BoundsRefreshSeconds = 0.5;

    public readonly Sync<LayoutAxis> Axis;

    // Gap left between neighbours.
    public readonly Sync<float> Separation;

    // Pack against each child's measured size instead of the fixed pitch below.
    public readonly Sync<bool> UseBounds;

    // Pitch used while UseBounds is off.
    public readonly Sync<float> CellSize;

    // Where the finished stack sits relative to this slot, along the axis.
    public readonly Sync<LayoutAlign> Alignment;

    private readonly List<float> _sizes = new();
    private readonly List<float> _measured = new();
    private double _nextMeasure;

    public AxisAligner()
    {
        Axis = new Sync<LayoutAxis>(this, LayoutAxis.XPositive);
        Separation = new Sync<float>(this, 0f);
        UseBounds = new Sync<bool>(this, true);
        CellSize = new Sync<float>(this, 0.25f);
        Alignment = new Sync<LayoutAlign>(this, LayoutAlign.Center);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        _nextMeasure = 0d;
    }

    public override void OnUpdate(float delta)
    {
        var items = OrderedItems;
        int count = items.Count;
        if (count == 0)
            return;

        MeasureSizes(items);

        float separation = Separation.Value;
        float total = 0f;
        for (int i = 0; i < count; i++)
            total += _sizes[i];
        total += separation * (count - 1);

        float cursor = Alignment.Value switch
        {
            LayoutAlign.Center => -total * 0.5f,
            LayoutAlign.Negative => -total,
            _ => 0f,
        };

        var axis = Axis.Value;
        for (int i = 0; i < count; i++)
        {
            float size = _sizes[i];
            var item = items[i];
            if (item.Position.IsLinkValid)
                Push(item.Position, AxisVector(axis, cursor + size * 0.5f));
            cursor += size + separation;
        }
    }

    private void MeasureSizes(List<LayoutItem> items)
    {
        _sizes.Clear();
        float cell = CellSize.Value;

        if (!UseBounds.Value)
        {
            for (int i = 0; i < items.Count; i++)
                _sizes.Add(cell);
            return;
        }

        double now = UtilityClock.Seconds(World);
        if (_measured.Count == items.Count && now < _nextMeasure)
        {
            _sizes.AddRange(_measured);
            return;
        }
        _nextMeasure = now + BoundsRefreshSeconds;

        int index = AxisIndex(Axis.Value);
        for (int i = 0; i < items.Count; i++)
        {
            var root = items[i].Root.Target;
            float size = cell;
            // The box is on the child's own axes, so its own scale is what turns it into a size in our
            // space. A rotated child measures as if it were not; accounting for that would need the
            // rotated hull, and the answer would jitter as the child turned. -xlinka
            if (root != null && !root.IsDestroyed && SlotBoundsHelper.TryComputeLocalBounds(root, out var bounds))
                size = bounds.Size[index] * System.Math.Abs(root.LocalScale.Value[index]);
            _sizes.Add(size);
        }

        _measured.Clear();
        _measured.AddRange(_sizes);
    }
}

// Lays children out around a circle, optionally turning each one to follow it.
[ComponentCategory("Utility/Transforms")]
public class CircleAligner : ChildLayoutBase
{
    private const float Deg2Rad = System.MathF.PI / 180f;

    public readonly Sync<float3> Axis;

    public readonly Sync<float> Radius;

    // Degrees, where the first child sits.
    public readonly Sync<float> StartAngle;

    // Degrees covered by the whole ring.
    public readonly Sync<float> Arc;

    // Put the last child at the end of the arc rather than one step short of it. No effect on a full
    // circle, where the two would land on top of each other.
    public readonly Sync<bool> FillWholeArc;

    public readonly Sync<bool> DriveRotation;

    // Degrees added to each child's rotation.
    public readonly Sync<float> RotationOffset;

    public CircleAligner()
    {
        Axis = new Sync<float3>(this, float3.Up);
        Radius = new Sync<float>(this, 1f);
        StartAngle = new Sync<float>(this, 0f);
        Arc = new Sync<float>(this, 360f);
        FillWholeArc = new Sync<bool>(this, false);
        DriveRotation = new Sync<bool>(this, false);
        RotationOffset = new Sync<float>(this, 0f);
    }

    public override void OnUpdate(float delta)
    {
        var items = OrderedItems;
        int count = items.Count;
        if (count == 0)
            return;

        var axis = Axis.Value;
        if (axis.LengthSquared <= 0f)
            axis = float3.Up;
        axis = axis.Normalized;

        var spoke = Orthogonal(axis) * Radius.Value;

        int steps = FillWholeArc.Value && count > 1 ? count - 1 : count;
        float start = StartAngle.Value * Deg2Rad;
        float step = Arc.Value / steps * Deg2Rad;
        float rotationOffset = RotationOffset.Value * Deg2Rad;
        bool driveRotation = DriveRotation.Value;

        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            float angle = start + step * i;
            var turn = floatQ.AxisAngleRad(axis, angle);

            if (item.Position.IsLinkValid)
                Push(item.Position, turn * spoke);

            var root = item.Root.Target;
            if (root == null || root.IsDestroyed)
                continue;

            if (driveRotation)
            {
                if (!ReferenceEquals(item.Rotation.Target, root.LocalRotation))
                    item.Rotation.DriveTarget(root.LocalRotation);
                if (item.Rotation.IsLinkValid)
                    Push(item.Rotation, floatQ.AxisAngleRad(axis, angle + rotationOffset));
            }
            else if (item.Rotation.HasTarget)
            {
                item.Rotation.ReleaseLink();
            }
        }
    }

    // Any direction perpendicular to the axis will do for the start of the ring; picking the world axis
    // the ring axis is least aligned with keeps the cross product away from zero.
    private static float3 Orthogonal(float3 axis)
    {
        var reference = System.Math.Abs(axis.y) < 0.9f ? float3.Up : float3.Forward;
        var ortho = float3.Cross(reference, axis);
        return ortho.LengthSquared > 0f ? ortho.Normalized : float3.Right;
    }
}

// Lays children out on a grid of fixed cells.
[ComponentCategory("Utility/Transforms")]
public class ObjectGridAligner : ChildLayoutBase
{
    public readonly Sync<int> ItemsPerRow;

    public readonly Sync<float2> CellSize;

    public readonly Sync<LayoutAxis> RowAxis;

    public readonly Sync<LayoutAxis> ColumnAxis;

    public readonly Sync<LayoutAlign> RowAlignment;

    public readonly Sync<LayoutAlign> ColumnAlignment;

    public ObjectGridAligner()
    {
        ItemsPerRow = new Sync<int>(this, 4);
        CellSize = new Sync<float2>(this, new float2(0.25f, 0.25f));
        RowAxis = new Sync<LayoutAxis>(this, LayoutAxis.XPositive);
        ColumnAxis = new Sync<LayoutAxis>(this, LayoutAxis.ZPositive);
        RowAlignment = new Sync<LayoutAlign>(this, LayoutAlign.Center);
        ColumnAlignment = new Sync<LayoutAlign>(this, LayoutAlign.Positive);
    }

    public override void OnUpdate(float delta)
    {
        var items = OrderedItems;
        int count = items.Count;
        if (count == 0)
            return;

        int perRow = System.Math.Max(1, ItemsPerRow.Value);
        int columns = System.Math.Min(count, perRow);
        int rows = (count + perRow - 1) / perRow;
        var cell = CellSize.Value;

        float rowStart = AlignmentOffset(RowAlignment.Value, columns * cell.x, cell.x);
        float columnStart = AlignmentOffset(ColumnAlignment.Value, rows * cell.y, cell.y);

        var rowAxis = RowAxis.Value;
        var columnAxis = ColumnAxis.Value;

        for (int i = 0; i < count; i++)
        {
            var item = items[i];
            if (!item.Position.IsLinkValid)
                continue;
            int column = i % perRow;
            int row = i / perRow;
            var position = AxisVector(rowAxis, cell.x * column + rowStart)
                + AxisVector(columnAxis, cell.y * row + columnStart);
            Push(item.Position, position);
        }
    }

    private static float AlignmentOffset(LayoutAlign align, float total, float cell) => align switch
    {
        LayoutAlign.Positive => cell * 0.5f,
        LayoutAlign.Center => total * -0.5f + cell * 0.5f,
        _ => -total + cell * 0.5f,
    };
}
