// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// Twists the slot's rigid body for this step, in newton metres, world space.
[Node("Physics/Add Torque", Capability.Physics)]
public sealed class AddTorque : ActionNode
{
    public static void Run(ScriptContext ctx, Slot? slot, float3 torque)
    {
        if (ScriptPhysics.Body(ctx, slot) is { } body)
            body.AddTorque(ScriptWrites.Finite(torque, "torque"));
    }
}
