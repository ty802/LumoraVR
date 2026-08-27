// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;

namespace Lumora.Core.Components;

// The one inspector row on a LodGroup that has to keep moving: which level the local
// viewer is currently inside.
//
// Everything else the group shows is derived from its fields and is correct the moment it is drawn.
// This one changes as the user walks, so it needs a clock - and a clock is exactly the thing worth
// keeping off a component that would otherwise never run an update. So the readout is a separate
// component that lives on the inspector's own text slot: it exists only while the row exists, it
// dies with the panel, and a group nobody is inspecting costs nothing. -xlinka
public static class LodGroupStats
{
    public static void AddLiveLevelRow(UIBuilder ui, LodGroup group)
    {
        var text = InspectorStats.AddRow(ui, "Local viewer", Describe(group));
        if (text?.Slot == null)
            return;

        var readout = text.Slot.AttachComponent<LodGroupLevelReadout>();
        readout.Persistent = false;
        readout.Group.Target = group;
        readout.Label.Target = text;
    }

    internal static string Describe(LodGroup group)
    {
        var head = group?.World?.LocalUser?.Root;
        if (group == null || head == null)
            return "no local viewer";

        int level = group.LevelAt(head.HeadPosition);
        float distance = (head.HeadPosition - group.Slot.GlobalPosition).Length;
        return level < 0
            ? $"culled at {distance:0.0} m"
            : $"level {level} at {distance:0.0} m";
    }
}

// four hertz because this is a number a person reads while walking backwards, and anything faster is
// motion nobody can follow; it is a distance and a band comparison, so the work itself is nothing -
// the rate is about the reading, not the cost
[HideInInspector]
public class LodGroupLevelReadout : Component
{
    private const double Interval = 0.25;

    public readonly SyncRef<LodGroup> Group;
    public readonly SyncRef<Text> Label;

    private double _next;

    public LodGroupLevelReadout()
    {
        Group = new SyncRef<LodGroup>(this);
        Label = new SyncRef<Text>(this);
    }

    public override void OnUpdate(float delta)
    {
        var group = Group.Target;
        var label = Label.Target;
        if (group == null || group.IsDestroyed || label == null || label.IsDestroyed)
        {
            // The panel closed or the group went away. Nothing left to write to, so stop existing
            // rather than idling on the update list.
            Slot?.RemoveComponent(this);
            return;
        }

        double now = World?.Time?.TotalTime ?? 0;
        if (now < _next)
            return;
        _next = now + Interval;

        var value = LodGroupStats.Describe(group);
        if (label.Content.Value != value)
            label.Content.Value = value;
    }
}
