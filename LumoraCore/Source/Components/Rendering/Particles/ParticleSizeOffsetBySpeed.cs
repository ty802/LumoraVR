// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;

namespace Lumora.Core.Components;

// Adds to particle size in proportion to velocity, per axis.
//
// The additive counterpart to <see cref="ParticleSizeBySpeed"/>, and not a substitute for it: a
// multiplier cannot grow a particle that starts at zero and cannot leave a slow one at its authored
// size. An offset does both, which is what stretches a spark along its travel while its cross
// section stays the width it was drawn.
//
// Two clamps in order - the offset is limited before it is added, then the result is limited after.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeOffsetBySpeed : ParticleModuleBase
{
    // Metres of size added per metre per second of velocity, per axis.
    [Group("Velocity")]
    public readonly Sync<float3> VelocityMultiplier = new();

    [Group("Offset Clamp")]
    public readonly Sync<float3> OffsetClampMin = new();
    public readonly Sync<float3> OffsetClampMax = new();

    [Group("Result Clamp")]
    public readonly Sync<float3> ResultClampMin = new();
    public readonly Sync<float3> ResultClampMax = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        VelocityMultiplier.Value = float3.Zero;

        var negative = new float3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        var positive = new float3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        OffsetClampMin.Value = negative;
        OffsetClampMax.Value = positive;
        ResultClampMin.Value = float3.Zero;
        ResultClampMax.Value = positive;
    }

    internal override ParticleSimModule CreateSimModule() => new Simulation.Particles.Modules.SizeOffsetByVelocity();

    internal override void PushParameters(ParticleSimModule module)
    {
        var size = (Simulation.Particles.Modules.SizeOffsetByVelocity)module;
        size.VelocityMultiplier = VelocityMultiplier.Value;
        size.OffsetClampMin = OffsetClampMin.Value;
        size.OffsetClampMax = OffsetClampMax.Value;
        size.ResultClampMin = ResultClampMin.Value;
        size.ResultClampMax = ResultClampMax.Value;
    }
}
