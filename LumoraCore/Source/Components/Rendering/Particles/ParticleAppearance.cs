// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Tints each particle from a start colour to an end colour across its lifetime.
//
// Attaching ANY appearance module retires the system's own built-in start/end envelope: the two write
// the same buffers, so leaving both on would mean whichever ran last silently won. The system's
// StartColor/EndColor/StartSize/EndSize fields stop applying the moment one of these is attached.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorOverLifetime : ParticleModuleBase
{
    public readonly Sync<colorHDR> StartColor = new();
    public readonly Sync<colorHDR> EndColor = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        StartColor.Value = colorHDR.White;
        EndColor.Value = colorHDR.Transparent;
    }

    internal override ParticleSimModule CreateSimModule() => new ColorOverLifetimeStartEnd();

    internal override void PushParameters(ParticleSimModule module)
    {
        var color = (ColorOverLifetimeStartEnd)module;
        color.StartColor = StartColor.Value;
        color.EndColor = EndColor.Value;
    }
}

// Tints each particle from a multi-stop gradient across its lifetime. Stops are two parallel lists:
// Times holds the position of each stop, Colors the colour there. A stop with no matching time is
// ignored rather than guessed at.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorGradient : ParticleModuleBase
{
    // 0 at birth to 1 at death.
    public readonly SyncFieldList<float> Times = new();

    // One per entry in Times.
    public readonly SyncFieldList<colorHDR> Colors = new();

    private readonly ColorGradient _gradient = new();
    private int _lastRevision = -1;

    internal override bool OverridesAppearance => true;

    internal override ParticleSimModule CreateSimModule() => new ColorOverLifetimeGradient();

    internal override void PushParameters(ParticleSimModule module)
    {
        RebuildGradient();
        ((ColorOverLifetimeGradient)module).Gradient = _gradient;
    }

    // Rebuild only when a stop actually changed. The check is a cheap hash of the two lists rather
    // than a change event, because a gradient is edited by hand a few times and then read every frame
    // forever - the read path is what has to be free. -xlinka
    private void RebuildGradient()
    {
        int count = Times.Count < Colors.Count ? Times.Count : Colors.Count;
        int revision = count;
        for (int i = 0; i < count; i++)
            revision = revision * 31 + Times[i].GetHashCode() * 17 + Colors[i].GetHashCode();
        if (revision == _lastRevision)
            return;
        _lastRevision = revision;

        var keys = new ColorGradientKey[count];
        for (int i = 0; i < count; i++)
            keys[i] = new ColorGradientKey(Times[i], Colors[i]);
        _gradient.SetKeys(keys);
    }
}

// Scales alpha from a curve across the lifetime, leaving the colour alone.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleAlphaOverLifetime : ParticleModuleBase
{
    // Key positions, 0 at birth to 1 at death.
    public readonly SyncFieldList<float> Times = new();

    // Alpha multiplier at each key, one per entry in Times.
    public readonly SyncFieldList<float> Values = new();

    private readonly FloatCurve _curve = new();
    private int _lastRevision = -1;

    internal override bool OverridesAppearance => true;

    internal override ParticleSimModule CreateSimModule() => new AlphaOverLifetimeCurve();

    internal override void PushParameters(ParticleSimModule module)
    {
        ParticleCurveHelper.Rebuild(Times, Values, _curve, ref _lastRevision);
        ((AlphaOverLifetimeCurve)module).Curve = _curve;
    }
}

// Scales size from a curve across the lifetime, for envelopes a straight lerp cannot express.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeCurve : ParticleModuleBase
{
    // Key positions, 0 at birth to 1 at death.
    public readonly SyncFieldList<float> Times = new();

    // Size multiplier at each key, one per entry in Times.
    public readonly SyncFieldList<float> Values = new();

    private readonly FloatCurve _curve = new();
    private int _lastRevision = -1;

    internal override bool OverridesAppearance => true;

    internal override ParticleSimModule CreateSimModule() => new SizeOverLifetimeCurve();

    internal override void PushParameters(ParticleSimModule module)
    {
        ParticleCurveHelper.Rebuild(Times, Values, _curve, ref _lastRevision);
        ((SizeOverLifetimeCurve)module).Curve = _curve;
    }
}

