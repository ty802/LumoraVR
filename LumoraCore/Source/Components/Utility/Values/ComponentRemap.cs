// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// What one output channel of a remap reads.
public enum RemapChannel
{
    X,
    Y,
    Z,
    W,
    NegativeX,
    NegativeY,
    NegativeZ,
    NegativeW,
    Zero,
    One,
}

public static class RemapChannels
{
    public static float Pick(RemapChannel channel, float x, float y, float z, float w) => channel switch
    {
        RemapChannel.X => x,
        RemapChannel.Y => y,
        RemapChannel.Z => z,
        RemapChannel.W => w,
        RemapChannel.NegativeX => -x,
        RemapChannel.NegativeY => -y,
        RemapChannel.NegativeZ => -z,
        RemapChannel.NegativeW => -w,
        RemapChannel.One => 1f,
        _ => 0f,
    };
}

// A three-component source has no W, so picking W (or its negation) reads zero rather than being
// silently rejected. -xlinka
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class Float3Remap : Component
{
    public readonly SyncRef<IField<float3>> Source;

    public readonly Sync<RemapChannel> X;

    public readonly Sync<RemapChannel> Y;

    public readonly Sync<RemapChannel> Z;

    public readonly FieldDrive<float3> Target;

    public Float3Remap()
    {
        Source = new SyncRef<IField<float3>>(this);
        X = new Sync<RemapChannel>(this, RemapChannel.X);
        Y = new Sync<RemapChannel>(this, RemapChannel.Y);
        Z = new Sync<RemapChannel>(this, RemapChannel.Z);
        Target = new FieldDrive<float3>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        var source = Source.Target;
        var value = source != null ? source.Value : float3.Zero;
        Target.SetValue(new float3(
            RemapChannels.Pick(X.Value, value.x, value.y, value.z, 0f),
            RemapChannels.Pick(Y.Value, value.x, value.y, value.z, 0f),
            RemapChannels.Pick(Z.Value, value.x, value.y, value.z, 0f)));
    }
}

[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class Float4Remap : Component
{
    public readonly SyncRef<IField<float4>> Source;

    public readonly Sync<RemapChannel> X;

    public readonly Sync<RemapChannel> Y;

    public readonly Sync<RemapChannel> Z;

    public readonly Sync<RemapChannel> W;

    public readonly FieldDrive<float4> Target;

    public Float4Remap()
    {
        Source = new SyncRef<IField<float4>>(this);
        X = new Sync<RemapChannel>(this, RemapChannel.X);
        Y = new Sync<RemapChannel>(this, RemapChannel.Y);
        Z = new Sync<RemapChannel>(this, RemapChannel.Z);
        W = new Sync<RemapChannel>(this, RemapChannel.W);
        Target = new FieldDrive<float4>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        var source = Source.Target;
        var v = source != null ? source.Value : new float4(0f, 0f, 0f, 0f);
        Target.SetValue(new float4(
            RemapChannels.Pick(X.Value, v.x, v.y, v.z, v.w),
            RemapChannels.Pick(Y.Value, v.x, v.y, v.z, v.w),
            RemapChannels.Pick(Z.Value, v.x, v.y, v.z, v.w),
            RemapChannels.Pick(W.Value, v.x, v.y, v.z, v.w)));
    }
}

// RGBA map onto XYZW.
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class ColorRemap : Component
{
    public readonly SyncRef<IField<color>> Source;

    public readonly Sync<RemapChannel> R;

    public readonly Sync<RemapChannel> G;

    public readonly Sync<RemapChannel> B;

    public readonly Sync<RemapChannel> A;

    public readonly FieldDrive<color> Target;

    public ColorRemap()
    {
        Source = new SyncRef<IField<color>>(this);
        R = new Sync<RemapChannel>(this, RemapChannel.X);
        G = new Sync<RemapChannel>(this, RemapChannel.Y);
        B = new Sync<RemapChannel>(this, RemapChannel.Z);
        A = new Sync<RemapChannel>(this, RemapChannel.W);
        Target = new FieldDrive<color>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        var source = Source.Target;
        var c = source != null ? source.Value : color.White;
        Target.SetValue(new color(
            RemapChannels.Pick(R.Value, c.r, c.g, c.b, c.a),
            RemapChannels.Pick(G.Value, c.r, c.g, c.b, c.a),
            RemapChannels.Pick(B.Value, c.r, c.g, c.b, c.a),
            RemapChannels.Pick(A.Value, c.r, c.g, c.b, c.a)));
    }
}
