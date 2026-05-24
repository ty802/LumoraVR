// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

public sealed class Button : InteractionElement
{
    public event Action<Button, UIInteractionContext>? Clicked;

    public override void OnAttach()
    {
        base.OnAttach();
        var image = Slot.GetComponent<Image>();
        if (image != null)
        {
            SetupBackgroundColor(image.Tint);
        }
    }

    public ColorDriver SetupBackgroundColor(IField<color> tint)
    {
        return AddColorDriver(tint, tint.Value);
    }

    protected override void OnSubmit(in UIInteractionContext context)
    {
        Clicked?.Invoke(this, context);
    }
}
