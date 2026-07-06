// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>Which character movement parameter a surface modifier acts on.</summary>
public enum CharacterControllerParameter
{
    MaximumTractionSlope,
    MaximumSupportSlope,
    TractionSpeed,
    SlidingSpeed,
    TractionForce,
    SlidingForce,
    TractionJumpSpeed,
    SlidingJumpSpeed,
    MaximumGlueForce
}

/// <summary>
/// Surface-driven character parameter modifier: while a character stands on (contacts) a collider on
/// this slot, the chosen parameter is overridden/added/multiplied. Ice, speed strips, low-jump zones.
/// The character hook consumes TractionSpeed, TractionJumpSpeed and MaximumTractionSlope; the other
/// parameters are accepted for content compatibility but have no engine effect yet. -xlinka
/// </summary>
[ComponentCategory("Locomotion/Modifiers")]
public abstract class CharacterControllerModifier : Component
{
    public enum Mode
    {
        Override,
        Add,
        Multiply
    }

    public readonly Sync<CharacterControllerParameter> Parameter;
    public readonly Sync<Mode> ModificationMode;

    protected CharacterControllerModifier()
    {
        Parameter = new Sync<CharacterControllerParameter>(this, CharacterControllerParameter.TractionSpeed);
        ModificationMode = new Sync<Mode>(this, Mode.Multiply);
    }

    public void ComputeParameter(ref float value, in float3 contactPosition, in float3 contactNormal)
    {
        float baseValue = ComputeBaseParameter(in contactPosition, in contactNormal);
        switch (ModificationMode.Value)
        {
            case Mode.Override:
                value = baseValue;
                break;
            case Mode.Add:
                value += baseValue;
                break;
            case Mode.Multiply:
                value *= baseValue;
                break;
        }
        if (!float.IsFinite(value))
            value = 0f;
    }

    protected abstract float ComputeBaseParameter(in float3 contactPosition, in float3 contactNormal);
}
