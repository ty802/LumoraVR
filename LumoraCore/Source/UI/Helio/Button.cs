// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Helio.UI;

public sealed class Button : InteractionElement
{
    public event Action<Button, UIInteractionContext>? Clicked;

    protected override void OnSubmit(in UIInteractionContext context)
    {
        Clicked?.Invoke(this, context);
    }
}
