// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;
using Lumora.Simulation.Particles;
using Lumora.Simulation.Particles.Modules;

namespace Lumora.Core.Components;

// Leaves a streak behind every particle.
//
// The trail records where the particle HAS BEEN, so it stays put in the world once laid: sparks that
// arc, a rocket's smoke, a blade trail. Points are committed as the particle pulls MinVertexDistance
// clear of the last one and expire on MaxPointAge, independently of the particle's own lifetime.
//
// Trails cost a fixed MaxTrails x MaxPointsPerTrail block of memory up front and nothing per frame
// after that. Changing either of those two fields RESETS every live trail, so they are setup values,
// not something to drive. -xlinka
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrails : ParticleModuleBase
{
    [Group("Capacity")]
    // Live trails at once. Particles past it get none.
    public readonly Sync<int> MaxTrails = new();

    // Points one trail can hold. The oldest is dropped past it.
    public readonly Sync<int> MaxPointsPerTrail = new();

    // Fraction of particles that get a trail at all, 0 to 1.
    public readonly Sync<float> Ratio = new();

    public readonly Sync<StrandDistribution> Distribution = new();

    [Group("Shape")]
    // Metres the particle must travel before another point is committed.
    public readonly Sync<float> MinVertexDistance = new();

    // Seconds a point survives after being laid.
    public readonly Sync<float> MaxPointAge = new();

    // World diameter multiplier on every point.
    public readonly Sync<float> WidthScale = new();

    // Smoothed vertices the renderer should draw per control segment. 1 draws the raw polyline.
    public readonly Sync<int> Smoothing = new();

    [Group("Death")]
    // True cuts the trail the moment its particle dies.
    public readonly Sync<bool> DieWithParticle = new();

    // Seconds an orphaned trail takes to fade out. 0 leaves it to age out on MaxPointAge instead.
    public readonly Sync<float> DeathFadeTime = new();

    [Group("Inheritance")]
    public readonly Sync<TrailInheritance> ColorInheritance = new();
    public readonly Sync<TrailInheritance> WidthInheritance = new();

    // Which component of the particle's size becomes the trail width.
    public readonly Sync<ParticleSizeAxis> WidthAxis = new();

    public readonly Sync<colorHDR> ColorScale = new();

    [Group("Over Trail")]
    // Key positions, 0 at the particle end to 1 at the far, oldest end.
    public readonly SyncFieldList<float> WidthTimes = new();
    public readonly SyncFieldList<float> WidthValues = new();

    public readonly SyncFieldList<float> ColorTimes = new();
    public readonly SyncFieldList<colorHDR> ColorValues = new();

    [Group("Rendering")]
    public readonly Sync<StrandUVMode> UVMode = new();

    // World length of one texture repeat under TilePerSegment.
    public readonly Sync<float> TileLength = new();

    public readonly Sync<StrandAlignment> Alignment = new();

    // Reference up axis for VelocityAligned, in the system's simulation space.
    public readonly Sync<float3> AlignmentUp = new();

    private readonly FloatCurve _widthCurve = new();
    private readonly ColorGradient _colorGradient = new();
    private int _widthRevision = -1;
    private int _colorRevision = -1;

    public override void OnInit()
    {
        base.OnInit();
        MaxTrails.Value = 256;
        MaxPointsPerTrail.Value = 48;
        Ratio.Value = 1f;
        MinVertexDistance.Value = 0.02f;
        MaxPointAge.Value = 0.6f;
        WidthScale.Value = 1f;
        Smoothing.Value = 4;
        DeathFadeTime.Value = 0.25f;
        ColorInheritance.Value = TrailInheritance.Continuous;
        WidthInheritance.Value = TrailInheritance.Birth;
        WidthAxis.Value = ParticleSizeAxis.Average;
        ColorScale.Value = colorHDR.White;
        TileLength.Value = 1f;
        AlignmentUp.Value = float3.Up;
    }

    internal override ParticleSimModule CreateSimModule() => new ParticleTrailsModule();

    internal override void PushParameters(ParticleSimModule module)
    {
        var trails = (ParticleTrailsModule)module;
        trails.MaxTrails = MaxTrails.Value;
        trails.MaxPointsPerTrail = MaxPointsPerTrail.Value;
        trails.Ratio = Ratio.Value;
        trails.Distribution = Distribution.Value;
        trails.MinVertexDistance = MinVertexDistance.Value;
        trails.MaxPointAge = MaxPointAge.Value;
        trails.WidthScale = WidthScale.Value;
        trails.Smoothing = Smoothing.Value;
        trails.DieWithParticle = DieWithParticle.Value;
        trails.DeathFadeTime = DeathFadeTime.Value;
        trails.ColorInheritance = ColorInheritance.Value;
        trails.WidthInheritance = WidthInheritance.Value;
        trails.WidthAxis = WidthAxis.Value;
        trails.ColorScale = ColorScale.Value;
        trails.UVMode = UVMode.Value;
        trails.TileLength = TileLength.Value;
        trails.Alignment = Alignment.Value;
        trails.AlignmentUp = AlignmentUp.Value;

        ParticleCurveHelper.Rebuild(WidthTimes, WidthValues, _widthCurve, ref _widthRevision);
        ParticleCurveHelper.RebuildGradient(ColorTimes, ColorValues, _colorGradient, ref _colorRevision);
        trails.WidthOverTrail = _widthCurve;
        trails.ColorOverTrail = _colorGradient;
    }
}

