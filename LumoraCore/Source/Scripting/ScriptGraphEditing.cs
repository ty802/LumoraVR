// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lumora.Core.Assets;
using Lumora.Core.Components;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Scripting;
using Lumora.Core.Localization;
using Lumora.Script;
using LumoraLogger = Lumora.Core.Logging.Logger;

namespace Lumora.Core.Scripting;

// The editing operations on a script, each one undo step for whoever does it.
//
// A node placed on empty space starts a group of its own: an object called Nodes that nothing shows of,
// holding the script the group runs as and its open graph. Wiring nodes of two groups puts the groups
// together first, in the same step, wherever they are and whoever placed them. A group runs live while it
// is built, and becomes a Script object only when it is packed. A graph open on a scripted object or a
// packed script is a script of its own: it takes a group in, and two of those never join.
//
// Unpack writes the host's current graph out as node slots under it; a host with no graph unpacks to an
// empty space. Pack checks the space, stores the document in this machine's asset store, points the
// host at it and removes the node slots.
//
// A slot picked up as a card works on everything under the slot: every script anywhere under it unpacks,
// or packs back, at once; and nodes standing on their own pack into it, becoming its script.
//
// A user's script never runs a graph its author did not put there. Packing it as someone else either
// takes it over, a new host minted by the packer in its place, when the packer may remove the author's;
// or leaves the new graph in place, stopped, until the author runs it as their own.
//
// A shader graph is edited with the same operations and its own nodes. Its loose groups have no script,
// its wires convert on their own and widen the node they land on, and it never packs into a document: it
// closes, packed in place with the material it makes the shader of.
//
// Showing or putting a graph away never touches its slot's switch, which stays on. Switching it off is
// how a graph is stopped. -xlinka
public static partial class ScriptGraphEditing
{
    public static readonly LocaleText UnpackStep = LocaleText.Keyed("Loom.Undo.Unpack", "Edit Script");
    public static readonly LocaleText PackStep = LocaleText.Keyed("Loom.Undo.Pack", "Pack Script");
    public static readonly LocaleText WireStep = LocaleText.Keyed("Loom.Undo.Wire", "Wire Nodes");
    public static readonly LocaleText AddNodeStep = LocaleText.Keyed("Loom.Undo.AddNode", "Add Node");
    public static readonly LocaleText ValueStep = LocaleText.Keyed("Loom.Undo.Value", "Set Node Value");
    public static readonly LocaleText JoinStep = LocaleText.Keyed("Loom.Undo.Join", "Join Groups");
    public static readonly LocaleText CutStep = LocaleText.Keyed("Loom.Undo.Cut", "Cut Wires");
    public static readonly LocaleText ConvertStep = LocaleText.Keyed("Loom.Undo.Convert", "Convert Wire");
    public static readonly LocaleText RelayStep = LocaleText.Keyed("Loom.Undo.Relay", "Add Relay");

    // The nodes a relay on a data wire and on a flow wire are.
    public const string DataRelayNode = "Relay";
    public const string FlowRelayNode = "FlowRelay";

    // What the object of a group placing a node started is called, and what packing it makes of it.
    public const string GroupSlotName = "Nodes";
    public const string ScriptSlotName = "Script";

    private const string TwoScripts =
        "those nodes are in two different scripts, which never join: place the node again inside the script that needs it";

    private const string TwoShaders =
        "those nodes are in two different shader graphs, which never join: place the node again inside the graph that needs it";

    private const string TwoKinds = "a shader node and a script node never join";

    // The nodes offered between two ends that do not match, in the order they are offered.
    private static readonly string[] ConversionNodes =
    {
        "ToText", "ToFloat", "ToInt", "ToDouble", "ToLong", "ToUInt", "ToULong", "ToShort", "ToUShort", "ToByte", "ToSByte", "ToHalf",
        "ParseNumber", "ParseInt", "Parse",
        "JsonParseObject", "JsonParseArray", "JsonParse", "JsonToText", "JsonAsObject", "JsonAsArray", "JsonFromValue", "JsonToValue",
    };

    private const float BodyDepth = 0.01f;
    private const float BodyGap = 0.002f;

    // A group of its own at a point, for a node placed on empty space: an object holding the script the group
    // runs as and an open, empty graph whose origin is the point, turned to face the given point. Nothing of
    // it shows; its nodes are what people see and grab.
    public static ScriptGraphSpace? NewGroup(World world, Math.float3 at, Math.float3 facing, out string? error,
        GraphKind kind = GraphKind.Script)
    {
        var space = MakeGroup(world, at, facing, kind, out error);
        if (space != null)
            InspectorUndo.Record(world, SlotExistenceUndoBatch.Created(world, new[] { space.Slot.Parent! }, AddNodeStep));
        return space;
    }

    // A node on empty space, in a group of its own there with the card's top edge centred on the point: the
    // group and the node are one undo step. A shader node's group is a shader graph that belongs to no
    // material yet; wiring it into a material's graph puts it there.
    public static ScriptGraphNode? PlaceNode(World world, string type, string? signature, Math.float3 at, Math.float3 facing,
        out string? error, GraphKind kind = GraphKind.Script)
    {
        if (!Resolves(GraphLanguage.For(kind), type, signature, out _, out error))
            return null;
        var space = MakeGroup(world, at, facing, kind, out error);
        if (space == null)
            return null;
        var group = space.Slot.Parent!;
        var place = Math.float3.Zero;
        ScriptGraphNode? node = null;
        try
        {
            node = MakeNode(space, type, signature, place, out error);
            if (node != null)
                node.Slot.LocalPosition.Value = new Math.float3(-ScriptCardLayout.For(node, 0).Width * ScriptEditorStyle.CardScale * 0.5f, 0f, 0f);
        }
        finally
        {
            // A group nothing was placed in is not left behind.
            if (node == null && !group.IsDestroyed)
                group.Destroy();
        }
        if (node != null)
            InspectorUndo.Record(world, SlotExistenceUndoBatch.Created(world, new[] { group }, AddNodeStep));
        return node;
    }

    private static ScriptGraphSpace? MakeGroup(World world, Math.float3 at, Math.float3 facing, GraphKind kind, out string? error)
    {
        error = null;
        Slot slot;
        try
        {
            slot = world.RootSlot.AddSlot(GroupSlotName);
        }
        catch (UnauthorizedAccessException)
        {
            error = "you may not add objects here";
            return null;
        }
        slot.GlobalPosition = at;
        var toward = facing - at;
        toward.y = 0f;
        if (toward.LengthSquared > 1e-6f)
            slot.GlobalRotation = Math.floatQ.LookRotation(toward.Normalized, Math.float3.Up).Inverse;

        if (kind == GraphKind.Shader)
        {
            var loose = slot.AddSlot(ShaderGraphMaterials.GraphSlotName).AttachComponent<ScriptGraphSpace>();
            loose.Kind.Value = GraphKind.Shader;
            return loose;
        }
        var host = slot.AttachComponent<ScriptHost>();
        var space = slot.AddSlot(ScriptGraphPacking.SpaceSlotName).AttachComponent<ScriptGraphSpace>();
        space.Host.Target = host;
        return space;
    }

    // A group as placing a node leaves it: an object called Nodes holding only its script and the open graph,
    // never packed; for shader nodes, only the graph, which belongs to no material. A graph open anywhere
    // else, on an object someone scripted, on a packed script or on a material, is one of its own.
    public static bool IsImplicit(ScriptGraphSpace? space)
    {
        if (space?.Kind.Value == GraphKind.Shader)
            return IsLooseShaderGroup(space);
        if (space?.Host.Target is not { IsDestroyed: false } host || host.Slot is not { IsDestroyed: false } slot
            || slot.Name.Value != GroupSlotName || host.Graph.Target != null || slot.ChildCount != 1
            || !ReferenceEquals(space.Slot?.Parent, slot))
            return false;
        foreach (var component in slot.Components)
        {
            // The host keeps the clips it is bound to warm on its own object while it runs.
            if (!ReferenceEquals(component, host) && component is not ScriptClipWarmer)
                return false;
        }
        return true;
    }

    private static bool IsLooseShaderGroup(ScriptGraphSpace space)
    {
        if (space.ShaderSource.Target != null || space.Slot?.Parent is not { IsDestroyed: false } slot || slot.IsRootSlot
            || slot.Name.Value != GroupSlotName || slot.ChildCount != 1)
            return false;
        foreach (var _ in slot.Components)
            return false;
        return true;
    }

    // Why two graphs could never become one, or null when they could: two scripts never join, two material
    // graphs never join, and a script and a shader graph never do.
    public static string? JoinRefusal(ScriptGraphSpace? a, ScriptGraphSpace? b)
    {
        if (a == null || b == null || a.IsDestroyed || b.IsDestroyed)
            return "that node is not in a graph";
        if (ReferenceEquals(a, b))
            return null;
        if (a.Kind.Value != b.Kind.Value)
            return TwoKinds;
        return IsImplicit(a) || IsImplicit(b) ? null : a.Kind.Value == GraphKind.Shader ? TwoShaders : TwoScripts;
    }

    // Puts two nodes in one graph when they are not. A script takes a group in; of two groups the one with more
    // nodes takes in the other, the consumer's on a tie. The join is not in the history yet: the caller records
    // it with the edit that needed it, or undoes it when that edit does not happen.
    internal static bool TryJoin(ScriptGraphNode consumer, ScriptGraphNode producer, out ScriptMergeUndo? merge, out string? error)
    {
        merge = null;
        var into = consumer.Space;
        var from = producer.Space;
        error = JoinRefusal(into, from);
        if (error != null)
            return false;
        if (ReferenceEquals(into, from))
            return true;
        if (!IsImplicit(into) || (IsImplicit(from) && Count(from!) <= Count(into!)))
            merge = ScriptMergeUndo.Take(into!, from!, out error);
        else
            merge = ScriptMergeUndo.Take(from!, into!, out error);
        return merge != null;
    }

    private static int Count(ScriptGraphSpace space)
    {
        int count = 0;
        foreach (var _ in space.Nodes)
            count++;
        return count;
    }

