// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lumora.Core.Assets.Interop;

// Sends by tag, from nodes and buttons alike. A send runs each receiver it reaches in turn, in the order the
// package does (PackageNodeIndex.Impulses.cs), then goes on; the receivers were joined into the sender's graph
// when the graphs were found. A value sent goes first into the Stored Value each receiver reads as its own. A tag
// worked out as it runs is kept as sent and compared with each receiver's under the target. Where the sender
// passes over what is switched off, each receiver runs only while its slot is on. -xlinka
internal static partial class PackageNodeTable
{
    private static void SendImpulse(PackageNodeScope s)
    {
        var index = s.Index;
        var (reach, _) = index.ImpulseTargetOf(s.Node);
        if (reach == ImpulseReach.Nowhere)
        {
            // The package stops at a target that is nothing: no receiver runs and flow goes no further.
            s.OutConstant("TriggeredCount", 0d);
            s.OpEnds("");
            return;
        }
        var (kind, tag) = index.SentTag(s.Node);
        if (kind == ImpulseTag.Worked && reach == ImpulseReach.Unknown)
        {
            s.Fail("sends to a tag worked out as it runs, under a slot worked out as it runs, which is not carried");
            return;
        }
        string? type = null;
        if (s.Node.Class != "DynamicImpulseTrigger")
        {
            type = s.Node.Class == "DynamicImpulseTriggerWithValue" ? s.Arg(0) : ObjectType(s.Node.Args.Count > 0 ? s.Node.Args[0] : null);
            if (type == null)
            {
                s.Fail("its value's type has no counterpart here");
                return;
            }
        }
        if (reach == ImpulseReach.Unknown)
            s.Problem("sends under a slot worked out as it runs; here it reaches every receiver of its tag");
        var receivers = kind == ImpulseTag.Blank ? Array.Empty<PackageNode>() : index.ImpulseReceivers(s.Node, tag);
        var skip = index.SkipOf(s.Node);
        if (receivers.Count > 0 && (kind == ImpulseTag.Worked || skip != ImpulseSkip.None) && s.IsRead("TriggeredCount"))
            s.Problem("counts every receiver it may reach, switched off or listening for another tag");
        s.OutConstant("TriggeredCount", (double)receivers.Count);

        // What is sent is kept before any receiver runs.
        string? last = null;
        void Then(string node)
        {
            if (last == null)
                s.Op("", node);
            else
                s.Flow(last, "next", node);
            last = node;
        }
        if (type != null && receivers.Count > 0)
        {
            foreach (var store in ImpulseStores(s, type, receivers))
            {
                string write = s.Add("WriteValue", type);
                s.Set(write, "var", store);
                s.In("Value", write, "value");
                Then(write);
            }
        }
        string? sentTag = null;
        if (kind == ImpulseTag.Worked && receivers.Count > 0)
        {
            sentTag = s.Add("StoredValue", "string");
            string write = s.Add("WriteValue", "string");
            s.Set(write, "var", sentTag);
            s.In("Tag", write, "value");
            Then(write);
        }

        var steps = new List<Action<string, string>>();
        foreach (var receiver in receivers)
            steps.Add(ReceiverStep(s, receiver, skip, sentTag));
        steps.Add((node, exit) => s.Exit("Next", node, exit));
        if (last == null && steps.Count == 1)
        {
            s.OpThrough("", "Next");
            return;
        }
        InTurn(s, last, "next", steps);
    }

    // Reached through its senders; it adds nothing of its own. What reads the value of one nobody reaches reads
    // nothing.
    private static void ReceiveImpulse(PackageNodeScope s)
    {
        if (s.Index.ConstantText(s.Node.Ref("Tag")) == null)
        {
            s.Fail("listens for a tag worked out as it runs, which is not carried");
            return;
        }
        if (s.Node.Class != "DynamicImpulseReceiver" && !s.Index.IsHeard(s.Node))
            s.OutConstant("Value", null);
    }