// Threads live particles onto ribbons in emission order.
//
// A ribbon is not a trail: every point is a LIVE particle, so the whole band moves when the particles
// do. That is what makes it right for a banner, an arc of energy or a stream, and wrong for a streak
// that has to stay where it was drawn - use ParticleTrails for that.
//
// Without a splitter attached, particles keep joining the same ribbon until MaxRibbonLength cuts it.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleRibbons : ParticleModuleBase
{
    [Group("Capacity")]
    // Published ribbons at once. Past it the OLDEST are dropped.
    public readonly Sync<int> MaxRibbons = new();

    // Particles per ribbon. Reaching it cuts the ribbon.
    public readonly Sync<int> MaxRibbonLength = new();

    // Fraction of particles that join a ribbon at all, 0 to 1.
    public readonly Sync<float> Ratio = new();

    public readonly Sync<StrandDistribution> Distribution = new();

    [Group("Appearance")]
    public readonly Sync<bool> UseParticleColor = new();
    public readonly Sync<bool> UseParticleSize = new();
    public readonly Sync<ParticleSizeAxis> WidthAxis = new();
    public readonly Sync<float> WidthScale = new();
    public readonly Sync<colorHDR> ColorScale = new();

    // Smoothed vertices per control segment. 1 draws the raw polyline through the particles.
    public readonly Sync<int> Smoothing = new();

    [Group("Over Ribbon")]
    // Key positions, 0 at the newest particle in the ribbon to 1 at the oldest.
    public readonly SyncFieldList<float> WidthTimes = new();
    public readonly SyncFieldList<float> WidthValues = new();

    public readonly SyncFieldList<float> ColorTimes = new();
    public readonly SyncFieldList<colorHDR> ColorValues = new();

    [Group("Rendering")]
    public readonly Sync<StrandUVMode> UVMode = new();
    public readonly Sync<float> TileLength = new();
    public readonly Sync<StrandAlignment> Alignment = new();
    public readonly Sync<float3> AlignmentUp = new();

    private readonly FloatCurve _widthCurve = new();
    private readonly ColorGradient _colorGradient = new();
    private int _widthRevision = -1;
    private int _colorRevision = -1;

    public override void OnInit()
    {
        base.OnInit();
        MaxRibbons.Value = 64;
        MaxRibbonLength.Value = 64;
        Ratio.Value = 1f;
        UseParticleColor.Value = true;
        UseParticleSize.Value = true;
        WidthAxis.Value = ParticleSizeAxis.Average;
        WidthScale.Value = 1f;
        ColorScale.Value = colorHDR.White;
        Smoothing.Value = 4;
        TileLength.Value = 1f;
        AlignmentUp.Value = float3.Up;
    }

    internal override ParticleSimModule CreateSimModule() => new ParticleRibbonsModule();

    internal override void PushParameters(ParticleSimModule module)
    {
        var ribbons = (ParticleRibbonsModule)module;
        ribbons.MaxRibbons = MaxRibbons.Value;
        ribbons.MaxRibbonLength = MaxRibbonLength.Value;
        ribbons.Ratio = Ratio.Value;
        ribbons.Distribution = Distribution.Value;
        ribbons.UseParticleColor = UseParticleColor.Value;
        ribbons.UseParticleSize = UseParticleSize.Value;
        ribbons.WidthAxis = WidthAxis.Value;
        ribbons.WidthScale = WidthScale.Value;
        ribbons.ColorScale = ColorScale.Value;
        ribbons.Smoothing = Smoothing.Value;
        ribbons.UVMode = UVMode.Value;
        ribbons.TileLength = TileLength.Value;
        ribbons.Alignment = Alignment.Value;
        ribbons.AlignmentUp = AlignmentUp.Value;

        ParticleCurveHelper.Rebuild(WidthTimes, WidthValues, _widthCurve, ref _widthRevision);
        ParticleCurveHelper.RebuildGradient(ColorTimes, ColorValues, _colorGradient, ref _colorRevision);
        ribbons.WidthOverRibbon = _widthCurve;
        ribbons.ColorOverRibbon = _colorGradient;
    }
}

