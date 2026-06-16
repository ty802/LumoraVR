// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;

namespace Helio.UI;

public abstract class UIComponent : Component
{
    public RectTransform? RectTransform { get; internal set; }

    public override void OnAttach()
    {
        base.OnAttach();
        EnsureRectTransform();
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureRectTransform();
    }

    private void EnsureRectTransform()
    {
        if (RectTransform != null) return;

        RectTransform = Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
    }
}
