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

    public readonly HoldingFieldDrive<float3> Target;

    // A write that lands on the output is mapped back into the source instead.
    public readonly Sync<bool> ReverseMapping;

    public RangeMap3D()
    {
        TargetMin = new Sync<float3>(this, float3.Zero);
        TargetMax = new Sync<float3>(this, float3.One);
        Target = new HoldingFieldDrive<float3>(this) { LocalValueOnly = true };
        ReverseMapping = new Sync<bool>(this, false);
        Target.Holds = () => ReverseMapping.Value && Source.Target != null;
        Target.SetupValueSetHook((_, value) => WriteSource(UnitAlong(TargetMin.Value, TargetMax.Value, value)));
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        var min = TargetMin.Value;
        Target.SetValue(min + (TargetMax.Value - min) * Progress);
    }
}
