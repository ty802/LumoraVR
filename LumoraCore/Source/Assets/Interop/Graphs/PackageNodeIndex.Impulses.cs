// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Variables;
using Lumora.Core.Persistence;

namespace Lumora.Core.Assets.Interop;

// Where a send goes: under one slot of the package, everywhere, nowhere (the package stops the flow), or under a
// slot worked out as it runs.
internal enum ImpulseReach { Under, Everywhere, Nowhere, Unknown }

// What a node sends to: a tag written out, none (blank or never set, which reaches nobody), or one worked out as
// it runs.
internal enum ImpulseTag { Named, Blank, Worked }

// What a send passes over: nothing, receivers that are switched off, or that decided as it runs.
internal enum ImpulseSkip { None, Off, Worked }

// Who hears a send. The package walks the sender's target slot and everything under it depth first, each slot's
// own receivers before its children's, children in child order: by order offset, then as stored. It runs every
// receiver of the sender's exact kind and type listening for exactly its tag, each to its end before the next.
// Node cards are never searched. A sender set to skip what is switched off passes over receivers on a slot that
// is off and receivers that are disabled.
//
// The target is worked out from what feeds it: a slot named outright, through sources, relays and globals, a
// global a drive in the graph aims, or the slot a named variable holds as the package stored it. A button with
// no target, and the world root, reach everything; a node whose target is nothing reaches nobody and stops. A
// target worked out as it runs reaches everything, and the sender says so. -xlinka
internal sealed partial class PackageNodeIndex
{
    // Each node's place in the order the package stored them, for receivers on one slot.
    private readonly Dictionary<PackageNode, int> _stored = new();
    private readonly Dictionary<PackageNode, (ImpulseReach Reach, string? Slot)> _targets = new();
    private readonly Dictionary<(PackageNode, string), List<PackageNode>> _reached = new();
    private readonly HashSet<PackageNode> _heard = new();
    private readonly Dictionary<(VariableScope, string), (ImpulseReach, string?)> _variables = new();
    private List<PackageNode>? _receivers;
    private Dictionary<string, int>? _slotRanks;
    private Dictionary<Slot, string>? _slotIds;

    public static bool IsImpulseSender(PackageNode node)
        => node.Class is "DynamicImpulseTrigger" or "DynamicImpulseTriggerWithValue" or "DynamicImpulseTriggerWithObject"
            || PackageNodeTable.IsButtonSender(node);

    // Whether any send reaches a receiver. One nobody reaches never runs, and what reads its value reads nothing.
    public bool IsHeard(PackageNode receiver) => _heard.Contains(receiver);

    // Every receiver any send of a node may run: for a tag worked out as it runs, every receiver under the target
    // with a tag of its own, one of which it picks as it runs.
    private List<PackageNode> Reached(PackageNode node)
    {
        var all = new List<PackageNode>();
        if (PackageNodeTable.IsButtonSender(node))
        {
            foreach (var tag in PackageNodeTable.ButtonTags(node))
                all.AddRange(ImpulseReceivers(node, tag));
        }
        else if (IsImpulseSender(node))
        {
            var (kind, tag) = SentTag(node);
            if (kind == ImpulseTag.Named)
                all.AddRange(ImpulseReceivers(node, tag));
            else if (kind == ImpulseTag.Worked && ImpulseTargetOf(node).Reach is ImpulseReach.Under or ImpulseReach.Everywhere)
                all.AddRange(ImpulseReceivers(node, null));
        }
        foreach (var receiver in all)
            _heard.Add(receiver);
        return all;
    }

    public (ImpulseTag Kind, string? Tag) SentTag(PackageNode sender)
    {
        string? id = sender.Ref("Tag");
        if (id == null || !TryNode(id, out var node, out var member))
            return (ImpulseTag.Blank, null);
        if (member.Length == 0 && (node.Class.StartsWith("ValueObjectInput", StringComparison.Ordinal)
            || node.Class.StartsWith("GlobalValue", StringComparison.Ordinal)))
            return node.Value("Value") is string tag && !string.IsNullOrWhiteSpace(tag) ? (ImpulseTag.Named, tag) : (ImpulseTag.Blank, null);
        return (ImpulseTag.Worked, null);
    }

