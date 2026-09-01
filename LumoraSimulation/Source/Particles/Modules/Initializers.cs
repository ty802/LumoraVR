// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Modules;

// Base for modules that only touch a particle once, at birth. They do no per-frame work at all, which
// is the whole reason initialization is a separate phase: a system with ten initializers costs exactly
// as much per frame as a system with none. -xlinka
public abstract class ParticleValueInitializer<T> : ParticleSimModule where T : unmanaged
{
    // The column being initialized. Empty means the owning module is missing; skip.
    protected abstract Span<T> Buffer { get; }

    public override ParticleSimPhase Phase => ParticleSimPhase.Initializer;

    public override void InitializeNewParticles(int index, int count)
    {
        var buffer = Buffer;
        if (buffer.IsEmpty || buffer.Length < index + count)
            return;
        InitializeValues(buffer.Slice(index, count));
    }

    protected abstract void InitializeValues(Span<T> data);

    public override void BackfillExistingParticles(int count) { }

    public override void SimulateChunk(int offset, int count, float deltaTime) { }
}

// LIFETIME

// Scales the emitted lifetime by a fixed value.
public sealed class LifetimeConstantInitializer : ParticleValueInitializer<float>
{
    public float Lifetime = 1f;

    protected override Span<float> Buffer => Simulation.StartingLifetimes;

    protected override void InitializeValues(Span<float> data)
    {
        for (int i = 0; i < data.Length; i++)
            data[i] *= Lifetime;
    }
}

// Scales the emitted lifetime by a value drawn per particle from a range.
public sealed class LifetimeRangeInitializer : ParticleValueInitializer<float>
{
    public float MinLifetime = 1f;
    public float MaxLifetime = 2f;

    protected override Span<float> Buffer => Simulation.StartingLifetimes;

    protected override void InitializeValues(Span<float> data)
    {
        var random = Simulation.Random;
        for (int i = 0; i < data.Length; i++)
            data[i] *= random.Range(MinLifetime, MaxLifetime);
    }
}

// SPEED AND VELOCITY

// Base for initializers writing into the velocity column the position integrator owns.
public abstract class VelocityInitializerBase : ParticleValueInitializer<float3>
{
    protected PositionSimulatorModule? PositionModule { get; private set; }

    protected override Span<float3> Buffer
        => PositionModule != null ? PositionModule.Velocities : default;

    public override void ModulesUpdated()
    {
        base.ModulesUpdated();
        PositionModule = Simulation.TryGetModule<PositionSimulatorModule>();
    }
}

// Scales the emission direction to a fixed speed. Direction comes from the emitter.
public sealed class SpeedConstantInitializer : VelocityInitializerBase
{
    public float Speed = 1f;

    protected override void InitializeValues(Span<float3> data)
    {
        for (int i = 0; i < data.Length; i++)
            data[i] *= Speed;
    }
}

// Scales the emission direction to a speed drawn per particle from a range.
public sealed class SpeedRangeInitializer : VelocityInitializerBase
{
    public float MinSpeed;
    public float MaxSpeed = 1f;

    protected override void InitializeValues(Span<float3> data)
    {
        var random = Simulation.Random;
        for (int i = 0; i < data.Length; i++)
            data[i] *= random.Range(MinSpeed, MaxSpeed);
    }
}

// Replaces the velocity outright with a fixed vector, ignoring the emission direction.
public sealed class VelocityConstantInitializer : VelocityInitializerBase
{
    public float3 Velocity;

    protected override void InitializeValues(Span<float3> data) => data.Fill(Velocity);
}

// Replaces the velocity with a vector drawn per particle from a per-axis range.
public sealed class VelocityRangeInitializer : VelocityInitializerBase
{
    public float3 MinVelocity;
    public float3 MaxVelocity = float3.One;

    protected override void InitializeValues(Span<float3> data)
    {
        var random = Simulation.Random;
        for (int i = 0; i < data.Length; i++)
            data[i] = random.Range(MinVelocity, MaxVelocity);
    }
}

// SIZE

// Scales the emitted size by a fixed per-axis value.
public sealed class SizeConstantInitializer : ParticleValueInitializer<float3>
{
    public float3 Size = float3.One;

    protected override Span<float3> Buffer => Simulation.StartingSizes;

    protected override void InitializeValues(Span<float3> data)
    {
        for (int i = 0; i < data.Length; i++)
            data[i] *= Size;
    }
}

