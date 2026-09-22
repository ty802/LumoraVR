// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Rolls each particle around its facing axis by a random angle, in degrees.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleRotationRangeInitializer : ParticleModuleBase
{
    internal override bool ProducesRotation => true;

    public readonly Sync<float> MinRoll = new();
    public readonly Sync<float> MaxRoll = new();

    public override void OnInit()
    {
        base.OnInit();
        MaxRoll.Value = 360f;
    }

    internal override ParticleSimModule CreateSimModule() => new RotationRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var rotation = (RotationRangeInitializer)module;
        rotation.MinRoll = MinRoll.Value * (MathF.PI / 180f);
        rotation.MaxRoll = MaxRoll.Value * (MathF.PI / 180f);
    }
}
