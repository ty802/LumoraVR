// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;

namespace Lumora.Core.Input.Actions;

// A named group of actions that turn on and off together, and can shut out lower-priority groups
// while it is live.
//
// This is what replaces the scatter of "is the dash open / is something suppressing input" checks
// that used to sit inside every module. The dash opening raises one set; every set below it stops
// evaluating, and the modules underneath simply read zero without knowing why.
//
// Interaction deliberately sits ABOVE the menu in priority. The dashboard is pointed at with the
// same laser the world is, so blocking interaction while the menu is up would make the menu
// unclickable. -xlinka
public sealed class InputActionSet
{
    // First half of every action path and persistence key.
    public string Name { get; }

    public string Label { get; }

    // Higher evaluates first and can block everything below it.
    public int Priority { get; }

    public Chirality Side { get; }

    // When true, this set being live gates off every set with a lower priority.
    public bool BlocksLower { get; }

    // Static enable, owned by whoever configured the map.
    public bool Enabled { get; set; } = true;

    // Per-frame gate, recomputed by the evaluator before every pass.
    public bool GateOpen { get; internal set; } = true;

    // Raised by a set owner while its feature is on screen; drives BlocksLower.
    public bool Asserting { get; set; }

    public bool IsLive => Enabled && GateOpen;

    private readonly List<InputAction> _actions = new();
    public IReadOnlyList<InputAction> Actions => _actions;

    public InputActionSet(string name, string label, int priority, Chirality side, bool blocksLower)
    {
        Name = name;
        Label = label;
        Priority = priority;
        Side = side;
        BlocksLower = blocksLower;
    }

    public T Add<T>(T action) where T : InputAction
    {
        action.Set = this;
        _actions.Add(action);
        return action;
    }

    public InputAction? Find(string name)
    {
        foreach (var action in _actions)
        {
            if (action.Name == name)
                return action;
        }
        return null;
    }

    // Analog overlaps are legal on purpose (the wheel means several things at once depending on what
    // you are holding), so only digital collisions are reported.
    public List<InputAction> FindConflicts(InputAction action, in ControlRef control)
    {
        var conflicts = new List<InputAction>();
        if (action.Kind != InputActionKind.Digital)
            return conflicts;

        foreach (var other in _actions)
        {
            if (ReferenceEquals(other, action) || other.Kind != InputActionKind.Digital)
                continue;
            foreach (var binding in other.Bindings)
            {
                if (binding.Control == control)
                {
                    conflicts.Add(other);
                    break;
                }
            }
        }
        return conflicts;
    }

    public override string ToString() => $"{Name} (priority {Priority}, live {IsLive})";
}
