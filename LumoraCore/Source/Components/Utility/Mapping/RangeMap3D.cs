// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

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
