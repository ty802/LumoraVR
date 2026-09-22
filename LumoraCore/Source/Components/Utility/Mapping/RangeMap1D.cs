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

    public readonly FieldDrive<float> Target;

    public RangeMap1D()
    {
        TargetMin = new Sync<float>(this, 0f);
        TargetMax = new Sync<float>(this, 1f);
        Target = new FieldDrive<float>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        float unit = Progress;
        Target.SetValue(TargetMin.Value + (TargetMax.Value - TargetMin.Value) * unit);
    }
}
