// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Input.Actions;

public enum InputActionKind
{
    Digital,
    Analog,
    Analog2D
}

// A named thing the game asks for ("Jump", "Primary") with a list of bindings behind it. Call sites
// read the action; nothing above this layer names a key, a button or a hand.
public abstract class InputAction
{
    // Used as the persistence key, so do not rename lightly.
    public string Name { get; }

    public string Label { get; }

    public string Description { get; internal set; } = string.Empty;

    public InputActionSet Set { get; internal set; } = null!;

    // "Locomotion/Jump". Persistence key and conflict identity.
    public string Path => Set == null ? Name : $"{Set.Name}/{Name}";

    // Independent of the set's gate.
    public bool Enabled { get; set; } = true;

    // False for actions whose control only makes sense as-shipped (raw pointer deltas).
    public bool Rebindable { get; internal set; } = true;

    public abstract InputActionKind Kind { get; }

    private readonly List<InputBinding> _bindings = new();
    public IReadOnlyList<InputBinding> Bindings => _bindings;

    // True once a user override replaced the shipped bindings.
    public bool IsOverridden { get; internal set; }

    protected InputAction(string name, string label)
    {
        Name = name;
        Label = label;
    }

    public InputAction Bind(InputBinding binding)
    {
        _bindings.Add(binding);
        return this;
    }

    public InputAction Bind(ControlRef control, BindingAxis source = BindingAxis.Auto,
        BindingAxis target = BindingAxis.Auto, float scale = 1f, params ControlRef[] modifiers)
        => Bind(new InputBinding(control, source, target, scale, modifiers));

    public InputAction Describe(string description)
    {
        Description = description;
        return this;
    }

    public InputAction NotRebindable()
    {
        Rebindable = false;
        return this;
    }

    public void ClearBindings() => _bindings.Clear();

    public void ClearBindings(InputDeviceKind device)
    {
        for (int i = _bindings.Count - 1; i >= 0; i--)
        {
            if (_bindings[i].Control.Device == device)
                _bindings.RemoveAt(i);
        }
    }

    public bool HasBindingFor(InputDeviceKind device)
    {
        foreach (var binding in _bindings)
        {
            if (binding.Control.Device == device)
                return true;
        }
        return false;
    }

    // Across several device families, joined for a controls-screen cell.
    public string DescribeBindings(params InputDeviceKind[] devices)
    {
        if (devices == null || devices.Length == 0)
            return "-";

        List<string>? parts = null;
        foreach (var device in devices)
        {
            foreach (var binding in _bindings)
            {
                if (binding.Control.Device != device)
                    continue;
                parts ??= new List<string>();
                parts.Add(binding.Describe());
            }
        }
        return parts == null ? "-" : string.Join(", ", parts);
    }

    public bool HasBindingFor(params InputDeviceKind[] devices)
    {
        if (devices == null)
            return false;
        foreach (var device in devices)
        {
            if (HasBindingFor(device))
                return true;
        }
        return false;
    }

    // On one device family, joined for a controls-screen cell.
    public string DescribeBindingsFor(InputDeviceKind device)
    {
        string? single = null;
        List<string>? many = null;
        foreach (var binding in _bindings)
        {
            if (binding.Control.Device != device)
                continue;
            if (single == null)
            {
                single = binding.Describe();
                continue;
            }
            many ??= new List<string> { single };
            many.Add(binding.Describe());
        }
        if (many != null)
            return string.Join(", ", many);
        return single ?? "-";
    }

    internal abstract void Begin();
    internal abstract void Accumulate(InputBinding binding, IInputSource source);
    internal abstract void Finish(float deltaTime);

    // Drive the action to rest while its set is gated off, so held edges release cleanly.
    internal abstract void Release(float deltaTime);
}

// Button-shaped action. Sources OR together, so any bound control can press it.
public sealed class DigitalAction : InputAction
{
    public override InputActionKind Kind => InputActionKind.Digital;

    // Threshold an analog control has to cross to count as pressed.
    public float PressPoint { get; set; } = 0.5f;

    public bool Held { get; private set; }
    public bool Pressed { get; private set; }
    public bool Released { get; private set; }

    // One-shot injection from code (scripted input, a UI button standing in for a control). Consumed
    // by the next evaluation and cleared, so a caller that stops setting it stops driving the action.
    public bool? ExternalInput { get; set; }

    private bool _accumulator;

    public DigitalAction(string name, string label) : base(name, label) { }

