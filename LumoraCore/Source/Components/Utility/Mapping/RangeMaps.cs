// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Shared base for the components that map one float input range onto an output range.
//
// One float in, any shape out. A per-component input would need a separate component per output type
// AND per input type, and the thing people actually wire up is a single number (a slider, a distance,
// a progress) opening onto a position, a colour or a rotation. -xlinka
public abstract class RangeMapBase : Component
{
    public readonly SyncRef<IField<float>> Source;

    // Input value that maps to the output minimum.
    public readonly Sync<float> SourceMin;

    // Input value that maps to the output maximum.
    public readonly Sync<float> SourceMax;

    // Hold the output at its ends when the input leaves the source range.
    public readonly Sync<bool> Clamp;

    protected RangeMapBase()
    {
        Source = new SyncRef<IField<float>>(this);
        SourceMin = new Sync<float>(this, 0f);
        SourceMax = new Sync<float>(this, 1f);
        Clamp = new Sync<bool>(this, true);
    }

    // 0 at the minimum and 1 at the maximum.
    protected float Progress
    {
        get
        {
            var source = Source.Target;
            float value = source?.Value ?? 0f;
            float span = SourceMax.Value - SourceMin.Value;
            // A zero-width source range has no position to report. Sit at the start rather than
            // dividing by zero and driving the target to NaN, which propagates into transforms and is
            // very hard to trace back here.
            float unit = span == 0f ? 0f : (value - SourceMin.Value) / span;
            return Clamp.Value ? LuminaMath.Clamp(unit, 0f, 1f) : unit;
        }
    }
}

[ComponentCategory("Utility/Mapping")]
[DefaultUpdateOrder(-50)]
public class RangeMap1D : RangeMapBase
{
    public readonly Sync<float> TargetMin;

    public readonly Sync<float> TargetMax;

    public readonly FieldDrive<float> Target;

    public RangeMap1D()
    {
        TargetMin = new Sync<float>(this, 0f);
        TargetMax = new Sync<float>(this, 1f);
        Target = new FieldDrive<float>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        float unit = Progress;
        Target.SetValue(TargetMin.Value + (TargetMax.Value - TargetMin.Value) * unit);
    }
}

[ComponentCategory("Utility/Mapping")]
[DefaultUpdateOrder(-50)]
public class RangeMap2D : RangeMapBase
{
    public readonly Sync<float2> TargetMin;

    public readonly Sync<float2> TargetMax;

    public readonly FieldDrive<float2> Target;

    public RangeMap2D()
    {
        TargetMin = new Sync<float2>(this, float2.Zero);
        TargetMax = new Sync<float2>(this, float2.One);
        Target = new FieldDrive<float2>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        var min = TargetMin.Value;
        Target.SetValue(min + (TargetMax.Value - min) * Progress);
    }
}

[ComponentCategory("Utility/Mapping")]
[DefaultUpdateOrder(-50)]
public class RangeMap3D : RangeMapBase
{
    public readonly Sync<float3> TargetMin;

    public readonly Sync<float3> TargetMax;

    public readonly FieldDrive<float3> Target;

    public RangeMap3D()
    {
        TargetMin = new Sync<float3>(this, float3.Zero);
        TargetMax = new Sync<float3>(this, float3.One);
        Target = new FieldDrive<float3>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        var min = TargetMin.Value;
        Target.SetValue(min + (TargetMax.Value - min) * Progress);
    }
}

[ComponentCategory("Utility/Mapping")]
[DefaultUpdateOrder(-50)]
public class RangeMap4D : RangeMapBase
{
    public readonly Sync<float4> TargetMin;

    public readonly Sync<float4> TargetMax;

    public readonly FieldDrive<float4> Target;

    public RangeMap4D()
    {
        TargetMin = new Sync<float4>(this, float4.Zero);
        TargetMax = new Sync<float4>(this, float4.One);
        Target = new FieldDrive<float4>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        var min = TargetMin.Value;
        Target.SetValue(min + (TargetMax.Value - min) * Progress);
    }
}

[ComponentCategory("Utility/Mapping")]
[DefaultUpdateOrder(-50)]
public class ColorRangeMap : RangeMapBase
{
    public readonly Sync<color> TargetMin;

    public readonly Sync<color> TargetMax;

    public readonly FieldDrive<color> Target;

    public ColorRangeMap()
    {
        TargetMin = new Sync<color>(this, color.Black);
        TargetMax = new Sync<color>(this, color.White);
        Target = new FieldDrive<color>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        Target.SetValue(color.Lerp(TargetMin.Value, TargetMax.Value, Progress));
    }
}

// Slerp, not a component blend: blending quaternion components takes the chord through the sphere
// rather than the arc across it, so the rotation speeds up in the middle of the sweep and the result
// is not unit length. -xlinka
[ComponentCategory("Utility/Mapping")]
[DefaultUpdateOrder(-50)]
public class RotationRangeMap : RangeMapBase
{
    public readonly Sync<floatQ> TargetMin;

    public readonly Sync<floatQ> TargetMax;

    public readonly FieldDrive<floatQ> Target;

    public RotationRangeMap()
    {
        TargetMin = new Sync<floatQ>(this, floatQ.Identity);
        TargetMax = new Sync<floatQ>(this, floatQ.AxisAngle(float3.Up, LuminaMath.PI * 0.5f));
        Target = new FieldDrive<floatQ>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        Target.SetValue(floatQ.Slerp(TargetMin.Value, TargetMax.Value, Progress));
    }
}
