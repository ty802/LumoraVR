// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.IO;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.Utility;

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