// Scales the emitted size from a per-axis range. Uniform draws ONE number and applies it to all three
// axes, which keeps particles proportional; non-uniform draws per axis and gives you squashed shapes.
public sealed class SizeRangeInitializer : ParticleValueInitializer<float3>
{
    public float3 MinSize = float3.One;
    public float3 MaxSize = float3.One;
    public bool Uniform = true;

    protected override Span<float3> Buffer => Simulation.StartingSizes;

    protected override void InitializeValues(Span<float3> data)
    {
        var random = Simulation.Random;
        if (Uniform)
        {
            for (int i = 0; i < data.Length; i++)
            {
                float t = random.Value;
                data[i] *= MinSize + (MaxSize - MinSize) * t;
            }
            return;
        }
        for (int i = 0; i < data.Length; i++)
            data[i] *= random.Range(MinSize, MaxSize);
    }
}

// Scales the emitted size by one fixed scalar on every axis.
public sealed class UniformSizeConstantInitializer : ParticleValueInitializer<float3>
{
    public float Size = 1f;

    protected override Span<float3> Buffer => Simulation.StartingSizes;

    protected override void InitializeValues(Span<float3> data)
    {
        for (int i = 0; i < data.Length; i++)
            data[i] *= Size;
    }
}

// Scales the emitted size by one scalar per particle, drawn from a range.
public sealed class UniformSizeRangeInitializer : ParticleValueInitializer<float3>
{
    public float MinSize = 1f;
    public float MaxSize = 1f;

    protected override Span<float3> Buffer => Simulation.StartingSizes;

    protected override void InitializeValues(Span<float3> data)
    {
        var random = Simulation.Random;
        for (int i = 0; i < data.Length; i++)
            data[i] *= random.Range(MinSize, MaxSize);
    }
}

// COLOUR

// Multiplies the emitted colour by a fixed colour.
public sealed class ColorConstantInitializer : ParticleValueInitializer<colorHDR>
{
    public colorHDR Color = colorHDR.White;

    protected override Span<colorHDR> Buffer => Simulation.StartingColors;

    protected override void InitializeValues(Span<colorHDR> data)
    {
        for (int i = 0; i < data.Length; i++)
            data[i] *= Color;
    }
}

// Multiplies the emitted colour by one drawn per particle between two colours.
public sealed class ColorRangeInitializer : ParticleValueInitializer<colorHDR>
{
    public colorHDR MinColor = colorHDR.White;
    public colorHDR MaxColor = colorHDR.White;

    protected override Span<colorHDR> Buffer => Simulation.StartingColors;

    protected override void InitializeValues(Span<colorHDR> data)
    {
        var random = Simulation.Random;
        for (int i = 0; i < data.Length; i++)
            data[i] *= random.Range(MinColor, MaxColor);
    }
}

// Multiplies the emitted colour by one picked from a list. Optional per-entry weights bias the draw;
// without them every entry is equally likely. A weight array shorter than the colour list is ignored
// outright rather than half-applied, because a partly-weighted draw is a bug that looks like a design.
public sealed class ColorListInitializer : ParticleValueInitializer<colorHDR>
{
    public colorHDR[]? Colors;
    public float[]? Weights;

    protected override Span<colorHDR> Buffer => Simulation.StartingColors;

    protected override void InitializeValues(Span<colorHDR> data)
    {
        var colors = Colors;
        if (colors == null || colors.Length == 0)
            return;

        var random = Simulation.Random;
        var weights = Weights != null && Weights.Length == colors.Length ? Weights : null;
        if (weights == null)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] *= colors[random.Range(0, colors.Length)];
            return;
        }

        float total = 0f;
        for (int i = 0; i < weights.Length; i++)
            total += MathF.Max(0f, weights[i]);
        if (total <= 0f)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] *= colors[random.Range(0, colors.Length)];
            return;
        }

        for (int i = 0; i < data.Length; i++)
        {
            float pick = random.Range(0f, total);
            int index = colors.Length - 1;
            for (int c = 0; c < weights.Length; c++)
            {
                pick -= MathF.Max(0f, weights[c]);
                if (pick <= 0f)
                {
                    index = c;
                    break;
                }
            }
            data[i] *= colors[index];
        }
    }
}

// ROTATION

