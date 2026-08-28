// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Input.Actions;

// Which component of a value a binding reads from, or writes into.
public enum BindingAxis
{
    // The whole value: a 2D control feeds both components, a scalar feeds the scalar.
    Auto = 0,
    X,
    Y
}

// One route from a physical control into an action.
//
// An action holds a LIST of these and sums them, which is what makes composites free: W/A/S/D are
// four bindings that each push +-1 into one component of the same 2D action, and a thumbstick is a
// fifth binding that writes the whole vector. No special-case "composite binding" type, no code
// path that knows a keyboard has to fake an axis. -xlinka
public sealed class InputBinding
{
    public ControlRef Control { get; }

    public BindingAxis Source { get; }

    public BindingAxis Target { get; }

    // Multiplier applied after reading. Negative inverts; digitals contribute exactly this.
    public float Scale { get; }

    // Must all be held for this binding to contribute anything.
    public IReadOnlyList<ControlRef> Modifiers { get; }

    // Set by the default map; user overrides clear it. Purely informational, but the controls screen
    // needs it to say whether a row is stock or has been changed.
    public bool IsDefault { get; internal set; }

    public InputBinding(
        ControlRef control,
        BindingAxis source = BindingAxis.Auto,
        BindingAxis target = BindingAxis.Auto,
        float scale = 1f,
        IReadOnlyList<ControlRef>? modifiers = null)
    {
        Control = control;
        Source = source;
        Target = target;
        Scale = scale;
        Modifiers = modifiers ?? Array.Empty<ControlRef>();
    }

    public InputBinding WithDefault(bool isDefault)
    {
        IsDefault = isDefault;
        return this;
    }

    public bool ModifiersSatisfied(IInputSource? sourceForControl, Func<ControlRef, IInputSource?> resolve)
    {
        for (int i = 0; i < Modifiers.Count; i++)
        {
            var modifier = Modifiers[i];
            var device = resolve(modifier);
            if (device == null || !device.ReadDigital(modifier))
                return false;
        }
        return true;
    }

    public string Describe()
    {
        if (Modifiers.Count == 0)
            return Control.Describe();

        var parts = new string[Modifiers.Count + 1];
        for (int i = 0; i < Modifiers.Count; i++)
            parts[i] = Modifiers[i].Describe();
        parts[Modifiers.Count] = Control.Describe();
        return string.Join(" + ", parts);
    }

    public override string ToString() => Describe();
}
