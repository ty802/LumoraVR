// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components.Scripting;
using Lumora.Core.Networking.Sync;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Where a node writes, as far as the graph says without running it. A Write Value writes the Stored Value it
// points at. A drive, or any node that changes the world, writes what its first object input names: an object
// bound on the card, or one handed along a wire from a Constant holding it, through relays, or the script's own
// object. What only a run would find, a child looked up by name or a user who pressed, is not shown. -xlinka
public static class ScriptWriteTargets
{
    private const int MaxHops = 16;

    public static bool Writes(ScriptGraphNode? node)
    {
        var definition = node?.Overload?.Definition;
        return definition != null && (definition.Kind is NodeKind.Drive or NodeKind.WriteVariable
            || definition.Capability is Capability.WorldWrite or Capability.WorldStructure);
    }

    public static void Collect(ScriptGraphNode node, List<ScriptWriteTarget> into)
    {
        if (node == null || node.IsDestroyed || node.Overload is not { } overload)
            return;
        var definition = overload.Definition;
        if (definition.Kind == NodeKind.WriteVariable)
        {
            if (node.FindValue("var")?.Reference.Target is ScriptGraphNode { IsDestroyed: false } stored)
                into.Add(new ScriptWriteTarget(node, "var", ScriptCardHandleKind.Variable, stored));
            return;
        }
        if (!Writes(node))
            return;
        foreach (var input in overload.Inputs)
        {
            if (!IsObject(input.Type))
                continue;
            if (Source(node, input.Name, 0) is { } target)
                into.Add(new ScriptWriteTarget(node, input.Name, ScriptCardHandleKind.DataInput, target));
            return;
        }
    }

    // The slot a target stands in, for a line to point at: a slot itself, a component's, a field's owner's.
    public static Slot? SlotOf(IWorldElement? element)
    {
        for (int hops = 0; element != null && hops < MaxHops; hops++)
        {
            switch (element)
            {
                case Slot slot:
                    return slot.IsDestroyed ? null : slot;
                case Component component:
                    return component.Slot is { IsDestroyed: false } owner ? owner : null;
                case SyncElement member:
                    element = member.Parent;
                    break;
                default:
                    return null;
            }
        }
        return null;
    }

    private static bool IsObject(ScriptType type)
        => type.IsHandle && type != ScriptTypes.String && type.Family is null or "field"
           && (typeof(IWorldElement).IsAssignableFrom(type.ClrType) || type.ClrType.IsInterface);

    private static IWorldElement? Source(ScriptGraphNode node, string port, int hops)
    {
        if (hops > MaxHops)
            return null;
        if (node.FindInput(port)?.Node.Target is ScriptGraphNode { IsDestroyed: false } producer)
        {
            var kind = producer.Overload?.Definition.Kind;
            if (kind is NodeKind.Relay or NodeKind.Constant)
                return Source(producer, "value", hops + 1);
            if (producer.NodeType.Value == nameof(SelfSlot))
                return producer.Space?.Host.Target?.Slot;
            return null;
        }
        return node.FindValue(port)?.Reference.Target is { IsDestroyed: false } bound and not ScriptGraphNode ? bound : null;
    }
}
