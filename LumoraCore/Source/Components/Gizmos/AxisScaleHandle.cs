// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;
using Lumora.Core.Networking.Sync;

namespace Lumora.Core.Components.Gizmos;

// dragging along the handle's axis stretches ONE component of the target's local scale by the distance
// ratio from the gizmo center (pull the X cube twice as far out = X scale doubles)
[ComponentCategory("Hidden")]
public class AxisScaleHandle : TransformHandle
{
    // 0=x, 1=y, 2=z
    public readonly Sync<int> ScaleAxisIndex;

    private const float MinFactor = 0.02f;
    private const float MaxFactor = 50f;
    private const float MinStartParam = 0.02f;

    private float3 _axis;
    private float3 _lineOrigin;
    private float3 _startScale;
    private float _startParam;
    private bool _valid;

    public AxisScaleHandle()
    {
        ScaleAxisIndex = new Sync<int>(this, 0);
    }

    protected override Localization.LocaleText DragDescription => UndoLocale.Scale;

    protected override void BeginDrag(Slot target, float3 rayOrigin, float3 rayDirection)
    {
        _axis = AxisWorld;
        _lineOrigin = CenterWorld;
        _startScale = target.LocalScale.Value;
        float param = ClosestLineParam(_lineOrigin, _axis, rayOrigin, rayDirection);
        // The grab starts ON the handle, which sits out along the axis - the param can't be ~0 unless
        // the beam is degenerate; floor it so the ratio can't explode.
        _startParam = float.IsNaN(param) ? float.NaN : MathF.Max(MathF.Abs(param), MinStartParam);
        _valid = !float.IsNaN(_startParam);
    }

    protected override void UpdateDrag(Slot target, float3 rayOrigin, float3 rayDirection)
    {
        if (!_valid)
            return;
        float param = ClosestLineParam(_lineOrigin, _axis, rayOrigin, rayDirection);
        if (float.IsNaN(param))
            return;
        float factor = MathF.Abs(param) / _startParam;
        if (factor < MinFactor) factor = MinFactor;
        if (factor > MaxFactor) factor = MaxFactor;

        var scale = _startScale;
        switch (ScaleAxisIndex.Value)
        {
            case 0: scale.x = _startScale.x * factor; break;
            case 1: scale.y = _startScale.y * factor; break;
            default: scale.z = _startScale.z * factor; break;
        }
        target.LocalScale.Value = scale;
    }
}
