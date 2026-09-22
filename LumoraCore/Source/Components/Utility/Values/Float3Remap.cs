// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

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
