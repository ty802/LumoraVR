// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;

namespace Lumora.Core.Components;

// Shifts a particle along its own facing by an amount that grows with its speed.
//
// Pair it with a size module that stretches by speed. On its own a stretching particle grows out of
// its centre, so it reaches backwards into whatever emitted it as much as forwards; this anchors the
// growth at the trailing end so a spark or a smoke puff extends the way it is travelling.
//
// The offset is applied in the particle's ROTATED frame, so it follows the particle's facing rather
// than a fixed world axis.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticlePivotBySpeed : ParticleModuleBase
{
    // Direction and scale of the shift, per metre per second. Forward by default, which is the axis
    // a stretched billboard grows along.
    [Group("Pivot")]
    public readonly Sync<float3> BaseOffset = new();

    [Group("Clamp")]
    public readonly Sync<float3> ClampMin = new();
    public readonly Sync<float3> ClampMax = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        BaseOffset.Value = float3.Forward;
        ClampMin.Value = new float3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        ClampMax.Value = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
    }

    internal override ParticleSimModule CreateSimModule() => new Simulation.Particles.Modules.PivotBySpeed();

    internal override void PushParameters(ParticleSimModule module)
    {
        var pivot = (Simulation.Particles.Modules.PivotBySpeed)module;
        pivot.BaseOffset = BaseOffset.Value;
        pivot.ClampMin = ClampMin.Value;
        pivot.ClampMax = ClampMax.Value;
    }
}
