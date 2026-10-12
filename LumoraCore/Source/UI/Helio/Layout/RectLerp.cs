// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

// Blends this rect toward another one once both are placed: at 0 it sits where its anchors or its parent's
// layout put it, at 1 exactly over the other rect, in between on the straight line. A label sliding out of view
// behind a mask, a highlight moving onto the picked tab.
//
// It moves where the rect draws and where its children are placed, nothing else: the parent's layout still
// sizes the rect and its siblings from its own metrics, so it stays in its layout, and any rect on the canvas
// can be the target. The canvas asks for the blend each time it places the rect (RectTransform.SetBlend). A
// target that moved on a pass of its own after the blend was worked out marks this rect, so it follows. -xlinka
[ComponentCategory("UI/Helio/Layout")]
public class RectLerp : UIComputeComponent
{
    public readonly Sync<float> Lerp;

    public readonly SyncRef<RectTransform> Source;

    // The target as it stood when the blend was last worked out.
    private Rect _usedTarget;
    private bool _blending;

    public RectLerp()
    {
        Lerp = new Sync<float>(this, 0f);
        Source = new SyncRef<RectTransform>(this);
    }

    public override void OnAwake()
    {
        base.OnAwake();
        RectTransform?.SetBlend(this);
    }

    public override void OnStart()
    {
        base.OnStart();
        RectTransform?.SetBlend(this);
    }

    public override void OnDestroying()
    {
        RectTransform?.ClearBlend(this);
        base.OnDestroying();
    }

    protected override void FlagChanges(RectTransform rect) => rect.MarkChangeDirty();

    public override void PrepareCompute()
    {
    }

    // Main thread, inside the layout pass: where the rect goes, given where it was placed.
    internal Rect Blend(in Rect placed)
    {
        var target = Source.Target;
        float t = Lerp.Value;
        if (!Enabled.Value || target == null || target.IsDestroyed || ReferenceEquals(target, RectTransform) || t == 0f)
        {
            _blending = false;
            return placed;
        }
        var to = target.LocalComputeRect;
        _usedTarget = to;
        _blending = true;
        return Rect.FromMinMax(
            new float2(placed.xMin + (to.xMin - placed.xMin) * t, placed.yMin + (to.yMin - placed.yMin) * t),
            new float2(placed.xMax + (to.xMax - placed.xMax) * t, placed.yMax + (to.yMax - placed.yMax) * t));
    }

    internal bool TargetMoved => _blending && Source.Target is { IsDestroyed: false } target && !target.LocalComputeRect.Equals(_usedTarget);

    public override void OnUpdate(float delta)
    {
        base.OnUpdate(delta);
        if (TargetMoved)
            RectTransform?.MarkChangeDirty();
    }
}
