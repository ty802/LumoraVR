// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Helio.UI;

public sealed class Mask : UIComputeComponent
{
    public readonly Sync<bool> ShowMaskGraphic;

    public Mask()
    {
        ShowMaskGraphic = new Sync<bool>(this, false);
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
