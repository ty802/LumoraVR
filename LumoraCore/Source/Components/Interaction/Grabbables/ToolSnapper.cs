// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core.Components.Magnets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.Interaction;

// A holster for one tool. Let a tool go near it and the tool drops into it; take it back out and,
// with EquipOnGrab set, it goes straight into that hand as the equipped tool instead of being
// carried around.
//
// Put it on a belt loop, a wall hook or a workbench slot. It holds one tool at a time and only the
// kinds named in AcceptTags. For a holster that also lines the tool up nicely and pulls it in from a
// distance, put a MagnetSocket on the same slot and let that do the snapping - this only cares about
// which tools belong here and what happens when one is taken.
//
// The holster needs a collider, on its own slot or a child. A release looks for drop targets by
// overlapping a sphere around the hand, so a holster with nothing physical about it is never found.
//
// The equip is deferred by one beat rather than run out of the grab event. That event fires from
// inside the grab itself, BEFORE the hand has finished recording what it picked up, so equipping
// there means letting go of something the hand is about to add to its hold list - and it ends up
// holding a tool that is simultaneously docked in its tool holder. One beat later the grab has
// settled and the equip can take it off the hand cleanly. -xlinka
[ComponentCategory("Interaction/Grabbables")]
public class ToolSnapper : Component, IGrabbableReceiver, IReparentBlock, ICustomInspectorUI
{
    // Empty uses this slot.
    public readonly SyncRef<Slot> SnapPoint;

    // In this slot's own scale.
    public readonly Sync<float> SnapRadius;

    // Empty takes any tool.
    public readonly SyncFieldList<string> AcceptTags;

    // Taking the tool out equips it into that hand rather than putting it in the hand.
    public readonly Sync<bool> EquipOnGrab;

    // Sit the tool exactly on the snap point instead of leaving it where it landed.
    public readonly Sync<bool> SnapPose;

    // Read CurrentTool for the live answer.
    public readonly SyncRef<ToolItem> Holstered;

    private Grabbable? _watched;
    private ToolItem? _watchedTool;

    public ToolSnapper()
    {
        SnapPoint = new SyncRef<Slot>(this);
        SnapRadius = new Sync<float>(this, 0.2f);
        AcceptTags = new SyncFieldList<string>(this);
        EquipOnGrab = new Sync<bool>(this, true);
        SnapPose = new Sync<bool>(this, true);
        Holstered = new SyncRef<ToolItem>(this);
    }

    public Slot? Rest
    {
        get
        {
            var point = SnapPoint.Target;
            return point != null && !point.IsDestroyed ? point : Slot;
        }
    }

    // SnapRadius in world units.
    public float WorldRadius => Rest?.LocalScaleToGlobal(System.Math.Max(0f, SnapRadius.Value)) ?? 0f;

    // The stored reference only counts while the tool is still parked here - anything can pull it
    // out, and nothing tells us when it does.
    public ToolItem? CurrentTool
    {
        get
        {
            var stored = Holstered.Target;
            if (stored != null && !stored.IsDestroyed && IsParkedHere(stored))
                return stored;
            return FindParkedTool();
        }
    }

    public bool Occupied => CurrentTool != null;

    public override void OnUpdate(float delta)
    {
        // Re-checked every frame rather than hooked once, because a tool can leave the holster
        // without anything raising an event we can hear: destroyed, reparented by a tool, or pulled
        // out by another peer whose grab arrives as a plain hierarchy change.
        var tool = CurrentTool;
        var grabbable = tool?.Slot?.GetComponent<Grabbable>();
        if (ReferenceEquals(grabbable, _watched))
            return;

        if (_watched != null)
            _watched.OnLocalGrabbed -= OnToolTaken;
        _watched = grabbable;
        _watchedTool = tool;
        if (_watched != null)
            _watched.OnLocalGrabbed += OnToolTaken;
    }

    public override void OnDestroy()
    {
        if (_watched != null)
            _watched.OnLocalGrabbed -= OnToolTaken;
        _watched = null;
        _watchedTool = null;
        base.OnDestroy();
    }

    // RECEIVING
    public float? GetReceiveDistance(IGrabbable grabbable, Grabber grabber)
    {
        if (IsDestroyed || !Enabled.Value)
            return null;
        var rest = Rest;
        if (rest == null || rest.IsDestroyed)
            return null;

        var tool = ResolveTool(grabbable);
        if (tool == null || !Accepts(tool))
            return null;

        // Occupied by something else. One tool per holster, and the one already in it keeps the spot.
        var current = CurrentTool;
        if (current != null && !ReferenceEquals(current, tool))
            return null;

        var toolSlot = tool.Slot;
        if (toolSlot == null || toolSlot.IsDestroyed)
            return null;

        float radius = WorldRadius;
        float distance = float3.Distance(toolSlot.GlobalPosition, rest.GlobalPosition);
        return distance <= radius ? distance : null;
    }

