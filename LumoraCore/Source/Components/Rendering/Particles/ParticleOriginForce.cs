// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Pulls each particle back toward the exact point it was BORN at, not toward a shared centre. Dust
// disturbed and resettling, sparks tethered to the surface that threw them.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleOriginForce : ParticleModuleBase
{
    public readonly Sync<float> Force = new();
    public readonly Sync<RadialForceMode> Mode = new();
    public readonly Sync<float> MinDistance = new();
    public readonly Sync<float> MaxDistance = new();

    public override void OnInit()
    {
        base.OnInit();
        Force.Value = 1f;
        Mode.Value = RadialForceMode.Linear;
        MinDistance.Value = 0.01f;
        MaxDistance.Value = float.PositiveInfinity;
    }

    internal override ParticleSimModule CreateSimModule() => new OriginRadialForce();

    internal override void PushParameters(ParticleSimModule module)
    {
        var origin = (OriginRadialForce)module;
        origin.Force = Force.Value;
        origin.Mode = Mode.Value;
        origin.MinDistance = MinDistance.Value;
        origin.MaxDistance = MaxDistance.Value;
    }
}