    // One receiver's flow from one exit: past the tag test for a tag worked out as it runs, then past the test of
    // its slot being on for a sender that skips what is switched off.
    private static Action<string, string> ReceiverStep(PackageNodeScope s, PackageNode receiver, ImpulseSkip skip, string? sentTag)
        => (node, exit) =>
        {
            if (sentTag != null)
            {
                string same = s.Add("IsSame", "string");
                s.Wire(sentTag, "value", same, "a");
                s.Set(same, "b", s.Index.ConstantText(receiver.Ref("Tag"))!);
                string match = s.Add("If");
                s.Flow(node, exit, match);
                s.Wire(same, "same", match, "condition");
                (node, exit) = (match, "true");
            }
            if (SwitchedOn(s, receiver, skip) is { } gate)
            {
                s.Flow(node, exit, gate);
                (node, exit) = (gate, "true");
            }
            s.ExitFrom(receiver, "OnTriggered", node, exit);
        };

    // An If letting the receiver run only while its slot is on, or while the sender does not skip, when that is
    // decided as it runs. Null when nothing is asked. A receiver that stays off never gets here for a sender that
    // always skips.
    private static string? SwitchedOn(PackageNodeScope s, PackageNode receiver, ImpulseSkip skip)
    {
        if (skip == ImpulseSkip.None)
            return null;
        var slot = s.Index.LiveSlot(receiver.SlotId, out bool offForGood);
        string? on = null;
        if (slot != null && !offForGood)
        {
            on = s.Add("SlotActive");
            s.Bind(on, "slot", slot, "slot");
        }
        if (skip == ImpulseSkip.Off && on == null)
            return null;
        string gate = s.Add("If");
        if (skip == ImpulseSkip.Off)
        {
            s.Wire(on!, "active", gate, "condition");
            return gate;
        }
        string keeps = s.Add("Not", "bool");
        s.In("ExcludeDisabled", keeps, "value");
        if (on == null)
        {
            s.Wire(keeps, "result", gate, "condition");
            return gate;
        }
        string either = s.Add("Or", "bool,bool");
        s.Wire(keeps, "result", either, "a");
        s.Wire(on, "active", either, "b");
        s.Wire(either, "result", gate, "condition");
        return gate;
    }

    // Steps in order from one exit, or from the package node's own flow in when there is none: Sequence branches,
    // three to a Sequence and its last branch on to the next, four in the last.
    private static void InTurn(PackageNodeScope s, string? from, string exit, List<Action<string, string>> steps)
    {
        if (from != null && steps.Count == 1)
        {
            steps[0](from, exit);
            return;
        }
        int at = 0;
        while (at < steps.Count)
        {
            string sequence = s.Add("Sequence");
            if (from == null)
                s.Op("", sequence);
            else
                s.Flow(from, exit, sequence);
            int here = steps.Count - at <= 4 ? steps.Count - at : 3;
            for (int k = 0; k < here; k++)
                steps[at + k](sequence, "out" + k.ToString(CultureInfo.InvariantCulture));
            at += here;
            from = sequence;
            exit = "out3";
        }
    }

    // The Stored Values a sent value goes into: each one a receiver already reads, and for receivers reading none
    // yet, the first of those or a new one.
    private static List<string> ImpulseStores(PackageNodeScope s, string type, IReadOnlyList<PackageNode> receivers)
    {
        var stores = new List<string>();
        foreach (var receiver in receivers)
        {
            if (s.Writer.OutputOf(receiver, "Value") is PackageGraphWriter.Port port && !stores.Contains(port.Node))
                stores.Add(port.Node);
        }
        string? shared = stores.Count > 0 ? stores[0] : null;
        foreach (var receiver in receivers)
        {
            if (s.Writer.HasOutput(receiver, "Value"))
                continue;
            if (shared == null)
            {
                shared = s.Add("StoredValue", type);
                stores.Add(shared);
            }
            s.Writer.Output(receiver, "Value", new PackageGraphWriter.Port(shared, "value"));
        }
        return stores;
    }
}
