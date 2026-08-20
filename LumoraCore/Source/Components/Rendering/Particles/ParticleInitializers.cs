// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Initializers run once per particle, at birth, and cost nothing per frame after that. They MULTIPLY
// what the emitter produced rather than replacing it (except the velocity ones, which are explicitly
// overrides), so stacking a lifetime initializer on an emitter that already sets lifetimes scales it
// instead of throwing the emitter's work away.
//
// Angles are in DEGREES on these components - that is what the rest of the engine's rotation fields
// use - and converted to radians on the way into the simulation. -xlinka
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleLifetimeInitializer : ParticleModuleBase
{
    // Multiplier on the emitted lifetime.
    public readonly Sync<float> Lifetime = new();

    public override void OnInit()
    {
        base.OnInit();
        Lifetime.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new LifetimeConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((LifetimeConstantInitializer)module).Lifetime = Lifetime.Value;
}

// Multiplies the emitted lifetime by a value drawn per particle from a range.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleLifetimeRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinLifetime = new();
    public readonly Sync<float> MaxLifetime = new();

    public override void OnInit()
    {
        base.OnInit();
        MinLifetime.Value = 1f;
        MaxLifetime.Value = 2f;
    }

    internal override ParticleSimModule CreateSimModule() => new LifetimeRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var lifetime = (LifetimeRangeInitializer)module;
        lifetime.MinLifetime = MinLifetime.Value;
        lifetime.MaxLifetime = MaxLifetime.Value;
    }
}

// Scales the emission direction to a fixed speed. The direction comes from the emitter.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSpeedInitializer : ParticleModuleBase
{
    public readonly Sync<float> Speed = new();

    public override void OnInit()
    {
        base.OnInit();
        Speed.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new SpeedConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((SpeedConstantInitializer)module).Speed = Speed.Value;
}

// Scales the emission direction to a speed drawn per particle from a range.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSpeedRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinSpeed = new();
    public readonly Sync<float> MaxSpeed = new();

    public override void OnInit()
    {
        base.OnInit();
        MaxSpeed.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new SpeedRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var speed = (SpeedRangeInitializer)module;
        speed.MinSpeed = MinSpeed.Value;
        speed.MaxSpeed = MaxSpeed.Value;
    }
}

// Replaces the velocity outright with a fixed vector, ignoring the emission direction.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleVelocityInitializer : ParticleModuleBase
{
    // Initial velocity in the system's simulation space.
    public readonly Sync<float3> Velocity = new();

    public override void OnInit()
    {
        base.OnInit();
        Velocity.Value = float3.Up;
    }

    internal override ParticleSimModule CreateSimModule() => new VelocityConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((VelocityConstantInitializer)module).Velocity = Velocity.Value;
}

// Replaces the velocity with a vector drawn per particle from a per-axis range.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleVelocityRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float3> MinVelocity = new();
    public readonly Sync<float3> MaxVelocity = new();

    public override void OnInit()
    {
        base.OnInit();
        MaxVelocity.Value = float3.One;
    }

    internal override ParticleSimModule CreateSimModule() => new VelocityRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var velocity = (VelocityRangeInitializer)module;
        velocity.MinVelocity = MinVelocity.Value;
        velocity.MaxVelocity = MaxVelocity.Value;
    }
}

// Scales the emitted size by a fixed per-axis value.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeInitializer : ParticleModuleBase
{
    public readonly Sync<float3> Size = new();

    public override void OnInit()
    {
        base.OnInit();
        Size.Value = float3.One;
    }

    internal override ParticleSimModule CreateSimModule() => new SizeConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((SizeConstantInitializer)module).Size = Size.Value;
}

// Scales the emitted size from a per-axis range. Uniform draws ONE number and applies it to all three
// axes, keeping particles proportional; turn it off to draw per axis and get squashed shapes.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float3> MinSize = new();
    public readonly Sync<float3> MaxSize = new();
    public readonly Sync<bool> Uniform = new();

    public override void OnInit()
    {
        base.OnInit();
        MinSize.Value = float3.One;
        MaxSize.Value = float3.One;
        Uniform.Value = true;
    }

    internal override ParticleSimModule CreateSimModule() => new SizeRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var size = (SizeRangeInitializer)module;
        size.MinSize = MinSize.Value;
        size.MaxSize = MaxSize.Value;
        size.Uniform = Uniform.Value;
    }
}

// Scales the emitted size by one scalar per particle, drawn from a range.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleUniformSizeRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinSize = new();
    public readonly Sync<float> MaxSize = new();

    public override void OnInit()
    {
        base.OnInit();
        MinSize.Value = 1f;
        MaxSize.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new UniformSizeRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var size = (UniformSizeRangeInitializer)module;
        size.MinSize = MinSize.Value;
        size.MaxSize = MaxSize.Value;
    }
}

