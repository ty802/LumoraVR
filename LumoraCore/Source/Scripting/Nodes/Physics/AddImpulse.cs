// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Components;
using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Kicks the slot's rigid body at once, in newton seconds, world space.
[Node("Physics/Add Impulse", Capability.Physics)]
public sealed class AddImpulse : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, float3 impulse)
    {
        if (ScriptPhysics.Body(ctx, slot) is { } body)
            body.AddImpulse(ScriptWrites.Finite(impulse, "impulse"));
    }
}
