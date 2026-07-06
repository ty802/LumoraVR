// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>Surface modifier with a constant value, regardless of contact point.</summary>
[ComponentCategory("Locomotion/Modifiers")]
public class ConstantCharacterControllerModifier : CharacterControllerModifier
{
    public readonly Sync<float> Value;

    public ConstantCharacterControllerModifier()
    {
        Value = new Sync<float>(this, 1f);
    }

    protected override float ComputeBaseParameter(in float3 contactPosition, in float3 contactNormal)
        => Value.Value;
}