    // A button keeps it as a setting, on by default for the plain one; a node reads it, off when unwired.
    public ImpulseSkip SkipOf(PackageNode sender)
    {
        if (PackageNodeTable.IsButtonSender(sender))
        {
            bool skips = sender.Value("ExcludeDisabled") is bool stored ? stored : sender.Class == "ButtonDynamicImpulseTrigger";
            return skips ? ImpulseSkip.Off : ImpulseSkip.None;
        }
        string? id = sender.Ref("ExcludeDisabled");
        for (int depth = 0; depth < 16; depth++)
        {
            if (id == null || !TryNode(id, out var node, out var member))
                return ImpulseSkip.None;
            if (member.Length > 0)
                return ImpulseSkip.Worked;
            if (node.Class.StartsWith("ValueRelay", StringComparison.Ordinal))
            {
                id = node.Ref("Input");
                continue;
            }
            if (node.Class is "ValueInput" or "GlobalValue")
                return node.Value("Value") is true ? ImpulseSkip.Off : ImpulseSkip.None;
            return ImpulseSkip.Worked;
        }
        return ImpulseSkip.Worked;
    }

    public (ImpulseReach Reach, string? Slot) ImpulseTargetOf(PackageNode sender)
    {
        if (!_targets.TryGetValue(sender, out var target))
            _targets[sender] = target = FindTarget(sender);
        return target;
    }

    // The receivers one send reaches, in the order the package runs them. A null tag takes every receiver with a
    // tag of its own. A receiver that stays off is left out for a sender that skips what is switched off.
    public IReadOnlyList<PackageNode> ImpulseReceivers(PackageNode sender, string? tag)
    {
        var key = (sender, tag ?? "\u0000");
        if (_reached.TryGetValue(key, out var found))
            return found;
        _reached[key] = found = new List<PackageNode>();
        if (ReceiverKind(sender) is not { } kind)
            return found;
        var (reach, under) = ImpulseTargetOf(sender);
        if (reach == ImpulseReach.Nowhere)
            return found;
        bool skipOff = SkipOf(sender) == ImpulseSkip.Off;
        foreach (var receiver in Receivers())
        {
            if (receiver.Class != kind.Class || (kind.Arg != null && (receiver.Args.Count == 0 || receiver.Args[0] != kind.Arg)))
                continue;
            string? listens = ConstantText(receiver.Ref("Tag"));
            if (tag != null ? !string.Equals(listens, tag, StringComparison.Ordinal) : string.IsNullOrWhiteSpace(listens))
                continue;
            if (!Searched(receiver.SlotId, reach == ImpulseReach.Under ? under : null) || (skipOff && OffForGood(receiver)))
                continue;
            found.Add(receiver);
        }
        return found;
    }

    // The nearest slot that came across at or above one of the package's, and whether a slot on the way that did
    // not come across was kept off: nothing here can turn that one on.
    public Slot? LiveSlot(string slotId, out bool offForGood)
    {
        offForGood = false;
        string? at = slotId;
        for (int depth = 0; at != null && depth < 512; depth++)
        {
            if (!_input.Slots.TryGetValue(at, out var record))
                return null;
            if (record.Slot is { IsDestroyed: false } live)
                return live;
            offForGood |= !Facts(at).Active;
            at = record.ParentId;
        }
        return null;
    }

    // The receiver class a sender reaches and its type. A button's value of a value type goes to receivers of
    // values; its text or address, like any reference, to receivers of objects.
    private static (string Class, string? Arg)? ReceiverKind(PackageNode sender)
    {
        string? arg = sender.Args.Count > 0 ? sender.Args[0] : null;
        return sender.Class switch
        {
            "DynamicImpulseTrigger" or "ButtonDynamicImpulseTrigger" => ("DynamicImpulseReceiver", null),
            "DynamicImpulseTriggerWithValue" when arg != null => ("DynamicImpulseReceiverWithValue", arg),
            "DynamicImpulseTriggerWithObject" or "ButtonDynamicImpulseTriggerWithReference" when arg != null => ("DynamicImpulseReceiverWithObject", arg),
            "ButtonDynamicImpulseTriggerWithValue" when arg != null
                => (arg is "string" or "Uri" ? "DynamicImpulseReceiverWithObject" : "DynamicImpulseReceiverWithValue", arg),
            _ => null,
        };
    }