    internal override void Begin()
    {
        _accumulator = false;
        if (ExternalInput.HasValue)
        {
            _accumulator = ExternalInput.Value;
            ExternalInput = null;
        }
    }

    internal override void Accumulate(InputBinding binding, IInputSource source)
    {
        if (_accumulator)
            return;

        if (InputControlCatalog.IsVectorControl(binding.Control))
        {
            var vector = source.ReadAnalog2D(binding.Control);
            float component = binding.Source switch
            {
                BindingAxis.X => vector.x,
                BindingAxis.Y => vector.y,
                _ => vector.Length
            };
            _accumulator = MathF.Abs(component * binding.Scale) >= PressPoint;
            return;
        }

        if (source.ReadDigital(binding.Control))
        {
            _accumulator = true;
            return;
        }

        float value = source.ReadAnalog(binding.Control);
        if (value != 0f && MathF.Abs(value * binding.Scale) >= PressPoint)
            _accumulator = true;
    }

    internal override void Finish(float deltaTime) => Apply(_accumulator);

    internal override void Release(float deltaTime)
    {
        ExternalInput = null;
        Apply(false);
    }

    private void Apply(bool held)
    {
        Pressed = !Held && held;
        Released = Held && !held;
        Held = held;
    }

    public static implicit operator bool(DigitalAction action) => action.Held;
    public override string ToString() => $"{Path}: {Held}";
}

// Single-axis action. Bindings sum, then the result is clamped to the strongest single source.
public sealed class AnalogAction : InputAction
{
    public override InputActionKind Kind => InputActionKind.Analog;

    // Magnitude below which the axis reads exactly zero.
    public float Deadzone { get; set; }

    // Response curve exponent applied past the deadzone. 1 is linear.
    public float Curve { get; set; } = 1f;

    // When true the deadzone rescales the remaining range instead of leaving a step at its edge.
    public bool RescaleDeadzone { get; set; } = true;

    public float Value { get; private set; }
    public float Delta { get; private set; }
    public float? ExternalInput { get; set; }

    private float _accumulator;
    private float _peak;

    public AnalogAction(string name, string label) : base(name, label) { }

    internal override void Begin()
    {
        _accumulator = 0f;
        _peak = 0f;
        if (ExternalInput.HasValue)
        {
            Add(ExternalInput.Value);
            ExternalInput = null;
        }
    }

    internal override void Accumulate(InputBinding binding, IInputSource source)
    {
        float raw;
        if (InputControlCatalog.IsVectorControl(binding.Control))
        {
            var vector = source.ReadAnalog2D(binding.Control);
            raw = binding.Source switch
            {
                BindingAxis.X => vector.x,
                BindingAxis.Y => vector.y,
                _ => vector.y
            };
        }
        else
        {
            raw = source.ReadAnalog(binding.Control);
            if (raw == 0f && source.ReadDigital(binding.Control))
                raw = 1f;
        }

        if (raw != 0f)
            Add(raw * binding.Scale);
    }

    private void Add(float value)
    {
        _accumulator += value;
        float magnitude = MathF.Abs(value);
        if (magnitude > _peak)
            _peak = magnitude;
    }

    internal override void Finish(float deltaTime)
    {
        float value = _accumulator;
        // Two bindings pushing the same way must not exceed what one of them could do on its own,
        // or holding W while pushing the stick forward would run at double speed.
        float magnitude = MathF.Abs(value);
        if (magnitude > _peak && magnitude > 0f)
            value *= _peak / magnitude;

        Apply(ApplyShaping(value), deltaTime);
    }

    internal override void Release(float deltaTime)
    {
        ExternalInput = null;
        Apply(0f, deltaTime);
    }

    private float ApplyShaping(float value)
    {
        float magnitude = MathF.Abs(value);
        if (Deadzone > 0f)
        {
            if (magnitude <= Deadzone)
                return 0f;
            if (RescaleDeadzone)
            {
                float span = 1f - Deadzone;
                magnitude = span > 0.0001f ? (magnitude - Deadzone) / span : magnitude;
                if (magnitude > 1f)
                    magnitude = 1f;
            }
        }
        if (Curve != 1f)
            magnitude = MathF.Pow(magnitude, Curve);
        return value < 0f ? -magnitude : magnitude;
    }

    private void Apply(float value, float deltaTime)
    {
        Delta = value - Value;
        Value = value;
    }