// Cuts the ribbon after a run of particles, its length drawn per ribbon from a range. Set both bounds
// the same for exact fixed-length ribbons.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleRibbonSequenceSplitter : ParticleModuleBase
{
    public readonly Sync<int> MinCount = new();
    public readonly Sync<int> MaxCount = new();

    public override void OnInit()
    {
        base.OnInit();
        MinCount.Value = 5;
        MaxCount.Value = 10;
    }

    internal override ParticleSimModule CreateSimModule() => new SequenceRibbonSplitter();

    internal override void PushParameters(ParticleSimModule module)
    {
        var splitter = (SequenceRibbonSplitter)module;
        splitter.MinCount = MinCount.Value;
        splitter.MaxCount = MaxCount.Value;
    }
}

// Cuts the ribbon on a time boundary: a fixed cadence, or a gap in emission.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleRibbonTimeSplitter : ParticleModuleBase
{
    // Seconds.
    public readonly Sync<float> Interval = new();

    public readonly Sync<RibbonTimeSplitMode> Mode = new();

    public override void OnInit()
    {
        base.OnInit();
        Interval.Value = 0.25f;
    }

    internal override ParticleSimModule CreateSimModule() => new TimeProximityRibbonSplitter();

    internal override void PushParameters(ParticleSimModule module)
    {
        var splitter = (TimeProximityRibbonSplitter)module;
        splitter.Interval = Interval.Value;
        splitter.Mode = Mode.Value;
    }
}

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

[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailWidthRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinWidth = new();
    public readonly Sync<float> MaxWidth = new();

    public override void OnInit()
    {
        base.OnInit();
        MinWidth.Value = 0.5f;
        MaxWidth.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailWidthRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var width = (TrailWidthRangeInitializer)module;
        width.MinWidth = MinWidth.Value;
        width.MaxWidth = MaxWidth.Value;
    }
}

[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailColorInitializer : ParticleModuleBase
{
    public readonly Sync<colorHDR> Color = new();

    public override void OnInit()
    {
        base.OnInit();
        Color.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailColorConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((TrailColorConstantInitializer)module).Color = Color.Value;
}

[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailColorRangeInitializer : ParticleModuleBase
{
    public readonly Sync<colorHDR> MinColor = new();
    public readonly Sync<colorHDR> MaxColor = new();

    public override void OnInit()
    {
        base.OnInit();
        MinColor.Value = colorHDR.White;
        MaxColor.Value = colorHDR.White;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailColorRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var color = (TrailColorRangeInitializer)module;
        color.MinColor = MinColor.Value;
        color.MaxColor = MaxColor.Value;
    }
}

// Scales how long ONE trail's points survive, against the trail module's MaxPointAge.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailLifetimeInitializer : ParticleModuleBase
{
    public readonly Sync<float> Lifetime = new();

    public override void OnInit()
    {
        base.OnInit();
        Lifetime.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailLifetimeConstantInitializer();

    internal override void PushParameters(ParticleSimModule module)
        => ((TrailLifetimeConstantInitializer)module).Lifetime = Lifetime.Value;
}

[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailLifetimeRangeInitializer : ParticleModuleBase
{
    public readonly Sync<float> MinLifetime = new();
    public readonly Sync<float> MaxLifetime = new();

    public override void OnInit()
    {
        base.OnInit();
        MinLifetime.Value = 0.5f;
        MaxLifetime.Value = 1f;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailLifetimeRangeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var lifetime = (TrailLifetimeRangeInitializer)module;
        lifetime.MinLifetime = MinLifetime.Value;
        lifetime.MaxLifetime = MaxLifetime.Value;
    }
}

// Ties trail length to particle size, so a burst of mixed sizes gets proportionate streaks instead of
// every particle dragging the same tail.
[ComponentCategory("Rendering/Particles")]
public sealed class ParticleTrailLifetimeFromSizeInitializer : ParticleModuleBase
{
    // Particle size that maps to a full-length trail.
    public readonly Sync<float> ReferenceSize = new();

    public readonly Sync<ParticleSizeAxis> Axis = new();

    public override void OnInit()
    {
        base.OnInit();
        ReferenceSize.Value = 1f;
        Axis.Value = ParticleSizeAxis.Average;
    }

    internal override ParticleSimModule CreateSimModule() => new TrailLifetimeFromSizeInitializer();

    internal override void PushParameters(ParticleSimModule module)
    {
        var lifetime = (TrailLifetimeFromSizeInitializer)module;
        lifetime.ReferenceSize = ReferenceSize.Value;
        lifetime.Axis = Axis.Value;
    }
}
