// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets.Interop;

// Package nodes first seen in worlds: slots and transforms.
//
// A package setter stops the call when its slot is nothing or gone, where ours writes nothing and goes on. A
// slot named outright goes straight in, one worked out passes an Is Null first, and one never set stops the
// call there. Unwired rotations and scales write what the package writes: identity and one.
//
// The importer mirrored the world across x, which swaps a slot's own left and right: our right, carried back
// over the mirror at the node's port, is the package's left. So a read of one side is our read of the other;
// forward, back, up and down cross as they are. -xlinka
internal static partial class PackageNodeTable
{
    private enum SlotArrival { Worked, Never, Named }

    private static void WorldD()
    {
        double[] zero = { 0d, 0d, 0d };
        double[] identity = { 0d, 0d, 0d, 1d };
        double[] one = { 1d, 1d, 1d };

        On("SetLocalPosition", s => SetterOnSlot(s, ("SetSlotLocalPosition", "Position", "position", zero)));
        On("SetGlobalPosition", s => SetterOnSlot(s, ("SetSlotPosition", "Position", "position", zero)));
        On("SetLocalRotation", s => SetterOnSlot(s, ("SetSlotLocalRotation", "Rotation", "rotation", identity)));
        On("SetGlobalRotation", s => SetterOnSlot(s, ("SetSlotRotation", "Rotation", "rotation", identity)));
        On("SetLocalScale", s => SetterOnSlot(s, ("SetSlotLocalScale", "Scale", "scale", one)));
        On("SetGlobalPositionRotation", s => SetterOnSlot(s,
            ("SetSlotPosition", "Position", "position", zero), ("SetSlotRotation", "Rotation", "rotation", identity)));
        On("SetGlobalTransform", s => SetterOnSlot(s,
            ("SetSlotPosition", "Position", "position", zero), ("SetSlotRotation", "Rotation", "rotation", identity),
            ("SetSlotScale", "Scale", "scale", one)));
        // The package names the slot null rather than empty; ours writes either as empty.
        On("SetSlotName", s => SetterOnSlot(s, ("SetSlotName", "Name", "name", null)));
        // Both keep the up as near as they can; a zero direction turns nothing.
        On("SetForward", s =>
        {
            if (SetterOnSlot(s, ("SetSlotDirection", "Forward", "direction", zero)) is { } aim)
                s.Set(aim, "axis", "Forward");
        });

        // With nothing wired the package writes nothing, but still stops at a missing slot.
        On("SetSlotPersistentSelf", s =>
        {
            var arrival = HowSlotArrives(s, "Instance", out _);
            if (arrival == SlotArrival.Never)
            {
                s.OpEnds("");
                return;
            }
            var gate = PastNullSlot(s, arrival);
            if (!s.Wired("Persistent"))
            {
                if (gate is { } open)
                    s.Exit("Next", open.Node, open.Exit);
                else
                    s.OpThrough("", "Next");
                return;
            }
            string set = s.Add("SetSlotPersistent");
            s.In("Instance", set, "slot");
            s.In("Persistent", set, "persistent", false);
            if (gate is { } through)
                s.Flow(through.Node, through.Exit, set);
            else
                s.Op("", set);
            s.Exit("Next", set, "next");
        });

        Map("GetForward", "SlotDirection", null, ins: "Instance=slot", outs: "=direction", set: "axis=Forward");
        Map("GetBackward", "SlotDirection", null, ins: "Instance=slot", outs: "=direction", set: "axis=Backward");
        Map("GetLeft", "SlotDirection", null, ins: "Instance=slot", outs: "=direction", set: "axis=Right");
        Map("GetRight", "SlotDirection", null, ins: "Instance=slot", outs: "=direction", set: "axis=Left");

        // The world scale over the slot's own, axis by axis, a zero axis giving infinity on both; no slot leaves
        // it as it is. The package divides by the size its matrix decomposes to, ours by each level's scale
        // multiplied out, which differ only under a skewed chain.
        On("GlobalScaleToLocal", s =>
        {
            var arrival = HowSlotArrives(s, "Instance", out _);
            if (arrival == SlotArrival.Never)
            {
                s.OutThrough("", "GlobalScale");
                return;
            }
            string size = s.Add("SlotScale");
            s.In("Instance", size, "slot");
            string divide = s.Add("Divide", "float3,float3");
            s.In("GlobalScale", divide, "a", zero);
            s.Wire(size, "scale", divide, "b");
            if (arrival == SlotArrival.Named)
            {
                s.Out("", divide, "result");
                return;
            }
            string none = s.Add("IsNull", "slot");
            s.In("Instance", none, "value");
            string pick = s.Add("Conditional", "float3");
            s.Wire(none, "null", pick, "condition");
            s.In("GlobalScale", pick, "whenTrue", zero);
            s.Wire(divide, "result", pick, "whenFalse");
            s.Out("", pick, "result");
        });

        // The package fires before the save starts, so what it changes is saved; ours hears the save once it has
        // landed, on the machine that made it.
        On("OnSaving", s =>
        {
            string saved = s.Add("OnWorldSaved");
            s.Exit("Trigger", saved, "next");
        });
        Same(_ => { }, "OnSaving+Proxy");

        // Sourced events on the watched slot, which a drive in the graph may aim at the world root. Child is the
        // slot of whichever fired last, kept for its readers when both are wired. The package's OnUser picks the
        // machine; ours runs on the script's.
        On("SlotChildrenEvents", s =>
        {
            var arrival = HowSlotArrives(s, "Instance", out var watched);
            if (arrival == SlotArrival.Never)
            {
                s.OutConstant("Child", null);
                return;
            }
            if (watched == null)
            {
                s.Fail(arrival == SlotArrival.Named ? "listens to a slot that did not come across"
                    : "listens to a slot worked out as it runs, which ours cannot follow");
                return;
            }
            bool both = s.Wired("OnChildAdded") && s.Wired("OnChildRemoved") && s.IsRead("Child");
            string? kept = both ? s.Add("StoredValue", "slot") : null;
            foreach (var (call, ours) in new[] { ("OnChildAdded", "OnChildAdded"), ("OnChildRemoved", "OnChildRemoved") })
            {
                if (!s.Wired(call))
                    continue;
                string heard = s.Add(ours);
                s.Bind(heard, "source", watched, "slot");
                if (kept == null)
                {
                    s.Out("Child", heard, "child");
                    s.Exit(call, heard, "next");
                    continue;
                }
                string keep = s.Add("WriteValue", "slot");
                s.Set(keep, "var", kept);
                s.Wire(heard, "child", keep, "value");
                s.Flow(heard, "next", keep);
                s.Exit(call, keep, "next");
            }
            if (kept != null)
                s.Out("Child", kept, "value");
        });
    }

