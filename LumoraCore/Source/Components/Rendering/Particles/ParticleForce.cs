// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Constant directional force (system-local space). Gravity in any direction, wind, updraft.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleForce : ParticleModuleBase
{
    public readonly Sync<float3> Force = new();

    public override void OnInit()
    {
        base.OnInit();
        Force.Value = new float3(0f, -9.81f, 0f);
    }

    internal override ParticleSimModule CreateSimModule() => new LinearForce();

    internal override void PushParameters(ParticleSimModule module)
    {
        var force = (LinearForce)module;
        force.Mode = ParticleForceMode.Additive;
        force.Strength = 1f;
        force.Force = Force.Value;
    }
}
