// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

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
