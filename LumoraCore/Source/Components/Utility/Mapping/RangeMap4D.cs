// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

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