    public static ScriptGraphSpace? FindSpace(ScriptHost host)
    {
        var slot = host?.Slot;
        if (slot == null)
            return null;
        foreach (var child in slot.Children)
        {
            var space = child.GetComponent<ScriptGraphSpace>();
            if (space != null && !space.IsDestroyed && ReferenceEquals(space.Host.Target, host))
                return space;
        }
        return null;
    }

    // The open space if there is one, else the host's graph unpacked. Null, with the reason, when the
    // graph has not loaded on this machine yet.
    public static ScriptGraphSpace? Unpack(ScriptHost host, out string? error)
    {
        error = null;
        if (host == null || host.IsDestroyed || host.World == null)
        {
            error = "there is no script here";
            return null;
        }
        if (FindSpace(host) is { } open)
        {
            Reveal(open, UnpackStep);
            return open;
        }

        ScriptGraph graph;
        if (host.Graph.Target == null)
        {
            graph = new ScriptGraph();
        }
        else if (host.Graph.Target is IUrlAssetProvider { IsLoadPending: true })
        {
            // Mid-switch, the old graph would still read as loaded, and editing it would lose the new one.
            error = "its graph is still loading here";
            return null;
        }
        else if (host.Graph.IsAssetAvailable && host.Graph.Asset?.Graph is { } loaded)
        {
            graph = loaded;
        }
        else
        {
            error = (host.Graph.Target as IUrlAssetProvider)?.LoadFailure ?? host.Graph.Asset?.LoadError ?? "its graph has not loaded here yet";
            return null;
        }

        var world = host.World;
        var space = ScriptGraphPacking.Unpack(host, graph, world.LocalUser?.Root?.HeadPosition);
        InspectorUndo.Record(world, SlotExistenceUndoBatch.Created(world, new[] { space.Slot }, UnpackStep));
        return space;
    }

    // A graph put away is shown again where it was, and switched on: opening a graph is for working on it.
    internal static void Reveal(ScriptGraphSpace space, LocaleText step)
    {
        var records = new List<IUndoBatch>();
        if (space.Packed.Value)
        {
            space.Packed.Value = false;
            records.Add(new FieldEditUndoBatch(space.Packed, true, false, step));
        }
        if (!space.Slot.ActiveSelf.Value)
        {
            space.Slot.ActiveSelf.Value = true;
            records.Add(new FieldEditUndoBatch(space.Slot.ActiveSelf, false, true, step));
        }
        if (records.Count > 0 && space.World is { } world)
            Record(world, step, records);
    }

    // What a hand grabs a node by: a box the size of its card, sized without problem lines so it is the
    // same on every machine, and a grab that never scales the node. It does not stop anyone walking. A node
    // made into a packed graph starts with both switched off; the host keeps them that way while the graph
    // stays packed (see ScriptGraphSpace).
    public static void FitNodeBody(ScriptGraphNode node)
    {
        if (node == null || node.IsDestroyed)
            return;
        var layout = ScriptCardLayout.For(node, 0);
        float width = layout.Width * ScriptEditorStyle.CardScale;
        float height = layout.Height * ScriptEditorStyle.CardScale;
        // Just behind the card, since the beam takes the nearest hit and the card's ports have to win it.
        var size = new Math.float3(width, height, BodyDepth);
        var offset = new Math.float3(width * 0.5f, -height * 0.5f, -(BodyDepth * 0.5f + BodyGap));
        bool shown = node.Space?.Packed.Value != true;
        var box = node.Slot.GetComponent<BoxCollider>() ?? node.Slot.AttachComponent<BoxCollider>();
        if (box.Size.Value != size)
            box.Size.Value = size;
        if (box.Offset.Value != offset)
            box.Offset.Value = offset;
        if (box.Type.Value != Physics.ColliderType.Trigger)
            box.Type.Value = Physics.ColliderType.Trigger;
        if (box.CharacterCollider.Value)
            box.CharacterCollider.Value = false;
        if (box.Enabled.Value != shown)
            box.Enabled.Value = shown;
        var grab = node.Slot.GetComponent<Grabbable>() ?? node.Slot.AttachComponent<Grabbable>();
        if (grab.Scalable.Value)
            grab.Scalable.Value = false;
        if (grab.Enabled.Value != shown)
            grab.Enabled.Value = shown;
    }

    // Adds a node of a type to a space at a point in the space, as its own undo step.
    public static ScriptGraphNode? AddNode(ScriptGraphSpace space, string type, string? signature, in Math.float3 localPosition,
        out string? error)
    {
        var node = MakeNode(space, type, signature, localPosition, out error);
        if (node != null)
            InspectorUndo.Record(space.World, SlotExistenceUndoBatch.Created(space.World, new[] { node.Slot }, AddNodeStep));
        return node;
    }

    internal static ScriptGraphNode? MakeNode(ScriptGraphSpace space, string type, string? signature, in Math.float3 localPosition,
        out string? error)
    {
        if (!Resolves(space.Language, type, signature, out var definition, out error))
            return null;
        var slot = space.Slot.AddSlot(type);
        slot.LocalPosition.Value = localPosition;
        var node = slot.AttachComponent<ScriptGraphNode>();
        node.NodeType.Value = definition.Name;
        node.Signature.Value = signature ?? string.Empty;
        node.Version.Value = definition.Version;
        slot.Name.Value = node.Title;
        if (space.Kind.Value == GraphKind.Shader)
            NameUniquely(space, node);
        FitNodeBody(node);
        return node;
    }

