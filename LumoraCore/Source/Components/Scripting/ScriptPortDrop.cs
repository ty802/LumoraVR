// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Scripting;
using Lumora.Script;

namespace Lumora.Core.Components.Scripting;

// Binds a world object to a node input: drop a reference card, pulled from an inspector row, onto the
// input's row on a card. Takes the first thing on the card that fits the port: the element itself, what
// it points at, or for a slot, one of its components. Lives on the local card. -xlinka
[ComponentCategory("Hidden")]
public sealed class ScriptPortDrop : Component, IProxyReceiver
{
    public ScriptGraphNode? Node { get; set; }
    public string Port { get; set; } = string.Empty;
    public ScriptType? Type { get; set; }

    public bool TryReceiveProxy(IReadOnlyList<IGrabbable> held, Grabber grabber)
    {
        var node = Node;
        var type = Type;
        if (node == null || node.IsDestroyed || type == null)
            return false;

        for (int i = 0; i < held.Count; i++)
        {
            if (held[i] is not Component component || component.Slot == null)
                continue;
            var proxy = component.Slot.GetComponent<ReferenceProxy>();
            if (proxy?.Target.Target is not { } element || Pick(type, element) is not { } picked)
                continue;

            var entry = node.FindValue(Port);
            var before = entry?.Reference.Value;
            try
            {
                node.SetReference(Port, picked);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            entry = node.FindValue(Port);
            if (entry != null && before is { } previous)
                InspectorUndo.Record(this, new FieldEditUndoBatch(entry.Reference, previous, entry.Reference.Value, ScriptGraphEditing.ValueStep));
            proxy.Consume(held[i], grabber);
            return true;
        }
        return false;
    }

    public static IWorldElement? Pick(ScriptType type, IWorldElement element)
    {
        var wanted = type.ClrType;
        if (wanted.IsInstanceOfType(element))
            return element;
        if (element is ISyncRef reference && reference.Target is { } target && wanted.IsInstanceOfType(target))
            return target;
        if (element is Slot slot)
        {
            foreach (var candidate in slot.Components)
            {
                if (wanted.IsInstanceOfType(candidate))
                    return candidate;
            }
        }
        if (element is Component owner && wanted.IsInstanceOfType(owner.Slot))
            return owner.Slot;
        return null;
    }
}
