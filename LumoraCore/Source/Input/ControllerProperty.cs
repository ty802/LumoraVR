// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Input;

public abstract class ControllerProperty
{
    public IInputDevice Device { get; private set; } = null!;
    public int Index { get; private set; } = -1;
    public string Name { get; private set; } = null!;

    internal void Initialize(IInputDevice device, int index, string name)
    {
        Device = device;
        Index = index;
        Name = name;
    }
}

public class Digital : ControllerProperty
{
    public bool Held { get; private set; }
    public bool Pressed { get; private set; }    // Just pressed this frame
    public bool Released { get; private set; }   // Just released this frame

    public void UpdateState(bool held)
    {
        Pressed = false;
        Released = false;

        if (!Held && held)
            Pressed = true;
        if (Held && !held)
            Released = true;

        Held = held;
    }
}

public class Analog : ControllerProperty
{
    public float Value { get; private set; }
    public float Delta { get; private set; }        // Change this frame
    public float Velocity { get; private set; }     // Delta / deltaTime

    public void UpdateValue(float newValue, float deltaTime)
    {
        Delta = newValue - Value;
        Value = newValue;
        Velocity = deltaTime > 0 ? Delta / deltaTime : 0;
    }
}

public class Analog2D : ControllerProperty
{
    public float2 Value { get; private set; }
    public float2 Delta { get; private set; }
    public float2 Velocity { get; private set; }

    public void UpdateValue(float2 newValue, float deltaTime)
    {
        Delta = newValue - Value;
        Value = newValue;
        Velocity = deltaTime > 0 ? Delta / deltaTime : float2.Zero;
    }
}
