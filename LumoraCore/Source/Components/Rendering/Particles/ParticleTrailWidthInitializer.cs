// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Trail initializers run once, when a trail is allocated, and MULTIPLY into what the inheritance
// settings already produced. They cost nothing per frame.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailWidthInitializer : ParticleModuleBase
{
    public readonly Sync<float> Width = new();

    public override void OnInit()
    {
        base.OnInit();
        Width.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailWidthConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((TrailWidthConstantInitializer)module).Width = Width.Value;
}
