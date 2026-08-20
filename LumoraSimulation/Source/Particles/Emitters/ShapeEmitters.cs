// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Simulation.Particles.Emitters;

// Every particle from one point. The cheapest emitter, and the one most effects start as.
public sealed class PointEmitter : ParticleSimEmitter
{
    public float3 Position;
    public float3 Direction = float3.Up;
    public float RandomDirectionWeight;
    public colorHDR Color = colorHDR.White;

    public override bool InitializesRotations => false;
    public override bool InitializesLifetimes => false;
    public override bool InitializesDirections => true;
    public override bool InitializesColors => Color != colorHDR.White;
    public override bool InitializesSizes => false;

    public override int Emit(int count, Span<float3> positions, Span<floatQ> rotations, Span<float3> directions,
        Span<colorHDR> colors, Span<float3> sizes, Span<float> lifetimes)
    {
        positions.Fill(Position);
        directions.Fill(Direction);
        DirectionTransformHelper.ApplyRandomSpread(directions, Simulation.Random, RandomDirectionWeight);
        if (InitializesColors)
            colors.Fill(Color);
        return count;
    }
}

// From inside a box, or from its shell. Shell emission picks a face first and a point on it second,
// which keeps the distribution even across faces of different sizes only when the box is a cube - for
// a stretched box the big faces are under-sampled, and that is the honest trade for one draw. -xlinka
public sealed class BoxEmitter : TransformableSimEmitter
{
    public float3 Size = float3.One;
    public bool EmitFromShell;
    public BoxEmitterDirection DirectionMode;
    public float3 Direction = float3.Up;
    public DirectionTransformMode DirectionTransformMode = DirectionTransformMode.AsVector;
    public float RandomDirectionWeight;

    public override bool InitializesRotations => false;
    public override bool InitializesLifetimes => false;
    public override bool InitializesDirections => true;
    public override bool InitializesColors => false;
    public override bool InitializesSizes => false;

    public override int Emit(int count, Span<float3> positions, Span<floatQ> rotations, Span<float3> directions,
        Span<colorHDR> colors, Span<float3> sizes, Span<float> lifetimes)
    {
        var random = Simulation.Random;
        var fixedDirection = DirectionTransformHelper.Transform(in Transform, in Direction, DirectionTransformMode);

        for (int i = 0; i < count; i++)
        {
            float3 unitPoint;
            float3 faceNormal;
            if (EmitFromShell)
            {
                random.OnUnitCubeWithNormal(out unitPoint, out faceNormal);
            }
            else
            {
                unitPoint = random.InsideUnitCube;
                var absolute = new float3(MathF.Abs(unitPoint.x), MathF.Abs(unitPoint.y), MathF.Abs(unitPoint.z));
                faceNormal = absolute.x >= absolute.y && absolute.x >= absolute.z
                    ? new float3(MathF.Sign(unitPoint.x), 0f, 0f)
                    : absolute.y >= absolute.z
                        ? new float3(0f, MathF.Sign(unitPoint.y), 0f)
                        : new float3(0f, 0f, MathF.Sign(unitPoint.z));
            }

            positions[i] = TransformPoint(unitPoint * Size);
            directions[i] = DirectionMode == BoxEmitterDirection.ClosestFaceNormal
                ? TransformVector(faceNormal)
                : fixedDirection;
        }
        DirectionTransformHelper.ApplyRandomSpread(directions, random, RandomDirectionWeight);
        return count;
    }
}

// From inside a sphere, or from its surface. The bread-and-butter burst emitter.
public sealed class SphereEmitter : TransformableSimEmitter
{
    public float Radius = 0.5f;
    public bool EmitFromShell;
    public SphereEmitterDirection DirectionMode = SphereEmitterDirection.RadialUniform;
    public float3 ForcedDirection = float3.Up;
    public DirectionTransformMode DirectionTransformMode = DirectionTransformMode.AsVector;

    // Shifts what "outward" means, so a sphere can spray off to one side.
    public float3 DirectionReferencePoint;

    public float RandomDirectionWeight;

    public override bool InitializesRotations => false;
    public override bool InitializesLifetimes => false;
    public override bool InitializesDirections => true;
    public override bool InitializesColors => false;
    public override bool InitializesSizes => false;

