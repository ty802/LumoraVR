// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Emitters;
using SimParticles = Lumora.Simulation.Particles.Emitters;

namespace Lumora.Core.Components;

// Emits along a segment, with independent colours and directions at each end interpolated along it.
// Beams, trails behind a swung object, edge highlights.
[ComponentCategory("Rendering/Particles")]
public sealed class LineEmitter : ParticleEmitterBase
{
    public readonly Sync<float3> Point0 = new();
    public readonly Sync<float3> Point1 = new();
    public readonly Sync<colorHDR> Color0 = new();
    public readonly Sync<colorHDR> Color1 = new();
    public readonly Sync<LineEmitterDirection> DirectionMode = new();
    public readonly Sync<float3> Direction0 = new();
    public readonly Sync<float3> Direction1 = new();

    public override void OnInit()
    {
        base.OnInit();
        Point0.Value = float3.Left;
        Point1.Value = float3.Right;
        Color0.Value = colorHDR.White;
        Color1.Value = colorHDR.White;
        Direction0.Value = float3.Up;
        Direction1.Value = float3.Up;
    }

    internal override ParticleSimEmitter CreateSimEmitter() => new SimParticles.LineEmitter();

    internal override void PushParameters(ParticleSimEmitter emitter, in float4x4 toSystemSpace)
    {
        var line = (SimParticles.LineEmitter)emitter;
        line.Transform = toSystemSpace;
        line.Point0 = Point0.Value;
        line.Point1 = Point1.Value;
        line.Color0 = Color0.Value;
        line.Color1 = Color1.Value;
        line.DirectionMode = DirectionMode.Value;
        line.Direction0 = Direction0.Value;
        line.Direction1 = Direction1.Value;
        line.UpDirection = float3.Up;
        line.RandomDirectionWeight = RandomDirectionWeight.Value;
    }
}
