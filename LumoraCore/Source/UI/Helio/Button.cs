// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

[ComponentCategory("UI/Helio/Interaction")]
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
        Broadcast(in context);
    }

    // Everything on this slot that asked to hear about a press, hears about it. The single press
    // action above stays exactly as it was - it is how the thing that BUILT the button binds its own
    // handler, and it survives duplication because it names an element and a method rather than
    // closing over one. This is for the other case: a slot assembled out of parts, where several
    // components each want the same press and there is only one delegate to go round. -xlinka
    private void Broadcast(in UIInteractionContext context)
    {
        var slot = Slot;
        if (slot == null || slot.IsDestroyed)
            return;

        foreach (var component in slot.GetComponents<Lumora.Core.Component>())
        {
            if (component is not IButtonPressReceiver receiver || component.IsDestroyed || !component.Enabled.Value)
                continue;
            try
            {
                receiver.OnButtonPressed(this, in context);
            }
            catch (System.Exception ex)
            {
                // One bad handler must not eat the press for the others.
                Lumora.Core.Logging.Logger.Error($"Button: a press receiver threw: {ex.Message}");
            }
        }
    }

    // Quiet on purpose. A click is confirmation, and confirmation that competes with the room is
    // the kind of sound people turn off.
}