// Shared rebuild for the scalar-curve modules; see ParticleColorGradient for why it hashes.
internal static class ParticleCurveHelper
{
    public static void Rebuild(SyncFieldList<float> times, SyncFieldList<float> values, FloatCurve curve, ref int lastRevision)
    {
        int count = times.Count < values.Count ? times.Count : values.Count;
        int revision = count;
        for (int i = 0; i < count; i++)
            revision = revision * 31 + times[i].GetHashCode() * 17 + values[i].GetHashCode();
        if (revision == lastRevision)
            return;
        lastRevision = revision;

        var keys = new FloatCurveKey[count];
        for (int i = 0; i < count; i++)
            keys[i] = new FloatCurveKey(times[i], values[i]);
        curve.SetKeys(keys);
    }
}

// Scales each axis of the particle size from a start to an end value across its lifetime.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleSizeOverLifetime : ParticleModuleBase
{
    public readonly Sync<float3> StartSize = new();
    public readonly Sync<float3> EndSize = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        StartSize.Value = float3.One;
        EndSize.Value = float3.Zero;
    }

    internal override ParticleSimModule CreateSimModule() => new SizeOverLifetimeStartEnd();

    internal override void PushParameters(ParticleSimModule module)
    {
        var size = (SizeOverLifetimeStartEnd)module;
        size.StartSize = StartSize.Value;
        size.EndSize = EndSize.Value;
    }
}

// Tints particles by how fast they are moving, between a slow colour and a fast colour.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorBySpeed : ParticleModuleBase
{
    public readonly Sync<float> MinSpeed = new();
    public readonly Sync<float> MaxSpeed = new();
    public readonly Sync<colorHDR> MinColor = new();
    public readonly Sync<colorHDR> MaxColor = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        MaxSpeed.Value = 1f;
        MinColor.Value = colorHDR.Black;
        MaxColor.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new ColorBySpeed();

    internal override void PushParameters(ParticleSimModule module)
    {
        var color = (ColorBySpeed)module;
        color.MinSpeed = MinSpeed.Value;
        color.MaxSpeed = MaxSpeed.Value;
        color.MinColor = MinColor.Value;
        color.MaxColor = MaxColor.Value;
    }
}

// Tints particles by how their travel direction lines up with a reference direction: one colour
// heading along it, another at right angles, a third coming back the other way.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleColorByDirection : ParticleModuleBase
{
    // In the system's simulation space.
    public readonly Sync<float3> ReferenceDirection = new();

    public readonly Sync<colorHDR> AlignedColor = new();
    public readonly Sync<colorHDR> OrthogonalColor = new();
    public readonly Sync<colorHDR> OppositeColor = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        ReferenceDirection.Value = float3.Forward;
        AlignedColor.Value = colorHDR.White;
        OrthogonalColor.Value = colorHDR.White;
        OppositeColor.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new ColorByVelocityDirection();

    internal override void PushParameters(ParticleSimModule module)
    {
        var color = (ColorByVelocityDirection)module;
        color.ReferenceDirection = ReferenceDirection.Value;
        color.AlignedColor = AlignedColor.Value;
        color.OrthogonalColor = OrthogonalColor.Value;
        color.OppositeColor = OppositeColor.Value;
    }
}

// Scales size by one scalar from a start to an end value across the lifetime.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleUniformSizeOverLifetime : ParticleModuleBase
{
    public readonly Sync<float> StartSize = new();
    public readonly Sync<float> EndSize = new();

    internal override bool OverridesAppearance => true;

    public override void OnInit()
    {
        base.OnInit();
        StartSize.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new UniformSizeOverLifetimeStartEnd();

    internal override void PushParameters(ParticleSimModule module)
    {
        var size = (UniformSizeOverLifetimeStartEnd)module;
        size.StartSize = StartSize.Value;
        size.EndSize = EndSize.Value;
    }
}