    public override int Emit(int count, Span<float3> positions, Span<floatQ> rotations, Span<float3> directions,
        Span<colorHDR> colors, Span<float3> sizes, Span<float> lifetimes)
    {
        var random = Simulation.Random;
        var forced = DirectionMode == SphereEmitterDirection.Forced
            ? DirectionTransformHelper.Transform(in Transform, in ForcedDirection, DirectionTransformMode)
            : float3.Zero;

        for (int i = 0; i < count; i++)
        {
            var unitPoint = EmitFromShell ? random.OnUnitSphere : random.InsideUnitSphere;
            positions[i] = TransformPoint(unitPoint * Radius);
            directions[i] = DirectionMode switch
            {
                SphereEmitterDirection.Forced => forced,
                SphereEmitterDirection.RadialProportional => TransformVector(unitPoint + DirectionReferencePoint),
                _ => TransformVector((unitPoint + DirectionReferencePoint).Normalized),
            };
        }
        DirectionTransformHelper.ApplyRandomSpread(directions, random, RandomDirectionWeight);
        return count;
    }
}

// A Y-up cone, apex down. Volume emission spreads through the whole solid; shell emission rides the
// lateral surface. Radial direction follows the flare, which is what makes a cone look like a jet
// rather than a cylinder of particles.
public sealed class ConeEmitter : TransformableSimEmitter
{
    public float BaseRadius = 0.5f;
    public float Height = 1f;
    public bool EmitFromShell;
    public ConeEmitterDirection DirectionMode = ConeEmitterDirection.RadialUniform;
    public float3 Direction = float3.Up;
    public DirectionTransformMode DirectionTransformMode = DirectionTransformMode.AsVector;

    // Where the flare is measured from, as a fraction of the cone's own extents.
    public float3 RelativeDirectionReferencePoint = float3.Up;

    public float RandomDirectionWeight;

    public override bool InitializesRotations => false;
    public override bool InitializesLifetimes => false;
    public override bool InitializesDirections => true;
    public override bool InitializesColors => false;
    public override bool InitializesSizes => false;

    public override int Emit(int count, Span<float3> positions, Span<floatQ> rotations, Span<float3> directions,
        Span<colorHDR> colors, Span<float3> sizes, Span<float> lifetimes)
    {
        var random = Simulation.Random;
        var fixedDirection = DirectionTransformHelper.Transform(in Transform, in Direction, DirectionTransformMode);
        var reference = RelativeDirectionReferencePoint * new float3(BaseRadius, Height * 0.5f, BaseRadius);

        for (int i = 0; i < count; i++)
        {
            var point = EmitFromShell ? random.OnCone(Height, BaseRadius) : random.InsideCone(Height, BaseRadius);
            positions[i] = TransformPoint(point);
            if (DirectionMode == ConeEmitterDirection.RadialUniform)
            {
                var flare = (point + reference).Normalized;
                directions[i] = DirectionTransformHelper.Transform(in Transform, in flare, DirectionTransformMode);
            }
            else
            {
                directions[i] = fixedDirection;
            }
        }
        DirectionTransformHelper.ApplyRandomSpread(directions, random, RandomDirectionWeight);
        return count;
    }
}

// A flat disc or ring in a chosen plane. Shockwaves, portal rims, ground-level swirls.
public sealed class CircleEmitter : TransformableSimEmitter
{
    public float Radius = 1f;

    // Per-axis scale of the disc, so it can be an ellipse.
    public float2 Scale = float2.One;

    public bool EmitFromShell;
    public CircleEmitterAlignment Alignment = CircleEmitterAlignment.XZ;
    public CircleEmitterDirection DirectionMode = CircleEmitterDirection.RadialUniform;
    public float3 Direction = float3.Up;
    public DirectionTransformMode DirectionTransformMode = DirectionTransformMode.AsVector;
    public float RandomDirectionWeight;

    public override bool InitializesRotations => false;
    public override bool InitializesLifetimes => false;
    public override bool InitializesDirections => true;
    public override bool InitializesColors => false;
    public override bool InitializesSizes => false;

    public override int Emit(int count, Span<float3> positions, Span<floatQ> rotations, Span<float3> directions,
        Span<colorHDR> colors, Span<float3> sizes, Span<float> lifetimes)
    {
        var random = Simulation.Random;
        var fixedDirection = DirectionTransformHelper.Transform(in Transform, in Direction, DirectionTransformMode);

        for (int i = 0; i < count; i++)
        {
            float2 unit = EmitFromShell ? random.OnUnitCircle : random.InsideUnitCircle;
            float2 outward = EmitFromShell ? unit : unit.Normalized;
            positions[i] = TransformPoint(ToPlane(unit * Radius * Scale));
            directions[i] = DirectionMode switch
            {
                CircleEmitterDirection.Fixed => fixedDirection,
                CircleEmitterDirection.FixedProportional => fixedDirection * unit.Length,
                CircleEmitterDirection.FixedReversedProportional => fixedDirection * (1f - unit.Length),
                CircleEmitterDirection.RadialProportional => TransformVector(ToPlane(unit)),
                _ => TransformVector(ToPlane(outward)),
            };
        }
        DirectionTransformHelper.ApplyRandomSpread(directions, random, RandomDirectionWeight);
        return count;
    }