    public static implicit operator float(AnalogAction action) => action.Value;
    public override string ToString() => $"{Path}: {Value:F2}";
}

// Two-axis action with a radial deadzone, for sticks and key composites alike.
public sealed class Analog2DAction : InputAction
{
    public override InputActionKind Kind => InputActionKind.Analog2D;

    // Radial deadzone: a stick inside this radius reads exactly zero on both axes.
    public float Deadzone { get; set; }

    // When true, values past magnitude 1 are normalized so diagonals are not faster.
    public bool ClampToUnit { get; set; } = true;

    public bool RescaleDeadzone { get; set; } = true;

    // Zero each axis independently instead of using a radial deadzone. Sticks that drive two
    // unrelated things at once (slide on Y, twist on X) need this: a radial cut lets a hard push on
    // one axis carry the other one's noise along with it.
    public bool PerAxisDeadzone { get; set; }

    public float2 Value { get; private set; }
    public float2 Delta { get; private set; }
    public float2? ExternalInput { get; set; }

    private float2 _accumulator;
    private float _peak;

    public Analog2DAction(string name, string label) : base(name, label) { }

    internal override void Begin()
    {
        _accumulator = float2.Zero;
        _peak = 0f;
        if (ExternalInput.HasValue)
        {
            Add(ExternalInput.Value);
            ExternalInput = null;
        }
    }

    internal override void Accumulate(InputBinding binding, IInputSource source)
    {
        float2 contribution;
        if (InputControlCatalog.IsVectorControl(binding.Control))
        {
            var vector = source.ReadAnalog2D(binding.Control) * binding.Scale;
            if (binding.Source == BindingAxis.Auto && binding.Target == BindingAxis.Auto)
            {
                contribution = vector;
            }
            else
            {
                // Either end named a component, so this is a one-axis route: take the named source
                // axis (or X by default) and land it on the named target axis.
                float picked = binding.Source == BindingAxis.Y ? vector.y : vector.x;
                contribution = binding.Target == BindingAxis.Y
                    ? new float2(0f, picked)
                    : new float2(picked, 0f);
            }
        }
        else
        {
            float raw = source.ReadAnalog(binding.Control);
            if (raw == 0f && source.ReadDigital(binding.Control))
                raw = 1f;
            if (raw == 0f)
                return;
            raw *= binding.Scale;
            // A scalar with no explicit target lands on Y: the scalar sources that feed a 2D action
            // (wheel notches, trigger pulls) all mean "forward/back", never "sideways".
            contribution = binding.Target == BindingAxis.X
                ? new float2(raw, 0f)
                : new float2(0f, raw);
        }

        if (contribution != float2.Zero)
            Add(contribution);
    }

    private void Add(float2 value)
    {
        _accumulator += value;
        float magnitude = value.Length;
        if (magnitude > _peak)
            _peak = magnitude;
    }

    internal override void Finish(float deltaTime)
    {
        var value = _accumulator;
        float magnitude = value.Length;
        if (magnitude > _peak && magnitude > 0f)
            value *= _peak / magnitude;

        Apply(ApplyShaping(value), deltaTime);
    }

    internal override void Release(float deltaTime)
    {
        ExternalInput = null;
        Apply(float2.Zero, deltaTime);
    }

    private float2 ApplyShaping(float2 value)
    {
        if (PerAxisDeadzone)
        {
            float px = MathF.Abs(value.x) <= Deadzone ? 0f : value.x;
            float py = MathF.Abs(value.y) <= Deadzone ? 0f : value.y;
            return new float2(px, py);
        }

        float magnitude = value.Length;
        if (magnitude <= 0.0001f)
            return float2.Zero;

        if (Deadzone > 0f)
        {
            if (magnitude <= Deadzone)
                return float2.Zero;
            if (RescaleDeadzone)
            {
                float span = 1f - Deadzone;
                float scaled = span > 0.0001f ? (magnitude - Deadzone) / span : magnitude;
                if (scaled > 1f)
                    scaled = 1f;
                value = value * (scaled / magnitude);
                magnitude = scaled;
            }
        }

        if (ClampToUnit && magnitude > 1f)
            value = value / magnitude;

        return value;
    }

    private void Apply(float2 value, float deltaTime)
    {
        Delta = value - Value;
        Value = value;
    }

    public float x => Value.x;
    public float y => Value.y;

    public static implicit operator float2(Analog2DAction action) => action.Value;
    public override string ToString() => $"{Path}: {Value}";
}
