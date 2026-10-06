// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Drives a float2 from two picked channels of a float3: the width and height of a box onto the size of a
// quad. Picking W reads zero, as there is none.
[ComponentCategory("Utility/Values")]
[DefaultUpdateOrder(-100)]
public class Float3ToFloat2Remap : Component
{
    public readonly SyncRef<IField<float3>> Source;

    public readonly Sync<RemapChannel> X;

    public readonly Sync<RemapChannel> Y;

    public readonly FieldDrive<float2> Target;

    public Float3ToFloat2Remap()
    {
        Source = new SyncRef<IField<float3>>(this);
        X = new Sync<RemapChannel>(this, RemapChannel.X);
        Y = new Sync<RemapChannel>(this, RemapChannel.Y);
        Target = new FieldDrive<float2>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        var source = Source.Target;
        var value = source != null ? source.Value : float3.Zero;
        Target.SetValue(new float2(
            RemapChannels.Pick(X.Value, value.x, value.y, value.z, 0f),
            RemapChannels.Pick(Y.Value, value.x, value.y, value.z, 0f)));
    }
}