    public void Receive(IGrabbable grabbable, Grabber grabber)
    {
        var tool = ResolveTool(grabbable);
        var rest = Rest;
        var toolSlot = tool?.Slot;
        if (tool == null || rest == null || rest.IsDestroyed || toolSlot == null || toolSlot.IsDestroyed)
            return;
        if (ReferenceEquals(toolSlot, rest) || rest.IsDescendantOf(toolSlot))
            return;

        // This runs inside the hand's release, which drops everything it was holding in one pass and
        // offers each item to a drop target in turn. A throw here would take the rest of that release
        // with it, and a holster in content this user cannot write is a perfectly ordinary reason to
        // be refused, so the refusal ends here rather than travelling. Occupancy is resolved from the
        // children as well as the stored reference, so a failed write leaves the holster correct
        // about what is in it either way.
        try
        {
            // The holster owns the tool's parentage from here, so this reparent does not run through
            // the guard - it IS the system the guard protects the tool for.
            if (!ReferenceEquals(toolSlot.Parent, rest))
                toolSlot.SetParent(rest, preserveGlobalTransform: !SnapPose.Value);

            if (SnapPose.Value)
            {
                toolSlot.LocalPosition.Value = float3.Zero;
                toolSlot.LocalRotation.Value = floatQ.Identity;
            }

            Holstered.Target = tool;
        }
        catch (Exception ex)
        {
            Logging.Logger.Warn($"ToolSnapper on {ParentHierarchyToString()} could not take the tool: {ex.Message}");
        }
    }

    // TAKING BACK OUT
    private void OnToolTaken(IGrabbable grabbable)
    {
        if (IsDestroyed || !Enabled.Value || !EquipOnGrab.Value)
            return;
        if (grabbable is not Grabbable carrier)
            return;

        var tool = _watchedTool;
        if (tool == null || tool.IsDestroyed || tool.BlockGripEquip.Value)
            return;

        var hand = carrier.Grabber?.Slot?.GetComponentInParents<HandTool>();
        if (hand == null || hand.IsDestroyed)
            return;

        // Only the hand that took it, on the machine that took it. Equipping is a change to that
        // user's own rig and no other peer gets to make it for them.
        var owner = hand.Slot?.ActiveUser;
        if (owner == null || !ReferenceEquals(owner, World?.LocalUser))
            return;

        RunSynchronously(() =>
        {
            if (hand.IsDestroyed || tool.IsDestroyed)
                return;
            hand.EquipToolItem(tool);
        });
    }

    // BLOCKING
    // An occupied holster owns its tool's parentage, the same bargain a socket makes: only a grab or
    // an equip takes it out, never a drop target or a tree drag.
    public bool AllowsReparent(Slot target, Slot newParent)
    {
        var current = CurrentTool;
        return current == null || !ReferenceEquals(current.Slot, target);
    }

    private bool IsParkedHere(ToolItem tool)
    {
        var rest = Rest;
        var toolSlot = tool?.Slot;
        return rest != null && toolSlot != null && !toolSlot.IsDestroyed
            && ReferenceEquals(toolSlot.Parent, rest);
    }

    private ToolItem? FindParkedTool()
    {
        var rest = Rest;
        if (rest == null || rest.IsDestroyed)
            return null;
        var children = rest.Children;
        for (int i = 0; i < children.Count; i++)
        {
            var tool = children[i].GetComponent<ToolItem>();
            if (tool != null && !tool.IsDestroyed)
                return tool;
        }
        return null;
    }

    private static ToolItem? ResolveTool(IGrabbable? grabbable)
    {
        var slot = (grabbable as Component)?.Slot;
        if (slot == null || slot.IsDestroyed)
            return null;
        return slot.GetComponent<ToolItem>() ?? slot.GetComponentInChildren<ToolItem>(includeSelf: false);
    }

    // Tags are read off a Magnet on the tool when it has one, so a holster and a magnet socket
    // filter on the same vocabulary instead of each inventing its own. A tool with no magnet falls
    // back to its equip name, which is the only other label it carries.
    private bool Accepts(ToolItem tool)
    {
        if (AcceptTags.Count == 0)
            return true;

        var magnet = tool.Slot?.GetComponent<Magnet>();
        foreach (var wanted in AcceptTags)
        {
            if (string.IsNullOrEmpty(wanted))
                continue;
            if (string.Equals(tool.EquipName.Value, wanted, StringComparison.Ordinal))
                return true;
            if (magnet == null)
                continue;
            foreach (var tag in magnet.Tags)
            {
                if (string.Equals(tag, wanted, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    public void BuildInspectorBody(UIBuilder ui)
    {
        var tool = CurrentTool;
        InspectorStats.AddRow(ui, "Holding", tool?.Slot?.SlotName.Value ?? "empty");
        InspectorStats.AddRow(ui, "Reach", $"{WorldRadius:0.###} m world");
        InspectorStats.AddRow(ui, "Accepts", AcceptTags.Count == 0 ? "any tool" : $"{AcceptTags.Count} tag(s)");
        InspectorStats.AddRow(ui, "On grab", EquipOnGrab.Value ? "equips to hand" : "carried");
    }
}
