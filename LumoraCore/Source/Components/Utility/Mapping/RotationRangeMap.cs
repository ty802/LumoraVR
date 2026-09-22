// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Components.Utility;

// Slerp, not a component blend: blending quaternion components takes the chord through the sphere
// rather than the arc across it, so the rotation speeds up in the middle of the sweep and the result
// is not unit length. -xlinka
[ComponentCategory("Utility/Mapping")]
[DefaultUpdateOrder(-50)]
public class RotationRangeMap : RangeMapBase
{
    public readonly Sync<floatQ> TargetMin;

    public readonly Sync<floatQ> TargetMax;

    public readonly FieldDrive<floatQ> Target;

    public RotationRangeMap()
    {
        TargetMin = new Sync<floatQ>(this, floatQ.Identity);
        TargetMax = new Sync<floatQ>(this, floatQ.AxisAngle(float3.Up, LuminaMath.PI * 0.5f));
        Target = new FieldDrive<floatQ>(this) { LocalValueOnly = true };
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        Target.SetValue(floatQ.Slerp(TargetMin.Value, TargetMax.Value, Progress));
    }
}
