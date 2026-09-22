// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

[ComponentCategory("Utility/Mapping")]
[DefaultUpdateOrder(-50)]
public class ColorRangeMap : RangeMapBase
{
    public readonly Sync<color> TargetMin;

    public readonly Sync<color> TargetMax;

    public readonly FieldDrive<color> Target;

    public ColorRangeMap()
    {
        TargetMin = new Sync<color>(this, color.Black);
        TargetMax = new Sync<color>(this, color.White);
        Target = new FieldDrive<color>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        Target.SetValue(color.Lerp(TargetMin.Value, TargetMax.Value, Progress));
    }
}
