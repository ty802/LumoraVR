// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Drives a float with the distance walked along a chain of slots.
//
// Runs late so the anchors have already been moved by whatever drives them this frame. A chain rather
// than a fixed pair: two anchors is the common case and falls out of the same sum, and measuring a
// route (hand to elbow to shoulder) otherwise needs a component per segment plus something to add
// them up. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(50)]
public class ElapsedDistance : Component
{
    // In order. Fewer than two measures zero.
    public readonly SyncRefList<Slot> Anchors;

    // Measure in this slot's local space, so the reading follows its scale. Global when empty.
    public readonly SyncRef<Slot> MeasureSpace;

    public readonly FieldDrive<float> Target;

    public ElapsedDistance()
    {
        Anchors = new SyncRefList<Slot>();
        MeasureSpace = new SyncRef<Slot>(this);
        Target = new FieldDrive<float>(this) { LocalValueOnly = true };
    }

    public float Distance
    {
        get
        {
            var space = MeasureSpace.Target;
            float total = 0f;
            bool hasPrevious = false;
            float3 previous = float3.Zero;

            foreach (var anchor in Anchors)
            {
                if (anchor == null || anchor.IsDestroyed)
                    continue;
                var point = anchor.GlobalPosition;
                if (space != null && !space.IsDestroyed)
                    point = space.GlobalPointToLocal(point);
                if (hasPrevious)
                    total += (point - previous).Length;
                previous = point;
                hasPrevious = true;
            }
            return total;
        }
    }

    public override void OnUpdate(float delta)
    {
        if (Target.IsLinkValid)
            Target.SetValue(Distance);
    }
}