    // Our setters in a row, each on the package node's slot with its value, where the package node stops when the
    // slot is missing. Answers the last setter, or null when flow can only stop.
    private static string? SetterOnSlot(PackageNodeScope s, params (string Ours, string Theirs, string Port, object? Unwired)[] parts)
    {
        var arrival = HowSlotArrives(s, "Instance", out _);
        if (arrival == SlotArrival.Never)
        {
            s.OpEnds("");
            return null;
        }
        string? last = null;
        foreach (var (ours, theirs, port, unwired) in parts)
        {
            string set = s.Add(ours);
            s.In("Instance", set, "slot");
            s.In(theirs, set, port, unwired);
            if (last != null)
                s.Flow(last, "next", set);
            else if (PastNullSlot(s, arrival) is { } gate)
                s.Flow(gate.Node, gate.Exit, set);
            else
                s.Op("", set);
            last = set;
        }
        s.Exit("Next", last!, "next");
        return last;
    }

    // Flow into the package node, through an Is Null test when its slot is worked out as it runs: answers where
    // flow goes on with a slot there, or null when it goes straight in.
    private static (string Node, string Exit)? PastNullSlot(PackageNodeScope s, SlotArrival arrival)
    {
        if (arrival != SlotArrival.Worked)
            return null;
        string none = s.Add("IsNull", "slot");
        s.In("Instance", none, "value");
        string check = s.Add("If");
        s.Wire(none, "null", check, "condition");
        s.Op("", check);
        return (check, "false");
    }

    // What a slot input comes to before anything runs: a slot named outright, through relays, sources and
    // globals, a global a reference drive in the graph aims, and the world root; nothing ever, where the chain
    // ends unset or at something the package had already lost; or something worked out as it runs. A slot the
    // package held that did not come across counts as named, with no slot.
    private static SlotArrival HowSlotArrives(PackageNodeScope s, string input, out Slot? slot)
    {
        slot = null;
        string? id = s.Node.Ref(input);
        for (int depth = 0; depth < 32; depth++)
        {
            if (id == null)
                return SlotArrival.Never;
            if (!s.Index.TryNode(id, out var node, out var member))
            {
                slot = s.Index.World(id) switch { Slot named => named, Component component => component.Slot, _ => null };
                return slot != null || s.Index.Holds(id) ? SlotArrival.Named : SlotArrival.Never;
            }
            if (member.Length > 0)
                return SlotArrival.Worked;
            switch (node.Class)
            {
                case "GlobalReference":
                    id = DriveAiming(s, node) is { } drive ? drive.Ref("Target") : node.Ref("Reference");
                    break;
                case "ElementSource" or "SlotSource":
                    id = node.Ref("Source");
                    break;
                case "ObjectRelay":
                    id = node.Ref("Input");
                    break;
                case "RefObjectInput":
                    id = node.Ref("Target");
                    break;
                case "RootSlot":
                    slot = s.Home.World.RootSlot;
                    return SlotArrival.Named;
                default:
                    return SlotArrival.Worked;
            }
        }
        return SlotArrival.Worked;
    }

    // The reference drive in the graph aiming a global reference node, if one does.
    private static PackageNode? DriveAiming(PackageNodeScope s, PackageNode global)
    {
        if (global.Data.TryGetNode("Reference") is not Persistence.DataTreeDictionary member
            || (member.TryGetNode("ID") as Persistence.DataTreeValue)?.Value is not string memberId
            || s.Index.DriverOf(memberId)?.Ref("Node") is not { } driveId)
            return null;
        return s.Index.Nodes.TryGetValue(driveId, out var drive) ? drive : null;
    }
}
