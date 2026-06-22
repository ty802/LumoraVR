// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Helio.UI;

public sealed class Mask : UIComputeComponent
{
    public readonly Sync<bool> ShowMaskGraphic;

    // Opt into TRUE GPU stencil masking: the mask's SHAPE is stamped into the stencil buffer and content is
    // clipped to that exact shape (circle / rounded / any geometry), not just the axis-aligned rect. Default
    // false keeps the cheap rectangular clip. Honored only when Canvas.StencilMaskingEnabled is also on. -xlinka
    public readonly Sync<bool> StencilMasking;

    public Mask()
    {
        ShowMaskGraphic = new Sync<bool>(this, false);
        StencilMasking = new Sync<bool>(this, false);
    }

    public override void OnAttach()
    {
        base.OnAttach();
        if (Slot.GetComponent<Graphic>() == null)
        {
            Slot.AttachComponent<Image>();
        }
    }

    protected override void FlagChanges(RectTransform rect)
    {
        rect.MarkGraphicDirty();
    }

    public override void PrepareCompute()
    {
    }
}
