// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Applies a fixed euler rotation, in degrees, to every emitted particle.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleRotation3DInitializer : ParticleModuleBase
{
    internal override bool ProducesRotation => true;

    public readonly Sync<float3> EulerAngles = new();

    internal override ParticleSimModule CreateSimModule() => new Rotation3DConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((Rotation3DConstantInitializer)module).EulerAngles = EulerAngles.Value;
}