    private float3 ToPlane(in float2 point) => Alignment switch
    {
        CircleEmitterAlignment.XY => new float3(point.x, point.y, 0f),
        CircleEmitterAlignment.YZ => new float3(0f, point.x, point.y),
        _ => new float3(point.x, 0f, point.y),
    };
}

// A Y-up cylinder, solid or shell. Shell emission splits between the caps and the side wall by their
// relative AREA, so a tall thin cylinder puts nearly everything on the wall and a flat one nearly
// everything on the caps, which is what you would get from sampling the real surface.
public sealed class CylinderEmitter : TransformableSimEmitter
{
    public float Radius = 0.5f;
    public float Height = 1f;
    public bool EmitFromShell;

    // Shell emission only: skip the flat ends and use the side wall alone.
    public bool ExcludeCaps;

    public CylinderEmitterDirection DirectionMode = CylinderEmitterDirection.CircleUniform;
    public CylinderEmitterCapsDirection CapsDirectionMode = CylinderEmitterCapsDirection.Facing;
    public float3 Direction = float3.Up;
    public DirectionTransformMode DirectionTransformMode = DirectionTransformMode.AsVector;
    public float RandomDirectionWeight;

    public override bool InitializesRotations => false;
    public override bool InitializesLifetimes => false;
    public override bool InitializesDirections => true;
    public override bool InitializesColors => false;
    public override bool InitializesSizes => false;

    public override int Emit(int count, Span<float3> positions, Span<floatQ> rotations, Span<float3> directions,
        Span<colorHDR> colors, Span<float3> sizes, Span<float> lifetimes)
    {
        var random = Simulation.Random;
        var fixedDirection = DirectionTransformHelper.Transform(in Transform, in Direction, DirectionTransformMode);
        float halfHeight = Height * 0.5f;

        float capShare = 0f;
        float bothCapsShare = 0f;
        if (EmitFromShell && !ExcludeCaps)
        {
            float capArea = MathF.PI * Radius * Radius;
            float wallArea = MathF.PI * 2f * Radius * Height;
            float total = capArea * 2f + wallArea;
            capShare = total > 1e-6f ? capArea / total : 0f;
            bothCapsShare = capShare * 2f;
        }

        for (int i = 0; i < count; i++)
        {
            float3 point;
            float3 direction;

            if (EmitFromShell && !ExcludeCaps)
            {
                float pick = random.Value;
                if (pick <= bothCapsShare)
                {
                    float sign = pick <= capShare ? 1f : -1f;
                    var disc = random.InsideUnitCircle * Radius;
                    point = new float3(disc.x, halfHeight * sign, disc.y);
                    direction = CapDirection(disc, new float3(0f, sign, 0f), fixedDirection);
                }
                else
                {
                    var ring = random.OnUnitCircle;
                    point = new float3(ring.x * Radius, random.Range(-halfHeight, halfHeight), ring.y * Radius);
                    direction = WallDirection(ring, point, fixedDirection);
                }
            }
            else if (EmitFromShell)
            {
                var ring = random.OnUnitCircle;
                point = new float3(ring.x * Radius, random.Range(-halfHeight, halfHeight), ring.y * Radius);
                direction = WallDirection(ring, point, fixedDirection);
            }
            else
            {
                var disc = random.InsideUnitCircle * Radius;
                point = new float3(disc.x, random.Range(-halfHeight, halfHeight), disc.y);
                direction = WallDirection(disc, point, fixedDirection);
            }

            positions[i] = TransformPoint(point);
            directions[i] = direction;
        }
        DirectionTransformHelper.ApplyRandomSpread(directions, random, RandomDirectionWeight);
        return count;
    }

    private float3 WallDirection(in float2 disc, in float3 point, in float3 fixedDirection) => DirectionMode switch
    {
        CylinderEmitterDirection.Fixed => fixedDirection,
        CylinderEmitterDirection.RadialUniform => TransformVector(point.Normalized),
        _ => TransformVector(new float3(disc.Normalized.x, 0f, disc.Normalized.y)),
    };