    private (ImpulseReach, string?) FindTarget(PackageNode sender)
    {
        // A button's target the package had lost is no target at all.
        if (PackageNodeTable.IsButtonSender(sender))
            return sender.Ref("Target") is { } named && Holds(named) ? (ImpulseReach.Under, named) : (ImpulseReach.Everywhere, null);
        string? id = sender.Ref("TargetHierarchy");
        for (int depth = 0; depth < 32; depth++)
        {
            if (id == null)
                return (ImpulseReach.Nowhere, null);
            if (!TryNode(id, out var node, out var member))
            {
                if (_input.Slots.ContainsKey(id))
                    return (ImpulseReach.Under, id);
                return Holds(id) ? (ImpulseReach.Unknown, null) : (ImpulseReach.Nowhere, null);
            }
            if (member.Length > 0)
                return node.Class == "DynamicVariableObjectInput" && member == "Value" ? FromVariable(node) : (ImpulseReach.Unknown, null);
            switch (node.Class)
            {
                case "GlobalReference":
                    id = DriveOf(node) is { } drive ? drive.Ref("Target") : node.Ref("Reference");
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
                    return (ImpulseReach.Everywhere, null);
                default:
                    return (ImpulseReach.Unknown, null);
            }
        }
        return (ImpulseReach.Unknown, null);
    }

    // The reference drive in the graph aiming a global reference node, if one does.
    private PackageNode? DriveOf(PackageNode global)
        => global.Data.TryGetNode("Reference") is DataTreeDictionary member
            && (member.TryGetNode("ID") as DataTreeValue)?.Value is string memberId
            && DriverOf(memberId)?.Ref("Node") is { } driveId && Nodes.TryGetValue(driveId, out var drive) ? drive : null;

    // The slot a named variable holds, read where the package reads it: the nearest space up from the reader that
    // takes the name, and the variable of that name bound to that space.
    private (ImpulseReach, string?) FromVariable(PackageNode reader)
    {
        if (ConstantText(reader.Ref("VariableName")) is not { } path || LiveSlot(reader.SlotId, out _) is not { } from)
            return (ImpulseReach.Unknown, null);
        Variables.ParsePath(path, out var spaceName, out var name);
        if (string.IsNullOrEmpty(name) || SpaceOf(from, spaceName) is not { } space || space.Slot is not { } spaceSlot)
            return (ImpulseReach.Unknown, null);
        if (!_variables.TryGetValue((space, name), out var found))
            _variables[(space, name)] = found = HeldBy(space, spaceSlot, name);
        return found;
    }

    private (ImpulseReach, string?) HeldBy(VariableScope space, Slot spaceSlot, string name)
    {
        foreach (var variable in spaceSlot.GetComponentsInChildren<ReferenceVariable<Slot>>())
        {
            Variables.ParsePath(variable.VariableName.Value, out var theirSpace, out var theirName);
            if (theirName != name || !ReferenceEquals(SpaceOf(variable.Slot, theirSpace), space))
                continue;
            if (variable.Reference.Target is not { IsDestroyed: false } held)
                return (ImpulseReach.Nowhere, null);
            return SlotIds().TryGetValue(held, out var heldId) ? (ImpulseReach.Under, heldId) : (ImpulseReach.Unknown, null);
        }
        return (ImpulseReach.Unknown, null);
    }

    private static VariableScope? SpaceOf(Slot? slot, string? name)
    {
        for (var at = slot; at != null; at = at.Parent)
        {
            foreach (var space in at.GetComponents<VariableScope>())
            {
                if (string.IsNullOrWhiteSpace(name) ? !space.DirectBindingOnly.Value
                    : string.Equals(Variables.ProcessName(space.ScopeName.Value), name, StringComparison.Ordinal))
                    return space;
            }
        }
        return null;
    }

