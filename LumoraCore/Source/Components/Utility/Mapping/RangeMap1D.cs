// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Mapping")]
[DefaultUpdateOrder(-50)]
public class RangeMap1D : RangeMapBase
{
    public readonly Sync<float> TargetMin;

    public readonly Sync<float> TargetMax;

    public readonly HoldingFieldDrive<float> Target;

    // A write that lands on the output is mapped back into the source instead.
    public readonly Sync<bool> ReverseMapping;

    public RangeMap1D()
    {
        TargetMin = new Sync<float>(this, 0f);
        TargetMax = new Sync<float>(this, 1f);
        Target = new HoldingFieldDrive<float>(this) { LocalValueOnly = true };
        ReverseMapping = new Sync<bool>(this, false);
        Target.Holds = () => ReverseMapping.Value && Source.Target != null;
        Target.SetupValueSetHook((_, value) => WriteSource(UnitAlong(TargetMin.Value, TargetMax.Value, value)));
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        float unit = Progress;
        Target.SetValue(TargetMin.Value + (TargetMax.Value - TargetMin.Value) * unit);
    }
}
