// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Rolls every emitted particle around its facing axis by a fixed angle, in degrees.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleRotationInitializer : ParticleModuleBase
{
    public readonly Sync<float> Roll = new();

    internal override bool ProducesRotation => true;

    internal override ParticleSimModule CreateSimModule() => new RotationConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((RotationConstantInitializer)module).Roll = Roll.Value * (MathF.PI / 180f);
}
