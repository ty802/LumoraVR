// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

public class Button : InteractionElement
{
    // Duplicable click action: a method on a world element, stored as target +
    // method name so it survives Slot.Duplicate (the clone's button calls the
    // clone's handler). Closures can't be referenced this way - they use Clicked.
    public readonly SyncDelegate<Action<Button, UIInteractionContext>> PressAction;

    public event Action<Button, UIInteractionContext>? Clicked;

    public Button()
    {
        PressAction = new SyncDelegate<Action<Button, UIInteractionContext>>(this);
    }

    // Bind a click action. A method on a world element (a component) is stored as
    // a duplicable reference; a closure falls back to the local-only Clicked event.
    public void SetAction(Action<Button, UIInteractionContext>? action)
    {
        if (action == null)
            return;
        if (action.Target is IWorldElement)
            PressAction.Target = action;
        else
            Clicked += action;
    }

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

    // A button inside a scroll list relinquishes any real drag so the list scrolls; a click (no movement)
    // stays with the button and still fires on release.
    public override bool PassDragToParent(in float2 dragDelta)
        => System.Math.Abs(dragDelta.x) > DragPassThreshold || System.Math.Abs(dragDelta.y) > DragPassThreshold;

    protected override void OnSubmit(in UIInteractionContext context)
    {
        Clicked?.Invoke(this, context);
        PressAction.Target?.Invoke(this, context);
    }
}