// Multiplies the emitted colour by a fixed colour.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorInitializer : ParticleModuleBase
{
    public readonly Sync<colorHDR> Color = new();

    public override void OnInit()
    {
        base.OnInit();
        Color.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new ColorConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((ColorConstantInitializer)module).Color = Color.Value;
}

// Multiplies the emitted colour by one drawn per particle between two colours.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorRangeInitializer : ParticleModuleBase
{
    public readonly Sync<colorHDR> MinColor = new();
    public readonly Sync<colorHDR> MaxColor = new();

    public override void OnInit()
    {
        base.OnInit();
        MinColor.Value = colorHDR.White;
        MaxColor.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new ColorRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var color = (ColorRangeInitializer)module;
        color.MinColor = MinColor.Value;
        color.MaxColor = MaxColor.Value;
    }
}

// Multiplies the emitted colour by one picked from a list. Optional per-entry weights bias the draw;
// a weight list whose length does not match the colour list is ignored outright rather than applied
// halfway, because a partly-weighted draw is a bug that looks like a design decision.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorListInitializer : ParticleModuleBase
{
    public readonly SyncFieldList<colorHDR> Colors = new();

    // Optional relative weights, one per colour. Leave empty for an even draw.
    public readonly SyncFieldList<float> Weights = new();

    private colorHDR[] _colors = global::System.Array.Empty<colorHDR>();
    private float[] _weights = global::System.Array.Empty<float>();

    internal override ParticleSimModule CreateSimModule() => new ColorListInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        if (_colors.Length != Colors.Count)
            _colors = new colorHDR[Colors.Count];
        for (int i = 0; i < Colors.Count; i++)
            _colors[i] = Colors[i];

        if (_weights.Length != Weights.Count)
            _weights = new float[Weights.Count];
        for (int i = 0; i < Weights.Count; i++)
            _weights[i] = Weights[i];

        var list = (ColorListInitializer)module;
        list.Colors = _colors;
        list.Weights = _weights.Length == _colors.Length && _weights.Length > 0 ? _weights : null;
    }
}

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

// Applies a random euler rotation, in degrees, drawn per particle from a per-axis range.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleRotation3DRangeInitializer : ParticleModuleBase
{
    internal override bool ProducesRotation => true;

    public readonly Sync<float3> MinEulerAngles = new();
    public readonly Sync<float3> MaxEulerAngles = new();

    public override void OnInit()
    {
        base.OnInit();
        MaxEulerAngles.Value = new float3(360f, 360f, 360f);
    }

    internal override ParticleSimModule CreateSimModule() => new Rotation3DEulerRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var rotation = (Rotation3DEulerRangeInitializer)module;
        rotation.MinEulerAngles = MinEulerAngles.Value;
        rotation.MaxEulerAngles = MaxEulerAngles.Value;
    }
}

// Fixed spin around the facing axis, in degrees per second.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleAngularVelocityInitializer : ParticleModuleBase
{
    public readonly Sync<float> AngularVelocity = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        AngularVelocity.Value = 180f;
    }

    internal override ParticleSimModule CreateSimModule() => new AngularVelocityConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((AngularVelocityConstantInitializer)module).AngularVelocity = AngularVelocity.Value * (MathF.PI / 180f);
}

// Spin around the facing axis drawn per particle from a range, in degrees per second.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleAngularVelocityRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinAngularVelocity = new();
    public readonly Sync<float> MaxAngularVelocity = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        MinAngularVelocity.Value = -180f;
        MaxAngularVelocity.Value = 180f;
    }

    internal override ParticleSimModule CreateSimModule() => new AngularVelocityRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var angular = (AngularVelocityRangeInitializer)module;
        angular.MinAngularVelocity = MinAngularVelocity.Value * (MathF.PI / 180f);
        angular.MaxAngularVelocity = MaxAngularVelocity.Value * (MathF.PI / 180f);
    }
}

// Fixed spin around all three axes, in degrees per second.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleAngularVelocity3DInitializer : ParticleModuleBase
{
    public readonly Sync<float3> AngularVelocity = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        AngularVelocity.Value = new float3(0f, 0f, 180f);
    }

    internal override ParticleSimModule CreateSimModule() => new AngularVelocity3DConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((AngularVelocity3DConstantInitializer)module).AngularVelocity = AngularVelocity.Value * (MathF.PI / 180f);
}

// Spin around all three axes drawn per particle from a per-axis range, in degrees per second.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleAngularVelocity3DRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float3> MinAngularVelocity = new();
    public readonly Sync<float3> MaxAngularVelocity = new();

    internal override bool RequiresRotationSimulation => true;

    public override void OnInit()
    {
        base.OnInit();
        MinAngularVelocity.Value = new float3(-180f, -180f, -180f);
        MaxAngularVelocity.Value = new float3(180f, 180f, 180f);
    }

    internal override ParticleSimModule CreateSimModule() => new AngularVelocity3DRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var angular = (AngularVelocity3DRangeInitializer)module;
        angular.MinAngularVelocity = MinAngularVelocity.Value * (MathF.PI / 180f);
        angular.MaxAngularVelocity = MaxAngularVelocity.Value * (MathF.PI / 180f);
    }
}

// Scales the emitted size by one fixed scalar on every axis.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleUniformSizeInitializer : ParticleModuleBase
{
    public readonly Sync<float> Size = new();

    public override void OnInit()
    {
        base.OnInit();
        Size.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new UniformSizeConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((UniformSizeConstantInitializer)module).Size = Size.Value;
}

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