// Rolls each particle around its facing axis by a fixed angle, in radians.
public sealed class RotationConstantInitializer : ParticleValueInitializer<floatQ>
{
    public float Roll;

    protected override Span<floatQ> Buffer => Simulation.StartingRotations;

    protected override void InitializeValues(Span<floatQ> data)
    {
        var roll = floatQ.AxisAngleRad(float3.Forward, Roll);
        for (int i = 0; i < data.Length; i++)
            data[i] *= roll;
    }
}

// Rolls each particle around its facing axis by a random angle from a range, in radians.
public sealed class RotationRangeInitializer : ParticleValueInitializer<floatQ>
{
    public float MinRoll;
    public float MaxRoll = MathF.PI * 2f;

    protected override Span<floatQ> Buffer => Simulation.StartingRotations;

    protected override void InitializeValues(Span<floatQ> data)
    {
        var random = Simulation.Random;
        for (int i = 0; i < data.Length; i++)
            data[i] *= floatQ.AxisAngleRad(float3.Forward, random.Range(MinRoll, MaxRoll));
    }
}

// Applies a fixed euler rotation, in degrees, to every emitted particle.
public sealed class Rotation3DConstantInitializer : ParticleValueInitializer<floatQ>
{
    public float3 EulerAngles;

    protected override Span<floatQ> Buffer => Simulation.StartingRotations;

    protected override void InitializeValues(Span<floatQ> data)
    {
        var rotation = floatQ.Euler(EulerAngles);
        for (int i = 0; i < data.Length; i++)
            data[i] *= rotation;
    }
}

// Applies a random euler rotation, in degrees, drawn per particle from a per-axis range.
public sealed class Rotation3DEulerRangeInitializer : ParticleValueInitializer<floatQ>
{
    public float3 MinEulerAngles;
    public float3 MaxEulerAngles = new float3(360f, 360f, 360f);

    protected override Span<floatQ> Buffer => Simulation.StartingRotations;

    protected override void InitializeValues(Span<floatQ> data)
    {
        var random = Simulation.Random;
        for (int i = 0; i < data.Length; i++)
            data[i] *= floatQ.Euler(random.Range(MinEulerAngles, MaxEulerAngles));
    }
}

// ANGULAR VELOCITY

// Base for initializers writing into the angular velocity column the rotation integrator owns.
public abstract class AngularVelocityInitializerBase : ParticleValueInitializer<float3>
{
    protected RotationSimulatorModule? RotationModule { get; private set; }

    protected override Span<float3> Buffer
        => RotationModule != null ? RotationModule.AngularVelocities : default;

    public override void ModulesUpdated()
    {
        base.ModulesUpdated();
        RotationModule = Simulation.TryGetModule<RotationSimulatorModule>();
    }
}

// Fixed spin around the facing axis, in radians per second.
public sealed class AngularVelocityConstantInitializer : AngularVelocityInitializerBase
{
    public float AngularVelocity;

    protected override void InitializeValues(Span<float3> data)
        => data.Fill(new float3(0f, 0f, AngularVelocity));
}

// Spin around the facing axis drawn per particle from a range, in radians per second.
public sealed class AngularVelocityRangeInitializer : AngularVelocityInitializerBase
{
    public float MinAngularVelocity;
    public float MaxAngularVelocity = MathF.PI;

    protected override void InitializeValues(Span<float3> data)
    {
        var random = Simulation.Random;
        for (int i = 0; i < data.Length; i++)
            data[i] = new float3(0f, 0f, random.Range(MinAngularVelocity, MaxAngularVelocity));
    }
}

// Fixed spin around all three axes, in radians per second.
public sealed class AngularVelocity3DConstantInitializer : AngularVelocityInitializerBase
{
    public float3 AngularVelocity;

    protected override void InitializeValues(Span<float3> data) => data.Fill(AngularVelocity);
}

// Spin around all three axes drawn per particle from a per-axis range, in radians per second.
public sealed class AngularVelocity3DRangeInitializer : AngularVelocityInitializerBase
{
    public float3 MinAngularVelocity;
    public float3 MaxAngularVelocity = new float3(MathF.PI, MathF.PI, MathF.PI);

    protected override void InitializeValues(Span<float3> data)
    {
        var random = Simulation.Random;
        for (int i = 0; i < data.Length; i++)
            data[i] = random.Range(MinAngularVelocity, MaxAngularVelocity);
    }
}
