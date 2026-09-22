// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

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