    private float3 CapDirection(in float2 disc, in float3 normal, in float3 fixedDirection) => CapsDirectionMode switch
    {
        CylinderEmitterCapsDirection.Fixed => fixedDirection,
        CylinderEmitterCapsDirection.RadialUniform => TransformVector(new float3(disc.Normalized.x, 0f, disc.Normalized.y)),
        _ => TransformVector(normal),
    };
}

// Along a segment between two points, with independent colours and directions at each end that are
// interpolated along it. Beams, trails behind a swung object, edge highlights.
public sealed class LineEmitter : TransformableSimEmitter
{
    public float3 Point0 = float3.Left;
    public float3 Point1 = float3.Right;
    public colorHDR Color0 = colorHDR.White;
    public colorHDR Color1 = colorHDR.White;
    public LineEmitterDirection DirectionMode = LineEmitterDirection.Fixed;
    public float3 Direction0 = float3.Up;
    public float3 Direction1 = float3.Up;
    public float3 UpDirection = float3.Up;
    public float RandomDirectionWeight;

    public override bool InitializesRotations => false;
    public override bool InitializesLifetimes => false;
    public override bool InitializesDirections => true;
    public override bool InitializesColors => Color0 != colorHDR.White || Color1 != colorHDR.White;
    public override bool InitializesSizes => false;

    public override int Emit(int count, Span<float3> positions, Span<floatQ> rotations, Span<float3> directions,
        Span<colorHDR> colors, Span<float3> sizes, Span<float> lifetimes)
    {
        var random = Simulation.Random;
        var span = Point1 - Point0;
        var lineRotation = DirectionMode == LineEmitterDirection.LineAligned
            ? ParticleMath.FromForwardUp(span, UpDirection)
            : floatQ.Identity;
        bool initColors = InitializesColors;

        for (int i = 0; i < count; i++)
        {
            float t = random.Value;
            positions[i] = TransformPoint(Point0 + span * t);
            var direction = Direction0 + (Direction1 - Direction0) * t;
            if (DirectionMode == LineEmitterDirection.LineAligned)
                direction = lineRotation * direction;
            directions[i] = TransformVector(direction);
            if (initColors)
                colors[i] = colorHDR.Lerp(Color0, Color1, t);
        }
        DirectionTransformHelper.ApplyRandomSpread(directions, random, RandomDirectionWeight);
        return count;
    }
}

// A flat disc that aims its particles inside a cone around the disc's axis. Distinct from
// ConeEmitter, which samples the cone as a SOLID: this one puts every particle on the base and only
// the DIRECTIONS flare. That is the nozzle/torch shape - a thin ring of origin with a wide spray -
// and sampling a solid cone cannot produce it at any parameters. -xlinka
public sealed class ConeSprayEmitter : TransformableSimEmitter
{
    public float Radius = 0.1f;

    // Cone half-angle in radians. 0 fires straight along the axis.
    public float Angle = 0.35f;

    // Axis the cone opens along, in shape space.
    public float3 Axis = float3.Up;

    public override bool InitializesRotations => false;
    public override bool InitializesLifetimes => false;
    public override bool InitializesDirections => true;
    public override bool InitializesColors => false;
    public override bool InitializesSizes => false;

    public override int Emit(int count, Span<float3> positions, Span<floatQ> rotations, Span<float3> directions,
        Span<colorHDR> colors, Span<float3> sizes, Span<float> lifetimes)
    {
        var random = Simulation.Random;
        float half = System.Math.Clamp(Angle, 0f, MathF.PI * 0.5f - 1e-3f);

        // Build a basis around the axis once: the cone is described in it and rotated out per particle.
        var axis = Axis.Normalized;
        var reference = MathF.Abs(axis.y) > 0.9f ? float3.Forward : float3.Up;
        var right = float3.Cross(reference, axis).Normalized;
        var forward = float3.Cross(axis, right);

        for (int i = 0; i < count; i++)
        {
            var disc = random.InsideUnitCircle * Radius;
            positions[i] = TransformPoint(right * disc.x + forward * disc.y);

            float tilt = random.Value * half;
            float around = random.Value * MathF.PI * 2f;
            float sin = MathF.Sin(tilt);
            var local = right * (MathF.Cos(around) * sin)
                + axis * MathF.Cos(tilt)
                + forward * (MathF.Sin(around) * sin);
            directions[i] = TransformVector(local);
        }
        return count;
    }
}
