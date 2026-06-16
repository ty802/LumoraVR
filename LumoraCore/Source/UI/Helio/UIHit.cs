// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Helio.UI;

public readonly struct UIHit
{
    public readonly IUIInteractable Interactable;
    public readonly UIInteractionContext Context;

    public UIHit(IUIInteractable interactable, in UIInteractionContext context)
    {
        Interactable = interactable;
        Context = context;
    }
}
