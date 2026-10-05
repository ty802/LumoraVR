// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Touch;
using Lumora.Core.Persistence;

namespace Lumora.Core.Assets.Interop;

// Buttons that send to node graphs by tag. The importer hands them over with the nodes, and each is joined into
// the graph of the receivers it reaches as the event on its own slot that starts them: a press, a release, the
// beam coming on, staying or going off. A press is heard as it lands, every frame it is held and as it ends, on a
// panel button and a touch control alike. A value or reference it sends is written into the Stored Value its
// receivers read, the same one any other sender of that tag in the graph writes. Who hears it is worked out as
// for any send (PackageNodeIndex.Impulses.cs): receivers under the slot it names, or every one when it names
// none. -xlinka
internal static partial class PackageNodeTable
{
    private static readonly (string Event, string Tag, string Data)[] ButtonSends =
    {
        ("Pressed", "PressedTag", "PressedData"),
        ("Pressing", "PressingTag", "PressingData"),
        ("Released", "ReleasedTag", "ReleasedData"),
        ("HoverEnter", "HoverEnterTag", "HoverEnterData"),
        ("HoverStay", "HoverStayTag", "HoverStayData"),
        ("HoverLeave", "HoverLeaveTag", "HoverLeaveData"),
    };

    internal static bool IsButtonSender(PackageNode node)
        => node.Class is "ButtonDynamicImpulseTrigger" or "ButtonDynamicImpulseTriggerWithValue" or "ButtonDynamicImpulseTriggerWithReference";

    private static void Buttons()
        => Same(SendFromButton, "ButtonDynamicImpulseTrigger", "ButtonDynamicImpulseTriggerWithValue", "ButtonDynamicImpulseTriggerWithReference");

    // The tags a button sends, one per event that has one.
    internal static IEnumerable<string> ButtonTags(PackageNode button)
    {
        foreach (var (_, tagMember, dataMember) in ButtonSends)
        {
            if (ButtonTag(button, tagMember, dataMember) is { } tag)
                yield return tag;
        }
    }

    private static void SendFromButton(PackageNodeScope s)
    {
        if (s.Index.World(s.Node.SlotId) is not Slot button)
        {
            s.Problem("sits on a slot that did not come across, so it never sends");
            return;
        }
        var skip = s.Index.SkipOf(s.Node);
        foreach (var (evt, tagMember, dataMember) in ButtonSends)
        {
            if (ButtonTag(s.Node, tagMember, dataMember) is not { } tag)
                continue;
            var receivers = s.Index.ImpulseReceivers(s.Node, tag);
            if (receivers.Count == 0)
                continue;
            string? ours = evt switch
            {
                "Pressed" => "OnPressBegin",
                "Pressing" => "OnPressHold",
                "Released" => "OnPressEnd",
                "HoverEnter" => "OnHoverEnter",
                "HoverStay" => "OnHoverStay",
                "HoverLeave" => "OnHoverExit",
                _ => null,
            };
            if (ours == null)
            {
                s.Problem("its sends while a press is held are not carried");
                continue;
            }
            string heard = s.Add(ours);
            s.Bind(heard, "source", button, "slot");
            string? from = s.Node.Class == "ButtonDynamicImpulseTrigger" ? heard : KeepButtonValue(s, dataMember, receivers, heard);
            if (from == null)
                continue;
            var steps = new List<Action<string, string>>();
            foreach (var receiver in receivers)
                steps.Add(ReceiverStep(s, receiver, skip, null));
            InTurn(s, from, "next", steps);
        }
    }

    // The value or reference the event sends, written after the event where its receivers read it: answers the
    // last write, or null for a type with no counterpart, which then sends nothing.
    private static string? KeepButtonValue(PackageNodeScope s, string dataMember, IReadOnlyList<PackageNode> receivers, string heard)
    {
        bool reference = s.Node.Class == "ButtonDynamicImpulseTriggerWithReference";
        string? raw = s.Node.Args.Count > 0 ? s.Node.Args[0] : null;
        string? type = reference ? ObjectType(raw) : s.Arg(0);
        if (type == null)
        {
            s.Problem($"sends a {raw ?? "value"}, which has no counterpart here");
            return null;
        }
        var data = s.Node.Data.TryGetNode(dataMember) as DataTreeDictionary;
        IWorldElement? element = null;
        DataTreeNode? value = null;
        if (reference)
        {
            string? id = (data?.TryGetNode("Reference") as DataTreeDictionary)?.TryGetNode("Data") is DataTreeValue { Value: string text }
                && text.Length > 0 && !PackageMemberMapper.IsNullId(text) ? text : null;
            element = s.Index.World(id);
            if (element == null && id != null)
                s.Problem($"sends {s.Index.Describe(id)}, so its receivers read nothing");
        }
        else
        {
            value = (data?.TryGetNode("Value") as DataTreeDictionary)?.TryGetNode("Data");
        }
        string last = heard;
        foreach (var store in ImpulseStores(s, type, receivers))
        {
            string write = s.Add("WriteValue", type);
            s.Set(write, "var", store);
            if (element != null)
                s.Bind(write, "value", element, type);
            else if (value != null)
                s.Set(write, "value", value);
            s.Flow(last, "next", write);
            last = write;
        }
        return last;
    }

    private static string? ButtonTag(PackageNode button, string tagMember, string dataMember)
    {
        object? stored = button.Class == "ButtonDynamicImpulseTrigger"
            ? button.Value(tagMember)
            : ((button.Data.TryGetNode(dataMember) as DataTreeDictionary)?.TryGetNode("Tag") as DataTreeDictionary)?.TryGetNode("Data") is DataTreeValue tag
                ? tag.Value
                : null;
        return stored is string text && !string.IsNullOrWhiteSpace(text) ? text : null;
    }
}
