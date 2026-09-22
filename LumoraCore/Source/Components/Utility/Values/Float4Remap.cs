// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

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