    // A shader node that makes a material property gets a name no other in its graph has: two never clash,
    // and the property is named after the node rather than its id, so a copied material keeps its textures
    // and values. Image, Image 2, Amount, Amount 2.
    private static void NameUniquely(ScriptGraphSpace space, ScriptGraphNode node)
    {
        if (node.Overload is not { } overload || overload.InputIndex("name") is not (int index and >= 0)
            || overload.Inputs[index].Type != ScriptTypes.String || !string.IsNullOrEmpty(node.FindValue("name")?.Json.Value))
            return;
        string root = overload.Inputs[index].Default as string is { Length: > 0 } given ? given : "Image";
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var other in space.Nodes)
        {
            if (ReferenceEquals(other, node) || other.Overload is not { } otherOverload || otherOverload.InputIndex("name") is not (int at and >= 0))
                continue;
            string? text = otherOverload.Inputs[at].Default as string;
            if (other.FindValue("name")?.Json.Value is { Length: > 0 } json)
            {
                try
                {
                    text = GraphValue.ParseJson(json).String;
                }
                catch (GraphFormatException)
                {
                }
            }
            if (!string.IsNullOrEmpty(text))
                taken.Add(Lumora.Shading.GlslNames.FromText(text, text));
        }
        string name = root;
        for (int i = 2; taken.Contains(Lumora.Shading.GlslNames.FromText(name, name)); i++)
            name = $"{root} {i}";
        node.SetValue("name", GraphValue.Of(name));
    }

    private static bool Resolves(GraphLanguage language, string type, string? signature, out NodeDefinition definition, out string? error)
    {
        error = null;
        var registry = language.Registry;
        if (!registry.TryGetDefinition(type, out definition))
        {
            error = language.Kind == GraphKind.Shader ? $"'{type}' is not a shader node" : $"unknown node type '{type}'";
            return false;
        }
        if (!registry.TryResolve(definition, signature, out _, out var why))
        {
            error = why;
            return false;
        }
        return true;
    }

    // Joins two ports, whichever end a drag began at, as one undo step for whoever joined them. Ports of two
    // groups put the groups together in the same step.
    public static bool Connect(ScriptCardHandle a, ScriptCardHandle b, out string? error)
    {
        error = null;
        if (a.Node is not { IsDestroyed: false } first || b.Node is not { IsDestroyed: false } second)
        {
            error = "that node is gone";
            return false;
        }
        switch (a.Kind, b.Kind)
        {
            case (ScriptCardHandleKind.DataOutput, ScriptCardHandleKind.DataInput):
                return Wire(second, false, b.Port, first, a.Port, out error);
            case (ScriptCardHandleKind.DataInput, ScriptCardHandleKind.DataOutput):
                return Wire(first, false, a.Port, second, b.Port, out error);
            case (ScriptCardHandleKind.FlowOutput, ScriptCardHandleKind.FlowInput):
                return Wire(first, true, a.Port, second, b.Port, out error);
            case (ScriptCardHandleKind.FlowInput, ScriptCardHandleKind.FlowOutput):
                return Wire(second, true, b.Port, first, a.Port, out error);
            case (ScriptCardHandleKind.Variable, _):
                return PointVariable(first, second, out error);
            case (_, ScriptCardHandleKind.Variable):
                return PointVariable(second, first, out error);
        }
        error = "those two ports do not join";
        return false;
    }

    // What a wire being dragged turns into when secondary is pressed: a Constant for an input, holding the
    // value the input had; a Display for an output; a Call for a flow input; a Pulse for a flow output; a
    // Stored Value for a Write Value's variable; a Function with its ports for a Return's or a call's. Made in
    // the dragged node's graph at a point in it, with the port it answers level with the point, wired in, all
    // as one undo step.
    public static ScriptGraphNode? AddHelper(ScriptCardHandle from, Math.float3 localPoint, out string? error)
    {
        error = null;
        if (from.Node is not { IsDestroyed: false } node || node.Space is not { IsDestroyed: false } space
            || node.Overload is not { } overload)
        {
            error = "that node is gone";
            return null;
        }
        if (space.Kind.Value == GraphKind.Shader)
            return AddShaderHelper(from, node, space, overload, localPoint, out error);

        string type;
        string? signature = null;
        bool leftOfPoint = from.Kind is ScriptCardHandleKind.DataInput or ScriptCardHandleKind.FlowInput or ScriptCardHandleKind.Variable;
        switch (from.Kind)
        {
            case ScriptCardHandleKind.DataInput when overload.ConnectableInputIndex(from.Port) is int input and >= 0:
                type = "Constant";
                signature = overload.Inputs[input].Type.Name;
                break;
            case ScriptCardHandleKind.DataOutput when overload.OutputIndex(from.Port) is int output and >= 0:
                type = "Display";
                signature = overload.Outputs[output].Type.Name;
                break;
            case ScriptCardHandleKind.FlowInput:
                type = "Call";
                break;
            case ScriptCardHandleKind.FlowOutput:
                type = "Pulse";
                break;
            case ScriptCardHandleKind.Variable when FunctionSignature.FollowsFunction(overload.Definition.Kind):
                type = "Function";
                signature = overload.Signature;
                break;
            case ScriptCardHandleKind.Variable when overload.Inputs.Count > 0:
                type = "StoredValue";
                signature = overload.Inputs[0].Type.Name;
                break;
            default:
                error = "nothing to make from that port";
                return null;
        }

        // The new node's port sits in its first row, on the side facing the port it answers.
        var place = localPoint;
        place.z = 0f;
        place.y += (ScriptEditorStyle.HeaderHeight + ScriptEditorStyle.Padding + ScriptEditorStyle.RowHeight * 0.5f) * ScriptEditorStyle.CardScale;

        var undo = FindUndo(space.World);
        using var scope = undo != null ? undo.BeginBatch(AddNodeStep) : default;
        var helper = AddNode(space, type, signature, place, out error);
        if (helper == null)
            return null;
        if (leftOfPoint)
            helper.Slot.LocalPosition.Value -= new Math.float3(ScriptCardLayout.For(helper, 0).Width * ScriptEditorStyle.CardScale, 0f, 0f);

        switch (from.Kind)
        {
            case ScriptCardHandleKind.DataInput:
                if (node.FindValue(from.Port) is { } given)
                {
                    if (given.Reference.Target is { IsDestroyed: false } bound and not ScriptGraphNode)
                        helper.SetReference("value", bound);
                    else if (!string.IsNullOrEmpty(given.Json.Value))
                    {
                        try
                        {
                            helper.SetValue("value", GraphValue.ParseJson(given.Json.Value));
                        }
                        catch (GraphFormatException)
                        {
                        }
                    }
                }
                Wire(node, false, from.Port, helper, "result", out error);
                break;
            case ScriptCardHandleKind.DataOutput:
                Wire(helper, false, "value", node, from.Port, out error);
                break;
            case ScriptCardHandleKind.FlowInput:
                Wire(helper, true, "next", node, from.Port, out error);
                break;
            case ScriptCardHandleKind.FlowOutput:
                Wire(node, true, from.Port, helper, "in", out error);
                break;
            default:
                PointVariable(node, helper, out error);
                break;
        }
        return helper;
    }

    // In a shader graph a wire off an input makes the constant node for its type, holding the value the input
    // had. An output has nothing to make: every card's preview already shows what its node makes.
    private static ScriptGraphNode? AddShaderHelper(ScriptCardHandle from, ScriptGraphNode node, ScriptGraphSpace space, NodeOverload overload,
        Math.float3 localPoint, out string? error)
    {
        error = null;
        if (from.Kind != ScriptCardHandleKind.DataInput || overload.ConnectableInputIndex(from.Port) is not (int input and >= 0)
            || ShaderGraphLanguage.ConstantFor(overload.Inputs[input].Type) is not { } constant)
        {
            error = from.Kind == ScriptCardHandleKind.DataOutput ? "the card's preview already shows that" : "nothing to make from that port";
            return null;
        }

        var place = localPoint;
        place.z = 0f;
        place.y += (ScriptEditorStyle.HeaderHeight + ScriptEditorStyle.Padding + ScriptEditorStyle.RowHeight * 0.5f) * ScriptEditorStyle.CardScale;

        var undo = FindUndo(space.World);
        using var scope = undo != null ? undo.BeginBatch(AddNodeStep) : default;
        var helper = AddNode(space, constant.Type, constant.Signature, place, out error);
        if (helper == null)
            return null;
        helper.Slot.LocalPosition.Value -= new Math.float3(ScriptCardLayout.For(helper, 0).Width * ScriptEditorStyle.CardScale, 0f, 0f);
        if (node.FindValue(from.Port) is { } given && !string.IsNullOrEmpty(given.Json.Value))
        {
            try
            {
                helper.SetValue(constant.ValuePort, GraphValue.ParseJson(given.Json.Value));
            }
            catch (GraphFormatException)
            {
            }
        }
        Wire(node, false, from.Port, helper, constant.OutputPort, out error);
        return helper;
    }

    // Every wire a swipe crossed, in the graph it began on and in any other it went over, let go of as one
    // undo step. A wire whoever cut may not change stays, and the reason says how many did.
    public static int CutWires(ScriptGraphSpace space, IReadOnlyList<ScriptWireKey> wires, out string? error)
    {
        error = null;
        var world = space?.World;
        if (space == null || world == null || space.IsDestroyed || wires.Count == 0)
            return 0;
        var undo = FindUndo(world);
        using var scope = undo != null ? undo.BeginBatch(CutStep) : default;
        int cut = 0, refused = 0;
        foreach (var wire in wires)
        {
            var node = wire.Owner;
            if (node.IsDestroyed || node.Space == null || !ReferenceEquals(node.World, world))
                continue;
            var existing = wire.Exit ? node.FindExit(wire.Port) : node.FindInput(wire.Port);
            if (existing?.Node.Target is not { } before)
                continue;
            string beforePort = existing.NodePort.Value ?? string.Empty;
            try
            {
                if (wire.Exit)
                    node.DisconnectExit(wire.Port);
                else
                    node.DisconnectInput(wire.Port);
            }
            catch (UnauthorizedAccessException)
            {
                refused++;
                continue;
            }
            InspectorUndo.Record(node, new ScriptWireUndo(node, wire.Exit, wire.Port, before, beforePort, null, string.Empty));
            cut++;
        }
        if (refused > 0)
            error = refused == 1 ? "one wire is not yours to cut" : $"{refused} wires are not yours to cut";
        return cut;
    }

    // Ports a dragged wire can join, including a receiving node that can safely select another overload.
    // Its own graph's ports come first, followed by other graphs it could join.
    public static List<(ScriptGraphNode Node, ScriptCardHandleKind Kind, string Port)> FittingPorts(ScriptCardHandle from)
    {
        var fitting = new List<(ScriptGraphNode, ScriptCardHandleKind, string)>();
        if (from.Node is not { IsDestroyed: false } source || source.Space is not { } space || source.Overload is not { } overload)
            return fitting;
        bool validPort = from.Kind switch
        {
            ScriptCardHandleKind.DataOutput => overload.OutputIndex(from.Port) >= 0,
            ScriptCardHandleKind.DataInput => overload.ConnectableInputIndex(from.Port) >= 0,
            ScriptCardHandleKind.FlowOutput => overload.FlowOutputs.Contains(from.Port),
            ScriptCardHandleKind.FlowInput => overload.FlowInputs.Contains(from.Port),
            _ => false,
        };
        if (!validPort)
            return fitting;

        AddFitting(space, source, from.Kind, from.Port, fitting);
        var spaces = new List<ScriptGraphSpace>();
        ScriptGraphSpace.GetAll(source.World, spaces);
        foreach (var other in spaces)
        {
            if (!ReferenceEquals(other, space) && other.IsShown && JoinRefusal(space, other) == null)
                AddFitting(other, source, from.Kind, from.Port, fitting);
        }
        return fitting;
    }

    private static void AddFitting(ScriptGraphSpace space, ScriptGraphNode source, ScriptCardHandleKind kind, string sourcePort,
        List<(ScriptGraphNode, ScriptCardHandleKind, string)> fitting)
    {
        foreach (var node in space.Nodes)
        {
            if (ReferenceEquals(node, source) || node.Overload is not { } other)
                continue;
            switch (kind)
            {
                case ScriptCardHandleKind.DataOutput:
                    foreach (var port in other.Inputs)
                    {
                        if (!port.IsSetting && node.WireProblem(false, port.Name, source, sourcePort) == null)
                            fitting.Add((node, ScriptCardHandleKind.DataInput, port.Name));
                    }
                    break;
                case ScriptCardHandleKind.DataInput:
                    foreach (var port in other.Outputs)
                    {
                        if (source.WireProblem(false, sourcePort, node, port.Name) == null)
                            fitting.Add((node, ScriptCardHandleKind.DataOutput, port.Name));
                    }
                    break;
                case ScriptCardHandleKind.FlowOutput:
                    foreach (var port in other.FlowInputs)
                        fitting.Add((node, ScriptCardHandleKind.FlowInput, port));
                    break;
                default:
                    foreach (var port in other.FlowOutputs)
                        fitting.Add((node, ScriptCardHandleKind.FlowOutput, port));
                    break;
            }
        }
    }

    // What could go between two data ports whose types do not match: every conversion node with an
    // overload that takes what the output gives and gives what the input takes. Empty when the two
    // match, are not an output and an input, are in two scripts, or nothing fits.
    public static List<ScriptConversion> ConversionsFor(ScriptCardHandle a, ScriptCardHandle b)
    {
        var found = new List<ScriptConversion>();
        if (!DataEnds(a, b, out var output, out var input) || JoinRefusal(output.Node!.Space, input.Node!.Space) != null)
            return found;
        // A shader graph's wires convert on their own; there is nothing to put between two of its ports.
        if (output.Node!.Space?.Kind.Value == GraphKind.Shader)
            return found;
        if (output.Node!.Overload is not { } producer || input.Node!.Overload is not { } consumer
            || producer.OutputIndex(output.Port) is not (int from and >= 0) || consumer.ConnectableInputIndex(input.Port) is not (int to and >= 0))
            return found;
        var given = producer.Outputs[from].Type;
        var wanted = consumer.Inputs[to].Type;
        if (Lumora.Script.ScriptTypes.Accepts(wanted, given))
            return found;

        var registry = ScriptEnvironment.Registry;
        foreach (var name in ConversionNodes)
        {
            if (!registry.TryGetDefinition(name, out var definition))
                continue;
            // A generic conversion is closed on either end's type: To Text on what it is given, Parse on
            // what is wanted.
            IReadOnlyList<NodeOverload> overloads;
            if (!definition.IsGeneric)
            {
                overloads = definition.KnownOverloads;
            }
            else if (definition.GenericArity == 1)
            {
                var closed = new List<NodeOverload>();
                if (registry.TryResolve(definition, given.Name, out var onGiven, out _))
                    closed.Add(onGiven);
                if (wanted != given && registry.TryResolve(definition, wanted.Name, out var onWanted, out _))
                    closed.Add(onWanted);
                overloads = closed;
            }
            else
            {
                continue;
            }
            foreach (var overload in overloads)
            {
                // Inputs past the first keep their defaults, such as To Text's indent.
                if (overload.Inputs.Count == 0 || overload.Inputs[0].IsSetting || overload.Inputs.Skip(1).Any(port => !port.HasDefault) || overload.Outputs.Count == 0
                    || !Lumora.Script.ScriptTypes.Accepts(overload.Inputs[0].Type, given)
                    || !Lumora.Script.ScriptTypes.Accepts(wanted, overload.Outputs[0].Type))
                    continue;
                string title = definition.Path[(definition.Path.LastIndexOf('/') + 1)..];
                found.Add(new ScriptConversion(definition.Name, definition.IsGeneric || overloads.Count > 1 ? overload.Signature : null, title,
                    overload.Inputs[0].Name, overload.Outputs[0].Name));
            }
        }
        return found;
    }

    // Wires an output into an input through a conversion node made between them, all one undo step. Ends in
    // two groups put the groups together first, and the conversion goes into the graph they make.
    public static ScriptGraphNode? ConnectThrough(ScriptCardHandle a, ScriptCardHandle b, ScriptConversion conversion, out string? error)
    {
        error = null;
        if (!DataEnds(a, b, out var output, out var input))
        {
            error = "a conversion goes between an output and an input";
            return null;
        }
        return ConnectThrough(output.Node!, output.Port, input.Node!, input.Port, conversion, out error);
    }

    public static ScriptGraphNode? ConnectThrough(ScriptGraphNode producer, string output, ScriptGraphNode consumer, string input,
        ScriptConversion conversion, out string? error)
    {
        error = null;
        if (producer.IsDestroyed || consumer.IsDestroyed || consumer.Space is not { IsDestroyed: false }
            || producer.Space is not { IsDestroyed: false })
        {
            error = "those nodes are not in a graph any more";
            return null;
        }
        if (!Resolves(consumer.Language, conversion.NodeType, conversion.Signature, out _, out error)
            || !TryJoin(consumer, producer, out var merge, out error))
            return null;
        var space = consumer.Space!;

        // Halfway between the two ports, its input level with the midpoint.
        var from = producer.Slot.LocalPosition.Value;
        var to = consumer.Slot.LocalPosition.Value;
        if (space.View is { } view && view.TryPortPoint(producer, ScriptCardHandleKind.DataOutput, output, out var outPoint)
            && view.TryPortPoint(consumer, ScriptCardHandleKind.DataInput, input, out var inPoint))
        {
            from = space.Slot.GlobalPointToLocal(outPoint);
            to = space.Slot.GlobalPointToLocal(inPoint);
        }
        var place = (from + to) * 0.5f;
        place.z = 0f;
        place.y += (ScriptEditorStyle.HeaderHeight + ScriptEditorStyle.Padding + ScriptEditorStyle.RowHeight * 0.5f) * ScriptEditorStyle.CardScale;

        ScriptGraphNode? converter;
        try
        {
            converter = MakeNode(space, conversion.NodeType, conversion.Signature, place, out error);
        }
        catch (UnauthorizedAccessException)
        {
            merge?.Undo();
            throw;
        }
        if (converter == null)
        {
            merge?.Undo();
            return null;
        }

        converter.Slot.LocalPosition.Value -= new Math.float3(ScriptCardLayout.For(converter, 0).Width * ScriptEditorStyle.CardScale * 0.5f, 0f, 0f);

        var records = new List<IUndoBatch>();
        if (merge != null)
            records.Add(merge);
        if (SlotExistenceUndoBatch.Created(space.World, new[] { converter.Slot }, AddNodeStep) is { } made)
            records.Add(made);
        if (TryWire(converter, false, conversion.Input, producer, output, out error) is { } into)
        {
            records.Add(into);
            if (TryWire(consumer, false, input, converter, conversion.Output, out error) is { } onward)
                records.Add(onward);
        }
        Record(space.World!, ConvertStep, records);
        return converter;
    }

    // A relay on a wire at a point on its curve, in the wire's graph: the relay centred on the point, the wire's two
    // ends wired through it, one undo step for whoever put it there. A data wire's relay carries what its output
    // gives and can feed any number of inputs; a flow wire's runs the one node the wire ran. Refused, changing
    // nothing, when the graph is put away or is a shader's, or whoever acts may not change the wire or add to the
    // graph. -xlinka
    public static ScriptGraphNode? InsertRelay(ScriptWireKey wire, Math.float3 localPoint, out string? error)
    {
        var relay = MakeRelay(wire, localPoint, out error, out var wires);
        if (relay != null)
            RecordRelay(relay, wires);
        return relay;
    }

    // The relay made and wired in with its step kept out of the history: RecordRelay writes it once the relay
    // is where it stays, so one carried off as it is made is still one step, and redo brings it back where it
    // was let go.
    internal static ScriptGraphNode? MakeRelay(ScriptWireKey wire, Math.float3 localPoint, out string? error, out List<IUndoBatch> wires)
    {
        error = null;
        wires = new List<IUndoBatch>();
        var owner = wire.Owner;
        if (owner == null || owner.IsDestroyed || owner.Space is not { IsDestroyed: false } space || owner.World is not { } world)
        {
            error = "that wire is gone";
            return null;
        }
        if (!space.IsShown || space.Kind.Value != GraphKind.Script)
        {
            error = "relays go on the wires of an open script";
            return null;
        }
        var entry = wire.Exit ? owner.FindExit(wire.Port) : owner.FindInput(wire.Port);
        if (entry?.Node.Target is not { IsDestroyed: false } other || !ReferenceEquals(other.Space, space))
        {
            error = "that wire is gone";
            return null;
        }
        string otherPort = entry.NodePort.Value ?? string.Empty;
        string? signature = null;
        if (!wire.Exit)
        {
            if (other.Overload is not { } producer || producer.OutputIndex(otherPort) is not (int output and >= 0))
            {
                error = $"{other.Title} has no output '{otherPort}'";
                return null;
            }
            signature = producer.Outputs[output].Type.Name;
        }
        if (!MayWire(owner, wire.Exit, wire.Port))
        {
            error = "you may not change that wire";
            return null;
        }

        ScriptGraphNode? relay;
        try
        {
            relay = MakeNode(space, wire.Exit ? FlowRelayNode : DataRelayNode, signature, localPoint, out error);
        }
        catch (UnauthorizedAccessException)
        {
            error = "you may not add nodes to that graph";
            return null;
        }
        if (relay == null)
            return null;
        var layout = ScriptCardLayout.For(relay, 0);
        relay.Slot.LocalPosition.Value = new Math.float3(localPoint.x - layout.Width * ScriptEditorStyle.CardScale * 0.5f,
            localPoint.y + layout.Height * ScriptEditorStyle.CardScale * 0.5f, 0f);

        // The relay's own wire first, then the one already there moved onto the relay.
        var own = wire.Exit
            ? TryWire(relay, true, "next", other, otherPort, out error)
            : TryWire(relay, false, "value", other, otherPort, out error);
        var moved = own == null ? null
            : wire.Exit
                ? TryWire(owner, true, wire.Port, relay, "in", out error)
                : TryWire(owner, false, wire.Port, relay, "result", out error);
        if (own == null || moved == null)
        {
            if (!relay.Slot.IsDestroyed)
                relay.Slot.Destroy();
            return null;
        }
        wires.Add(own);
        wires.Add(moved);
        return relay;
    }

    internal static void RecordRelay(ScriptGraphNode relay, List<IUndoBatch> wires)
    {
        if (relay.World is not { } world)
            return;
        var records = new List<IUndoBatch>();
        if (!relay.IsDestroyed && SlotExistenceUndoBatch.Created(world, new[] { relay.Slot }, AddNodeStep) is { } made)
            records.Add(made);
        records.AddRange(wires);
        Record(world, RelayStep, records);
    }

    // The output end and the input end of two data ports, in that order.
    private static bool DataEnds(ScriptCardHandle a, ScriptCardHandle b, out ScriptCardHandle output, out ScriptCardHandle input)
    {
        output = a;
        input = b;
        if (a.Node is not { IsDestroyed: false } || b.Node is not { IsDestroyed: false } || ReferenceEquals(a.Node, b.Node))
            return false;
        if (a.Kind == ScriptCardHandleKind.DataOutput && b.Kind == ScriptCardHandleKind.DataInput)
            return true;
        if (a.Kind == ScriptCardHandleKind.DataInput && b.Kind == ScriptCardHandleKind.DataOutput)
        {
            output = b;
            input = a;
            return true;
        }
        return false;
    }

    // A drag off a wired input or exit that lands on nothing lets go of the wire.
    public static bool Unwire(ScriptCardHandle handle)
    {
        if (handle.Node is not { IsDestroyed: false } node || handle.Kind is not (ScriptCardHandleKind.DataInput or ScriptCardHandleKind.FlowOutput))
            return false;
        bool exit = handle.Kind == ScriptCardHandleKind.FlowOutput;
        var existing = exit ? node.FindExit(handle.Port) : node.FindInput(handle.Port);
        if (existing?.Node.Target is not { } before)
            return false;
        string beforePort = existing.NodePort.Value ?? string.Empty;
        try
        {
            if (exit)
                node.DisconnectExit(handle.Port);
            else
                node.DisconnectInput(handle.Port);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        InspectorUndo.Record(node, new ScriptWireUndo(node, exit, handle.Port, before, beforePort, null, string.Empty));
        return true;
    }

    public static bool DeleteNode(ScriptGraphNode node, out string? error)
    {
        error = null;
        var world = node?.World;
        if (node == null || world == null || node.IsDestroyed)
        {
            error = "that node is gone";
            return false;
        }
        try
        {
            // The last node of a group takes the group with it; nothing else of it shows, and undo brings
            // both back.
            var gone = node.Slot;
            if (node.Space is { } space && IsImplicit(space) && space.Nodes.All(n => ReferenceEquals(n, node))
                && space.Slot?.Parent is { IsRootSlot: false } group)
                gone = group;
            var undo = FindUndo(world);
            if (undo != null && SlotExistenceUndoBatch.Destroy(world, new[] { gone }) is { } removed)
                undo.Record(removed);
            else
                gone.Destroy();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            error = "you may not delete a node someone else made";
            return false;
        }
    }

    // The script on an object, unpacked, or a new one on it when it has none.
    public static ScriptGraphSpace? EditScriptOn(Slot target, out string? error)
    {
        error = null;
        if (target == null || target.IsDestroyed || target.IsRootSlot)
        {
            error = "pick an object to script";
            return null;
        }
        var host = target.GetComponent<ScriptHost>();
        if (host == null)
        {
            try
            {
                host = target.AttachComponent<ScriptHost>();
            }
            catch (UnauthorizedAccessException)
            {
                error = "you may not add a script to that";
                return null;
            }
            ComponentUndo.RecordAttach(host, host);
        }
        return Unpack(host, out error);
    }

    // The graphs under a slot, what a slot card held in a hand works on: every script anywhere under it,
    // packed or open, and the material graphs on orbs under it, closed or open. Open means not packed, on
    // or switched off: either way it is there to pack.
    public readonly record struct SlotGraphs(List<ScriptHost> Packed, List<ScriptGraphSpace> Open,
        List<ShaderGraphSource> Parked, List<ShaderGraphSource> Shown)
    {
        public bool AnyPacked => Packed.Count + Parked.Count > 0;

        public bool AnyOpen => Open.Count + Shown.Count > 0;

        public bool Any => AnyPacked || AnyOpen;
    }

    public static SlotGraphs GraphsUnder(Slot root)
    {
        var graphs = new SlotGraphs(new List<ScriptHost>(), new List<ScriptGraphSpace>(), new List<ShaderGraphSource>(),
            new List<ShaderGraphSource>());
        if (root == null || root.IsDestroyed)
            return graphs;
        foreach (var host in root.GetComponentsInChildren<ScriptHost>())
        {
            if (host.IsDestroyed)
                continue;
            if (FindSpace(host) is { Packed.Value: false } space)
                graphs.Open.Add(space);
            else
                graphs.Packed.Add(host);
        }
        foreach (var source in root.GetComponentsInChildren<ShaderGraphSource>())
        {
            if (source.IsDestroyed || source.Graph.Target is not { IsDestroyed: false } graph)
                continue;
            (graph.Packed.Value ? graphs.Parked : graphs.Shown).Add(source);
        }
        return graphs;
    }

    // Every packed script under a slot unpacked, as one undo step, and the material graphs under it opened
    // over their orbs. What opened, and the first refusal.
    public static List<ScriptGraphSpace> UnpackAll(Slot root, out string? error)
    {
        error = null;
        var opened = new List<ScriptGraphSpace>();
        var world = root?.World;
        if (root == null || root.IsDestroyed || world == null)
        {
            error = "that object is gone";
            return opened;
        }
        var graphs = GraphsUnder(root);
        var undo = FindUndo(world);
        var fresh = new List<(ScriptGraphSpace Space, ScriptGraph Graph)>();
        using (undo != null ? undo.BeginBatch(UnpackStep) : default)
        {
            foreach (var host in graphs.Packed)
            {
                try
                {
                    bool made = FindSpace(host) == null;
                    if (Unpack(host, out var refused) is { } space)
                    {
                        opened.Add(space);
                        if (made && host.Graph.Asset?.Graph is { } document)
                            fresh.Add((space, document));
                    }
                    else
                    {
                        error ??= refused;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    error ??= $"you may not unpack the script on {host.Slot.Name.Value}";
                }
            }
            if (fresh.Count > 1)
                StandSideBySide(root, fresh);
        }
        foreach (var source in graphs.Parked)
        {
            if (ShaderGraphMaterials.SetOpen(source, true) && source.Graph.Target is { } space)
                opened.Add(space);
            else
                error ??= $"you may not open the graph of {source.Slot.Name.Value}";
        }
        return opened;
    }

    private const float WallWidth = 12f;
    private const float WallMaxWidth = 48f;
    private const float WallHeight = 6f;
    private const float WallGap = 0.3f;

    // Each graph unpacks 35 cm above its own script, and the scripts of one avatar sit within a hand of each
    // other, so unpacking a whole flux slot stood thirty graphs inside one another. Graphs unpacked together
    // go up as one wall instead, facing whoever unpacked them: rows a gap apart, tallest first, centred over
    // the object. The rows start WallWidth wide and widen until the wall is no taller than WallHeight, so the
    // top rows stay within reach. A graph opened again keeps the place it was packed at. -xlinka
    private static void StandSideBySide(Slot root, List<(ScriptGraphSpace Space, ScriptGraph Graph)> fresh)
    {
        var boxes = new List<(ScriptGraphSpace Space, ShaderGraphBuilder.Bounds Box)>();
        foreach (var (space, graph) in fresh)
            boxes.Add((space, ScriptGraphLayout.Extent(graph, space.Language)));
        boxes.Sort((a, b) => (b.Box.Top - b.Box.Bottom).CompareTo(a.Box.Top - a.Box.Bottom));
        float limit = WallWidth;
        foreach (var (_, box) in boxes)
            limit = MathF.Max(limit, box.Right - box.Left);

        var rows = new List<(float Width, float Height, int First, int Count)>();
        while (true)
        {
            rows.Clear();
            for (int i = 0; i < boxes.Count; i++)
            {
                float width = boxes[i].Box.Right - boxes[i].Box.Left;
                float height = boxes[i].Box.Top - boxes[i].Box.Bottom;
                if (rows.Count == 0 || rows[^1].Width + WallGap + width > limit)
                {
                    rows.Add((width, height, i, 1));
                    continue;
                }
                var row = rows[^1];
                rows[^1] = (row.Width + WallGap + width, MathF.Max(row.Height, height), row.First, row.Count + 1);
            }
            float tall = -WallGap;
            foreach (var row in rows)
                tall += row.Height + WallGap;
            if (tall <= WallHeight || limit >= WallMaxWidth)
                break;
            limit += 2f;
        }

        var at = root.GlobalPosition + new Math.float3(0f, 0.35f, 0f);
        var facing = Math.floatQ.Identity;
        if (root.World?.LocalUser?.Root?.HeadPosition is Math.float3 head)
        {
            var toward = head - at;
            toward.y = 0f;
            if (toward.LengthSquared > 1e-6f)
                facing = Math.floatQ.LookRotation(toward.Normalized, Math.float3.Up).Inverse;
        }
        var across = facing * Math.float3.Right;
        float y = 0f;
        foreach (var (rowWidth, rowHeight, first, count) in rows)
        {
            float x = -rowWidth * 0.5f;
            for (int i = first; i < first + count; i++)
            {
                var (space, box) = boxes[i];
                var slot = space.Slot;
                slot.GlobalRotation = facing;
                slot.GlobalPosition = at + across * (x - box.Left) + Math.float3.Up * (y - box.Bottom);
                slot.GlobalScale = Math.float3.One;
                x += box.Right - box.Left + WallGap;
            }
            y += rowHeight + WallGap;
        }
    }

    // Every open script graph under a slot packed back, and the material graphs under it closed onto their
    // orbs. Each pack is its own undo step. The first refusal, or null.
    public static async Task<string?> PackAllAsync(Slot root)
    {
        if (root == null || root.IsDestroyed || root.World == null)
            return "that object is gone";
        var graphs = GraphsUnder(root);
        string? error = null;
        foreach (var source in graphs.Shown)
        {
            if (!ShaderGraphMaterials.SetOpen(source, false))
                error ??= $"you may not close the graph of {source.Slot.Name.Value}";
        }
        // Every pack starts here, on the world's thread, which is where its first half has to run.
        var packs = graphs.Open.Select(space => PackAsync(space)).ToList();
        foreach (var refused in await Task.WhenAll(packs).ConfigureAwait(false))
            error ??= refused;
        return error;
    }

    // Nodes standing on their own packed into an object picked by its card: the object's script takes them
    // in, a new script when it has none and its graph unpacked first when it has one, and is packed. They
    // go beside what the script already holds. Taking them in is one undo step and the pack another.
    public static Task<string?> PackIntoAsync(ScriptGraphSpace group, Slot target)
    {
        var world = group?.World;
        if (group == null || group.IsDestroyed || world == null)
            return Task.FromResult<string?>("there is no graph to pack");
        if (group.Kind.Value == GraphKind.Shader)
            return Task.FromResult<string?>("a shader graph stays with its material; there is nothing to pack");
        if (!IsImplicit(group))
            return Task.FromResult<string?>("that graph is a script of its own already; only nodes standing on their own pack into an object");
        if (target == null || target.IsDestroyed || target.IsRootSlot)
            return Task.FromResult<string?>("pick an object to pack into");
        if (group.Slot?.Parent is { } loose && (ReferenceEquals(target, loose) || target.IsDescendantOf(loose)))
            return Task.FromResult<string?>("those nodes cannot pack into themselves");

        ScriptGraphSpace? into;
        var undo = FindUndo(world);
        using (undo != null ? undo.BeginBatch(PackStep) : default)
        {
            into = EditScriptOn(target, out var error);
            if (into == null)
                return Task.FromResult<string?>(error ?? "that object's script could not be opened");
            var already = new HashSet<ScriptGraphNode>(into.Nodes);
            var merge = ScriptMergeUndo.Take(into, group, out error);
            if (merge == null)
                return Task.FromResult<string?>(error);
            InspectorUndo.Record(world, merge);
            Beside(into, already);
        }
        return PackAsync(into);
    }

    // The nodes that just came into a graph as a block, to the right of the ones it had, tops level; in a
    // graph that had none, centred on its origin. Undoing the join puts them back where they were.
    private static void Beside(ScriptGraphSpace space, HashSet<ScriptGraphNode> already)
    {
        var came = new List<ScriptGraphNode>();
        foreach (var node in space.Nodes)
        {
            if (!already.Contains(node) && node.Slot is { IsDestroyed: false })
                came.Add(node);
        }
        if (came.Count == 0)
            return;
        var (left, right, top, _) = Extent(came);
        Math.float3 move;
        if (already.Count > 0)
        {
            var had = Extent(already);
            move = new Math.float3(had.Right + 0.1f - left, had.Top - top, 0f);
        }
        else
        {
            move = new Math.float3(-(left + right) * 0.5f, -top, 0f);
        }
        foreach (var node in came)
        {
            try
            {
                node.Slot.LocalPosition.Value += move;
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static (float Left, float Right, float Top, float Bottom) Extent(IEnumerable<ScriptGraphNode> nodes)
    {
        float left = float.MaxValue, right = float.MinValue, top = float.MinValue, bottom = float.MaxValue;
        foreach (var node in nodes)
        {
            if (node.Slot is not { IsDestroyed: false } slot)
                continue;
            var at = slot.LocalPosition.Value;
            var layout = ScriptCardLayout.For(node, 0);
            left = MathF.Min(left, at.x);
            right = MathF.Max(right, at.x + layout.Width * ScriptEditorStyle.CardScale);
            top = MathF.Max(top, at.y);
            bottom = MathF.Min(bottom, at.y - layout.Height * ScriptEditorStyle.CardScale);
        }
        return (left, right, top, bottom);
    }

    // A node's input wired to another's output, or its exit to another's flow input, as one undo step for
    // whoever wired it. Nodes of two groups put their groups together first, in the same step.
    public static bool Wire(ScriptGraphNode owner, bool exit, string port, ScriptGraphNode other, string otherPort, out string? error)
    {
        if (owner == null || other == null || owner.IsDestroyed || other.IsDestroyed)
        {
            error = "that node is gone";
            return false;
        }
        error = owner.WireProblem(exit, port, other, otherPort);
        if (error != null)
            return false;
        if (!MayWire(owner, exit, port))
        {
            error = "you may not change that node";
            return false;
        }
        if (!TryJoin(exit ? other : owner, exit ? owner : other, out var merge, out error))
            return false;
        string beforeSignature = owner.Signature.Value ?? string.Empty;
        string? inferredSignature = null;
        if (!exit && owner.Language.Kind == GraphKind.Script && other.Overload is { } producer
            && producer.OutputIndex(otherPort) is int output and >= 0)
            inferredSignature = ScriptGraphOverloads.SelectInput(owner, port, producer.Outputs[output].Type)?.Signature;
        var widened = exit ? null : Widen(owner, port, other, otherPort);
        var wire = TryWire(owner, exit, port, other, otherPort, out error, inferredSignature);
        if (wire == null)
        {
            widened?.Undo();
            merge?.Undo();
            return false;
        }
        wire.CaptureSignatures(beforeSignature, owner.Signature.Value ?? string.Empty);
        if (merge == null)
        {
            InspectorUndo.Record(owner, wire);
            return true;
        }
        var records = new List<IUndoBatch>();
        if (merge != null)
            records.Add(merge);
        records.Add(wire);
        Record(owner.World!, WireStep, records);
        return true;
    }

    // A shader node takes the width of what is wired into it, the way a node editor's math nodes do: a float
    // Multiply given a float3 becomes the float3 Multiply, and its output with it. Only ever wider, never
    // narrower, so one thin wire does not shrink a node the rest of the graph relies on. Null when nothing
    // changed.
    private static FieldEditUndoBatch? Widen(ScriptGraphNode consumer, string input, ScriptGraphNode producer, string output)
    {
        if (consumer.Space?.Kind.Value != GraphKind.Shader || consumer.Overload is not { } current || producer.Overload is not { } from
            || current.Definition.IsGeneric || !ScriptGraphOverloads.CanWrite(consumer.Signature))
            return null;
        int given = from.OutputIndex(output);
        int wanted = current.ConnectableInputIndex(input);
        if (given < 0 || wanted < 0)
            return null;
        var givenType = from.Outputs[given].Type;
        var wantedType = current.Inputs[wanted].Type;
        if (givenType == wantedType || !Lumora.Shading.ShaderTypes.TryFrom(givenType, out var givenShader)
            || !Lumora.Shading.ShaderTypes.TryFrom(wantedType, out var wantedShader))
            return null;
        int givenWidth = Lumora.Shading.ShaderTypes.Width(givenShader);
        int wantedWidth = Lumora.Shading.ShaderTypes.Width(wantedShader);
        bool colorOfSameWidth = givenWidth == wantedWidth && (givenShader == Lumora.Shading.ShaderType.Color) != (wantedShader == Lumora.Shading.ShaderType.Color);
        if (givenWidth < wantedWidth || (givenWidth == wantedWidth && !colorOfSameWidth))
            return null;

        foreach (var candidate in current.Definition.KnownOverloads)
        {
            int index = candidate.ConnectableInputIndex(input);
            if (index < 0 || candidate.Inputs[index].Type != givenType || candidate.Folds != null)
                continue;
            string before = consumer.Signature.Value ?? string.Empty;
            try
            {
                consumer.Signature.Value = candidate.Signature;
                FitNodeBody(consumer);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            return consumer.Signature.Value == candidate.Signature
                ? new FieldEditUndoBatch(consumer.Signature, before, candidate.Signature, ValueStep)
                : null;
        }
        return null;
    }

    // The wire itself and the step that takes it back, or null with the reason.
    private static ScriptWireUndo? TryWire(ScriptGraphNode owner, bool exit, string port, ScriptGraphNode other, string otherPort,
        out string? error, string? signature = null)
    {
        var existing = exit ? owner.FindExit(port) : owner.FindInput(port);
        var beforeNode = existing?.Node.Target;
        string beforePort = existing?.NodePort.Value ?? string.Empty;
        string beforeSignature = owner.Signature.Value ?? string.Empty;
        bool connected = false;
        try
        {
            if (signature != null && owner.Overload?.Signature != signature)
            {
                owner.Signature.Value = signature;
                if (owner.Signature.Value != signature)
                {
                    error = "you may not change that node's type";
                    return null;
                }
                FitNodeBody(owner);
            }
            if (!(exit ? owner.ConnectExit(port, other, otherPort, out error) : owner.ConnectInput(port, other, otherPort, out error)))
                return null;
            var wire = exit ? owner.FindExit(port) : owner.FindInput(port);
            connected = ReferenceEquals(wire?.Node.Target, other) && wire?.NodePort.Value == otherPort;
            if (!connected)
            {
                error = "you may not change that node";
                return null;
            }
            return new ScriptWireUndo(owner, exit, port, beforeNode, beforePort, other, otherPort);
        }
        catch (UnauthorizedAccessException)
        {
            error = "you may not change that node";
            return null;
        }
        finally
        {
            if (!connected && owner.Signature.Value != beforeSignature)
            {
                owner.Signature.Value = beforeSignature;
                FitNodeBody(owner);
            }
        }
    }

    // Whether whoever acts may write the wire a node keeps on a port: the entry there, or a new one.
    private static bool MayWire(ScriptGraphNode owner, bool exit, string port)
    {
        var entry = exit ? owner.FindExit(port) : owner.FindInput(port);
        if (entry != null)
            return ScriptGraphOverloads.CanWrite(entry.Node) && ScriptGraphOverloads.CanWrite(entry.NodePort);
        return ScriptGraphOverloads.CanWrite(exit ? owner.Exits : owner.Inputs);
    }

    private static bool PointVariable(ScriptGraphNode writer, ScriptGraphNode variable, out string? error)
    {
        error = null;
        var writes = writer.Overload;
        var holds = variable.Overload;
        if (writes != null && FunctionSignature.FollowsFunction(writes.Definition.Kind))
            return PointFunction(writer, variable, out error);
        if (writes?.Definition.Kind != NodeKind.WriteVariable || holds?.Definition.Kind != NodeKind.StoredVariable)
        {
            error = "a Write Value points at a Stored Value";
            return false;
        }
        if (writes.Inputs[0].Type != holds.Outputs[0].Type)
        {
            error = $"it writes {writes.Inputs[0].Type.Name}, that holds {holds.Outputs[0].Type.Name}";
            return false;
        }
        var entry = writer.FindValue("var");
        if (!ReferenceEquals(writer.Space, variable.Space)
            && !(entry != null ? entry.Reference.IsWriteAllowed() && entry.Json.IsWriteAllowed() : writer.Values.IsWriteAllowed()))
        {
            error = "you may not change that node";
            return false;
        }
        if (!TryJoin(writer, variable, out var merge, out error))
            return false;
        var before = entry?.Reference.Value;
        try
        {
            writer.SetReference("var", variable);
        }
        catch (UnauthorizedAccessException)
        {
            merge?.Undo();
            error = "you may not change that node";
            return false;
        }
        if (writer.FindValue("var") is not { } set || !ReferenceEquals(set.Reference.Target, variable))
        {
            merge?.Undo();
            error = "you may not change that node";
            return false;
        }
        var edit = new FieldEditUndoBatch(set.Reference, before ?? default(RefID), set.Reference.Value, ValueStep);
        if (merge == null)
            InspectorUndo.Record(writer, edit);
        else
            Record(writer.World!, ValueStep, new List<IUndoBatch> { merge, edit });
        return true;
    }

    // One gesture's records as one undo step. With no history to keep them in, a group a join parked goes at
    // once.
    private static void Record(World world, LocaleText step, List<IUndoBatch> records)
    {
        if (FindUndo(world) is { } undo)
        {
            undo.Record(CompositeUndoBatch.Combine(step, records));
            return;
        }
        foreach (var record in records)
        {
            if (record is ScriptMergeUndo merge)
                merge.Forget();
        }
    }

    private static readonly string[] CommonTypes =
    {
        "bool", "int", "float", "double", "string", "float3", "floatQ", "color", "slot", "user", "component",
        "long", "float2", "float4", "colorHDR", "int2", "int3", "int4", "char", "datetime", "timespan",
        "jsonobject", "jsonarray", "jsontoken",
    };

    // What a node can switch between: a node's own overloads, or the usual types for a generic node
    // with one type argument. A function node's ports are typed, never stepped through.
    public static IReadOnlyList<string> SignatureChoices(ScriptGraphNode node)
    {
        var registry = node.Language.Registry;
        var choices = new List<string>();
        if (!registry.TryGetDefinition(node.NodeType.Value ?? string.Empty, out var definition) || FunctionSignature.IsFunctionNode(definition.Kind)
            || definition.IsScriptCall)
            return choices;
        if (!definition.IsGeneric)
        {
            // A variadic node keeps its input count as it steps between types.
            int count = definition.IsVariadic ? InputCount(node) : 2;
            foreach (var overload in definition.KnownOverloads)
            {
                if (overload.Folds != null)
                    continue;
                if (count <= 2)
                    choices.Add(overload.Signature);
                else if (Foldable(overload))
                    choices.Add(string.Join(",", Enumerable.Repeat(overload.Inputs[0].Type.Name, count)));
            }
            return choices;
        }
        if (definition.GenericArity != 1)
            return choices;
        foreach (var type in CommonTypes)
        {
            if (registry.TryResolve(definition, type, out _, out _))
                choices.Add(type);
        }
        return choices;
    }

    // What a node starts as when picked from the browser: its only overload, a float one when it has
    // several, float for a generic one. The card's arrows step through the rest.
    public static string? DefaultSignature(NodeDefinition definition, GraphLanguage? language = null)
    {
        var registry = (language ?? GraphLanguage.Script).Registry;
        if (definition.IsGeneric)
        {
            if (definition.GenericArity != 1)
                return null;
            foreach (var type in CommonTypes)
            {
                if (type == "float" && registry.TryResolve(definition, type, out _, out _))
                    return type;
            }
            foreach (var type in CommonTypes)
            {
                if (registry.TryResolve(definition, type, out _, out _))
                    return type;
            }
            return null;
        }
        var overloads = definition.KnownOverloads;
        if (overloads.Count <= 1)
            return null;
        foreach (var overload in overloads)
        {
            if (overload.Signature.StartsWith("float", StringComparison.Ordinal))
                return overload.Signature;
        }
        return overloads[0].Signature;
    }

    // How many inputs a variadic node has now, from its signature.
    public static int InputCount(ScriptGraphNode node)
        => ScriptTypes.SplitTopLevel(node.Signature.Value ?? string.Empty) is { Length: > 1 } names ? names.Length : 2;

    private static bool Foldable(NodeOverload overload)
        => overload.Inputs.Count == 2 && overload.Inputs[0].Type == overload.Inputs[1].Type && overload.Outputs.Count == 1
           && overload.Outputs[0].Type == overload.Inputs[0].Type;

    // One more or one fewer input on a variadic node, between two and its limit. Wires to an input that
    // goes stay, and the check marks them, the same as stepping to an overload they do not fit.
    public static bool ChangeInputCount(ScriptGraphNode node, int delta)
    {
        if (node == null || node.IsDestroyed || node.Overload is not { } overload || !overload.Definition.IsVariadic)
            return false;
        var type = overload.Inputs[0].Type.Name;
        int count = System.Math.Clamp(overload.Inputs.Count + delta, 2, overload.Definition.MaxInputs);
        if (count == overload.Inputs.Count)
            return false;
        var binary = overload.Folds ?? overload;
        if (!Foldable(binary))
            return false;
        string current = node.Signature.Value ?? string.Empty;
        string next = string.Join(",", Enumerable.Repeat(type, count));
        try
        {
            node.Signature.Value = next;
            FitNodeBody(node);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        InspectorUndo.Record(node, new FieldEditUndoBatch(node.Signature, current, next, ValueStep));
        return true;
    }

    // A generic node's types typed out, such as "list<int>" or "dict<string,float>", or a Function's ports, such
    // as "a:int,b:float|sum:float", which its Returns and calls take with it. Refused, with the reason, when the
    // node cannot take them.
    public static bool TrySetSignature(ScriptGraphNode node, string text, out string? error)
    {
        error = null;
        if (node == null || node.IsDestroyed)
            return false;
        var registry = node.Language.Registry;
        if (!registry.TryGetDefinition(node.NodeType.Value ?? string.Empty, out var definition))
        {
            error = "unknown node";
            return false;
        }
        if (FunctionSignature.FollowsFunction(definition.Kind))
        {
            error = $"a {node.Title} takes its Function's ports; change the Function's instead";
            return false;
        }
        text = (text ?? string.Empty).Trim();
        if (!registry.TryResolve(definition, text, out var overload, out error))
            return false;
        if (definition.Kind == NodeKind.Function || definition.IsScriptCall)
            return SetFunctionPorts(node, overload.Signature, out error);
        string current = node.Signature.Value ?? string.Empty;
        if (overload.Signature == current)
            return true;
        try
        {
            node.Signature.Value = overload.Signature;
            FitNodeBody(node);
        }
        catch (UnauthorizedAccessException)
        {
            error = "you may not change that node";
            return false;
        }
        InspectorUndo.Record(node, new FieldEditUndoBatch(node.Signature, current, overload.Signature, ValueStep));
        return true;
    }

    // Moves a node to its next or previous overload. Wires that no longer fit stay, and the check marks
    // them, so nothing is lost by stepping past the one wanted.
    public static bool CycleOverload(ScriptGraphNode node, int step)
    {
        if (node == null || node.IsDestroyed)
            return false;
        var choices = SignatureChoices(node);
        if (choices.Count < 2)
            return false;
        string current = node.Signature.Value ?? string.Empty;
        int index = -1;
        for (int i = 0; i < choices.Count; i++)
        {
            if (choices[i] == current)
                index = i;
        }
        int next = index < 0 ? 0 : ((index + step) % choices.Count + choices.Count) % choices.Count;
        try
        {
            node.Signature.Value = choices[next];
            FitNodeBody(node);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        InspectorUndo.Record(node, new FieldEditUndoBatch(node.Signature, current, choices[next], ValueStep));
        return true;
    }

    public static Task<string?> PackAsync(ScriptGraphSpace space)
    {
        var world = space?.World;
        if (space == null || world == null || space.IsDestroyed)
            return Task.FromResult<string?>("there is no graph to pack");
        return PackAsync(space, world);
    }

    private static async Task<string?> PackAsync(ScriptGraphSpace space, World world)
    {
        if (space.Kind.Value == GraphKind.Shader)
            return "a shader graph stays with its material; there is nothing to pack";
        var host = space.Host.Target;
        if (host == null || host.IsDestroyed)
            return "the graph has no script to pack into";

        RenameClashingIds(space);
        var build = ScriptGraphPacking.Build(space);
        if (build.Problems.Count > 0)
            return Describe(build.Problems[0].Node, build.Problems[0].Message);

        var compiled = await ScriptEnvironment.CompileAsync(build.Graph).ConfigureAwait(false);
        if (!compiled.Success)
        {
            foreach (var diagnostic in compiled.Errors)
            {
                build.Nodes.TryGetValue(diagnostic.NodeId ?? string.Empty, out var node);
                return await OnWorldThread(world, () => Describe(node, diagnostic.Message)).ConfigureAwait(false);
            }
            return "the graph does not compile";
        }

        Uri? url;
        try
        {
            url = await ScriptGraphAsset.StoreAsync(build.Graph).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return "the graph could not be stored: " + ex.Message;
        }
        if (url == null)
            return "there is no local asset store to keep the graph in";

        return await OnWorldThread(world, () => Apply(space, build, url)).ConfigureAwait(false);
    }

    private static string? Apply(ScriptGraphSpace space, ScriptGraphBuild build, Uri url)
    {
        var world = space.World;
        var host = space.Host.Target;
        if (world == null || space.IsDestroyed || host == null || host.IsDestroyed)
            return "the graph or its script went away while packing";

        var undo = FindUndo(world);
        using var scope = undo != null ? undo.BeginBatch(PackStep) : default;

        // Asked before the graph is stored on the host, after which it is a group no longer.
        bool group = IsImplicit(space);
        var packer = world.LocalUser;
        byte owner = host.ReferenceID.GetUserByte();
        bool worldContent = owner == RefIDConstants.AUTHORITY_BYTE;
        var target = host;
        if (!worldContent && (packer == null || !packer.HoldsByte(owner)) && host.Slot.MayRemoveComponent(host))
            target = Remint(host);

        var provider = target.Graph.Target as ScriptGraphProvider;
        if (provider == null || provider.IsDestroyed || !ReferenceEquals(provider.Slot, target.Slot)
            || provider.ReferenceID.GetUserByte() != target.ReferenceID.GetUserByte())
        {
            provider = target.Slot.AttachComponent<ScriptGraphProvider>();
            ComponentUndo.RecordAttach(target, provider);
        }

        var beforeUrl = provider.URL.Value;
        provider.URL.Value = url;
        InspectorUndo.Record(world, new FieldEditUndoBatch(provider.URL, beforeUrl, url, PackStep));

        var beforeGraph = target.Graph.Value;
        target.Graph.Target = provider;
        if (!beforeGraph.Equals(target.Graph.Value))
            InspectorUndo.Record(world, new FieldEditUndoBatch(target.Graph, beforeGraph, target.Graph.Value, PackStep));

        var beforeBindings = ScriptBindingsUndo.Snapshot(target.Bindings);
        var afterBindings = new IWorldElement?[build.Bindings.Count];
        for (int i = 0; i < afterBindings.Length; i++)
            afterBindings[i] = build.Bindings[i];
        bool rebuild = world.IsAuthority || packer?.HoldsByte(target.ReferenceID.GetUserByte()) == true;
        ScriptBindingsUndo.Assign(target.Bindings, afterBindings, rebuild);
        InspectorUndo.Record(world, new ScriptBindingsUndo(target.Bindings, beforeBindings, afterBindings, PackStep, rebuild));

        // A packed group is a Script from now on.
        var named = target.Slot.Name;
        if (group && named.Value != ScriptSlotName)
        {
            var beforeName = named.Value;
            named.Value = ScriptSlotName;
            if (named.Value == ScriptSlotName)
                InspectorUndo.Record(world, new FieldEditUndoBatch(named, beforeName, ScriptSlotName, PackStep));
        }

        RemoveSpace(world, undo, space, target);
        return null;
    }

    // Parked for the undo step, or destroyed. Someone who may not delete another's node slots packs them in
    // place instead, pointed at the script they now pack into, so the next unpack finds and shows them.
    private static void RemoveSpace(World world, UndoManager? undo, ScriptGraphSpace space, ScriptHost host)
    {
        try
        {
            if (undo != null && SlotExistenceUndoBatch.Destroy(world, new[] { space.Slot }) is { } removed)
                undo.Record(removed);
            else
                space.Slot.Destroy();
        }
        catch (UnauthorizedAccessException)
        {
            if (space.Slot.IsDestroyed)
                return;
            try
            {
                var beforeHost = space.Host.Value;
                if (!ReferenceEquals(space.Host.Target, host))
                {
                    space.Host.Target = host;
                    InspectorUndo.Record(world, new FieldEditUndoBatch(space.Host, beforeHost, space.Host.Value, PackStep));
                }
                if (!space.Packed.Value)
                {
                    space.Packed.Value = true;
                    InspectorUndo.Record(world, new FieldEditUndoBatch(space.Packed, false, true, PackStep));
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    // The packer's own host in place of someone else's: same slot, same settings, new author.
    private static ScriptHost Remint(ScriptHost old)
    {
        var fresh = old.Slot.AttachComponent<ScriptHost>();
        fresh.Mode.Value = old.Mode.Value;
        fresh.Enabled.Value = old.Enabled.Value;
        ComponentUndo.RecordAttach(fresh, fresh);
        ComponentUndo.RecordDestroy(old, old);
        return fresh;
    }

    // Only on the packer's machine, right before it packs: every other peer only reads the ids.
    private static void RenameClashingIds(ScriptGraphSpace space)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in space.Nodes)
        {
            string id = node.NodeId.Value ?? string.Empty;
            if (GraphNode.IsValidId(id) && taken.Add(id))
                continue;
            string fresh = node.FreshId();
            for (int i = 2; !taken.Add(fresh); i++)
                fresh = node.FreshId() + "_" + i;
            node.NodeId.Value = fresh;
        }
    }

    private static string Describe(ScriptGraphNode? node, string message)
        => node == null ? message : $"{node.Title}: {message}";

    internal static UndoManager? FindUndo(World world)
        => world.LocalUser?.Root?.Slot?.GetComponentInChildren<UndoManager>();

    private const string LocalOnly = "a Local script changes only what it made on this machine";

    // A script's Unpack Graph: the open graph when there is one, else the host's graph written out under it,
    // made the way the script makes any slot. Refused, with the reason, while the graph has not loaded on the
    // host. No undo step: a script that wants one records the space itself.
    internal static ScriptGraphSpace? UnpackFor(ScriptContext ctx, ScriptHost? host)
    {
        if (ctx.OffHost)
            throw new UnauthorizedAccessException(LocalOnly);
        if (!ScriptReads.Reached(ctx, host, "script"))
            return null;
        if (FindSpace(host) is { } open)
        {
            if (open.Packed.Value)
                ScriptWrites.Write(ctx, open.Packed, false);
            if (!open.Slot.ActiveSelf.Value)
                ScriptWrites.Write(ctx, open.Slot.ActiveSelf, true);
            return open;
        }

        ScriptGraph graph;
        if (host.Graph.Target == null)
            graph = new ScriptGraph();
        else if (host.Graph.Target is IUrlAssetProvider { IsLoadPending: true })
            throw new InvalidOperationException("its graph is still loading here");
        else if (host.Graph.IsAssetAvailable && host.Graph.Asset?.Graph is { } loaded)
            graph = loaded;
        else
            throw new InvalidOperationException((host.Graph.Target as IUrlAssetProvider)?.LoadFailure ?? host.Graph.Asset?.LoadError
                ?? "its graph has not loaded here yet");

        var made = ScriptWrites.MakeUnder(ctx, host.Slot, 1 + graph.Nodes.Count, () => ScriptGraphPacking.Unpack(host, graph).Slot);
        return made.GetComponent<ScriptGraphSpace>();
    }

    // A script's Pack Graph: the graph in space as a new script of the packer's own, on a new slot beside the
    // graph's script, made the way the packer makes any slot. Its compile runs on a worker and the graph is
    // stored as the editor stores it; the script appears on the slot once both land, and runs as whoever
    // packed it, the same rule a person's pack follows.
    internal static Slot? PackFor(ScriptContext ctx, ScriptGraphSpace? space)
    {
        if (ctx.OffHost)
            throw new UnauthorizedAccessException(LocalOnly);
        var world = ctx.World;
        if (!ScriptReads.Reached(ctx, space, "graph"))
            return null;
        if (!world.IsAuthority)
        {
            ctx.Fail("graphs are packed on the host");
            return null;
        }
        if (space.Kind.Value == GraphKind.Shader)
            throw new InvalidOperationException("a shader graph stays with its material; there is nothing to pack");
        if (NotPackersOwn(ctx.Principal, space) is { } refused)
            throw new UnauthorizedAccessException(refused);
        if (space.IsChecked && space.HasProblems)
            throw new InvalidOperationException("the graph has problems: " + FirstProblem(space));
        var build = ScriptGraphPacking.Build(space);
        if (build.Problems.Count > 0)
            throw new InvalidOperationException(Describe(build.Problems[0].Node, build.Problems[0].Message));

        var scripts = world.Scripts;
        var source = space.Host.Target is { IsDestroyed: false } owner ? owner : null;
        var anchor = source?.Slot ?? space.Slot;
        string name = IsImplicit(space) || string.IsNullOrEmpty(anchor.Name.Value) ? ScriptSlotName : anchor.Name.Value;
        var made = ScriptWrites.MakeBeside(ctx, anchor, name);
        scripts.Packed(ctx);

        var principal = ctx.Principal;
        var mode = source?.Mode.Value ?? ScriptMode.Shared;
        var bindings = new List<IWorldElement>(build.Bindings);
        var compile = ScriptEnvironment.CompileAsync(build.Graph);
        var store = ScriptGraphAsset.StoreAsync(build.Graph);
        scripts.AwaitPack(Task.WhenAll(compile, store), () => LandPack(world, principal, made, store, bindings, mode));
        return made;
    }

    // Why a script may not pack a graph as its own, or null when it may. A world script packs a graph open on
    // one of the world's own scripts, where every edit already runs as the world. A guest's script packs only
    // its author's own work and the world's, and nothing anyone else wrote, or someone else's graph would run
    // with its author's rights.
    internal static string? NotPackersOwn(ScriptPrincipal principal, ScriptGraphSpace space)
    {
        if (principal.IsWorldContent)
        {
            return space.Host.Target is { IsDestroyed: false } host && host.ReferenceID.GetUserByte() == RefIDConstants.AUTHORITY_BYTE
                && ReferenceEquals(FindSpace(host), space)
                ? null
                : "a world script packs only a graph open on one of the world's own scripts";
        }
        byte author = principal.AllocationByte;
        var user = principal.Author;
        foreach (var node in space.Nodes)
        {
            if (!HeldBy(node, author) || WrittenByOther(node.NodeType, user) || WrittenByOther(node.Signature, user)
                || WrittenByOther(node.Version, user) || WrittenByOther(node.NodeId, user))
                return OnlyOwnWork;
            foreach (var value in node.Values)
            {
                if (!HeldBy(value, author) || WrittenByOther(value.Port, user) || WrittenByOther(value.Json, user)
                    || WrittenByOther(value.Reference, user))
                    return OnlyOwnWork;
            }
            foreach (var wire in node.Inputs)
            {
                if (!HeldBy(wire, author) || WrittenByOther(wire.Port, user) || WrittenByOther(wire.Node, user) || WrittenByOther(wire.NodePort, user))
                    return OnlyOwnWork;
            }
            foreach (var wire in node.Exits)
            {
                if (!HeldBy(wire, author) || WrittenByOther(wire.Port, user) || WrittenByOther(wire.Node, user) || WrittenByOther(wire.NodePort, user))
                    return OnlyOwnWork;
            }
        }
        return null;
    }

    private const string OnlyOwnWork = "a guest's script packs only its author's own work and the world's";

    private static bool HeldBy(IWorldElement element, byte author)
    {
        byte owner = element.ReferenceID.GetUserByte();
        return owner == author || owner == RefIDConstants.AUTHORITY_BYTE;
    }

    private static bool WrittenByOther(Networking.Sync.ConflictingSyncElement member, User? author)
        => member.LastModifyingUser is { } writer && !ReferenceEquals(writer, author);

    private static string FirstProblem(ScriptGraphSpace space)
    {
        if (space.GraphProblems.Count > 0)
            return space.GraphProblems[0];
        foreach (var node in space.Nodes)
        {
            var problems = space.ProblemsOf(node);
            if (problems.Count > 0)
                return Describe(node, problems[0]);
        }
        return "it does not compile";
    }

    // Once the compile and the store have landed, on the host: the script onto the slot, as the packer, in the
    // packer's range. A graph that could not be stored takes its slot with it.
    private static void LandPack(World world, ScriptPrincipal principal, Slot slot, Task<Uri?> store, List<IWorldElement> bindings,
        ScriptMode mode)
    {
        if (slot.IsDestroyed || world.IsDestroyed || !principal.IsStillAuthorIn(world))
            return;
        var url = store.IsCompletedSuccessfully ? store.Result : null;
        using (world.DataModelPermissions?.EnterActor(principal))
        {
            if (url == null)
            {
                LumoraLogger.Warn($"Loom [{principal}]: a packed graph could not be stored, so nothing was packed");
                slot.Destroy();
                return;
            }
            using (ScriptWrites.RangeOf(world, principal))
            {
                var provider = slot.AttachComponent<ScriptGraphProvider>();
                provider.URL.Value = url;
                var host = slot.AttachComponent<ScriptHost>();
                if (host.Mode.Value != mode)
                    host.Mode.Value = mode;
                host.Graph.Target = provider;
                foreach (var binding in bindings)
                    host.Bindings.Add(binding.IsDestroyed ? null! : binding);
                if (!principal.IsWorldContent)
                {
                    var decides = new List<IField> { host.Graph, provider.URL };
                    for (int i = 0; i < host.Bindings.Count; i++)
                        decides.Add(host.Bindings.GetElement(i));
                    world.Scripts.RememberSetUp(host, decides);
                }
            }
        }
    }

    private static Task<string?> OnWorldThread(World world, Func<string?> action)
    {
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        world.RunSynchronously(() =>
        {
            try
            {
                done.SetResult(action());
            }
            catch (Exception ex)
            {
                LumoraLogger.Warn($"Loom: pack failed: {ex}");
                done.SetResult("packing failed: " + ex.Message);
            }
        });
        return done.Task;
    }
}
