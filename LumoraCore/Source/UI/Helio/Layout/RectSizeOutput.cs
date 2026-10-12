// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI.Layout;

// Drives a float2 with the size this slot's rect was laid out at, scaled: a texture sized to the panel it
// fills, a value that follows a box the layout grows. Reads the last computed rect, so a layout change shows
// up the frame after it lands; the drive itself only writes when the size moved.
[ComponentCategory("UI/Helio/Layout")]
[DefaultUpdateOrder(100)]
public class RectSizeOutput : Component
{
    public readonly FieldDrive<float2> Target;

    public readonly Sync<float2> Scale;

    private RectTransform? _rect;

    public RectSizeOutput()
    {
        Target = new FieldDrive<float2>(this) { LocalValueOnly = true };
        Scale = new Sync<float2>(this, float2.One);
    }

    public override void OnUpdate(float delta)
    {
        if (!Target.IsLinkValid)
            return;
        if (_rect == null || _rect.IsDestroyed)
        {
            _rect = Slot.GetComponent<RectTransform>();
            if (_rect == null)
                return;
        }
        var size = _rect.LocalComputeRect.Size;
        var scale = Scale.Value;
        Target.SetValue(new float2(size.x * scale.x, size.y * scale.y));
    }
}