    private Dictionary<Slot, string> SlotIds()
    {
        if (_slotIds != null)
            return _slotIds;
        _slotIds = new Dictionary<Slot, string>(ReferenceEqualityComparer.Instance);
        foreach (var record in _input.Slots.Values)
        {
            if (record.Slot != null)
                _slotIds.TryAdd(record.Slot, record.Id);
        }
        return _slotIds;
    }

    // Whether a walk from the ancestor (the whole package when null) comes to the slot, no node card on the way.
    private bool Searched(string slotId, string? ancestor)
    {
        string? at = slotId;
        for (int depth = 0; at != null && depth < 512; depth++)
        {
            if (Facts(at).NodeCard)
                return false;
            if (ancestor != null && string.Equals(at, ancestor, StringComparison.OrdinalIgnoreCase))
                return true;
            at = _input.Slots.TryGetValue(at, out var record) ? record.ParentId : null;
        }
        return ancestor == null;
    }

    // Disabled, or on a slot kept off that did not come across.
    private bool OffForGood(PackageNode receiver)
    {
        if (receiver.Value("Enabled") is false)
            return true;
        if (Helpers.TryGetValue(receiver, out var helpers))
        {
            foreach (var helper in helpers)
            {
                if (helper.Value("Enabled") is false)
                    return true;
            }
        }
        LiveSlot(receiver.SlotId, out bool off);
        return off;
    }

    // Every receiver in the order a walk of the whole package meets them.
    private List<PackageNode> Receivers()
    {
        if (_receivers != null)
            return _receivers;
        var ranks = SlotRanks();
        _receivers = new List<PackageNode>();
        foreach (var node in Nodes.Values)
        {
            if (node.Class is "DynamicImpulseReceiver" or "DynamicImpulseReceiverWithValue" or "DynamicImpulseReceiverWithObject")
                _receivers.Add(node);
        }
        int Rank(PackageNode node) => ranks.TryGetValue(node.SlotId, out var rank) ? rank : int.MaxValue;
        _receivers.Sort((a, b) =>
        {
            int bySlot = Rank(a).CompareTo(Rank(b));
            return bySlot != 0 ? bySlot : _stored[a].CompareTo(_stored[b]);
        });
        return _receivers;
    }

    // Every slot's place in a depth-first walk of the package in child order.
    private Dictionary<string, int> SlotRanks()
    {
        if (_slotRanks != null)
            return _slotRanks;
        var children = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();
        foreach (var record in _input.Slots.Values)
        {
            if (record.ParentId == null || !_input.Slots.ContainsKey(record.ParentId))
            {
                roots.Add(record.Id);
                continue;
            }
            if (!children.TryGetValue(record.ParentId, out var list))
                children[record.ParentId] = list = new List<string>();
            list.Add(record.Id);
        }
        int ChildOrder(string a, string b)
        {
            var (x, y) = (Facts(a), Facts(b));
            int byOffset = x.OrderOffset.CompareTo(y.OrderOffset);
            return byOffset != 0 ? byOffset : x.Sequence.CompareTo(y.Sequence);
        }
        roots.Sort(ChildOrder);
        foreach (var list in children.Values)
            list.Sort(ChildOrder);
        _slotRanks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var walk = new Stack<string>();
        for (int i = roots.Count - 1; i >= 0; i--)
            walk.Push(roots[i]);
        while (walk.Count > 0)
        {
            string id = walk.Pop();
            _slotRanks[id] = _slotRanks.Count;
            if (!children.TryGetValue(id, out var list))
                continue;
            for (int i = list.Count - 1; i >= 0; i--)
                walk.Push(list[i]);
        }
        return _slotRanks;
    }

    private PackageSlotFacts Facts(string slotId)
        => _input.SlotFacts.TryGetValue(slotId, out var facts) ? facts : new PackageSlotFacts(0, int.MaxValue, true, false);
}
