// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

public class InteractionElement : UIComponent, IUIInteractable
{
    public readonly Sync<bool> Interactable;

    public event Action<UIInteractionContext>? HoverEntered;
    public event Action<UIInteractionContext>? HoverExited;
    public event Action<UIInteractionContext>? Pressed;
    public event Action<UIInteractionContext>? Dragged;
    public event Action<UIInteractionContext>? Released;
    public event Action<UIInteractionContext>? Submitted;

    public InteractionElement()
    {
        Interactable = new Sync<bool>(this, true);
    }

    public bool CanInteract => Enabled.Value && Interactable.Value && Slot != null && Slot.IsActive;

    public virtual bool IsPointInside(in float2 point)
    {
        return RectTransform?.LocalComputeRect.Contains(point) ?? false;
    }

    public void NotifyHoverEnter(in UIInteractionContext context)
    {
        if (!CanInteract) return;
        OnHoverEnter(in context);
        HoverEntered?.Invoke(context);
    }

    public void NotifyHoverExit(in UIInteractionContext context)
    {
        OnHoverExit(in context);
        HoverExited?.Invoke(context);
    }

    public void NotifyPress(in UIInteractionContext context)
    {
        if (!CanInteract) return;
        OnPress(in context);
        Pressed?.Invoke(context);
    }

    public void NotifyDrag(in UIInteractionContext context)
    {
        if (!CanInteract) return;
        OnDrag(in context);
        Dragged?.Invoke(context);
    }

    public void NotifyRelease(in UIInteractionContext context)
    {
        OnRelease(in context);
        Released?.Invoke(context);
    }

    public void NotifySubmit(in UIInteractionContext context)
    {
        if (!CanInteract) return;
        OnSubmit(in context);
        Submitted?.Invoke(context);
    }

    protected virtual void OnHoverEnter(in UIInteractionContext context) { }
    protected virtual void OnHoverExit(in UIInteractionContext context) { }
    protected virtual void OnPress(in UIInteractionContext context) { }
    protected virtual void OnDrag(in UIInteractionContext context) { }
    protected virtual void OnRelease(in UIInteractionContext context) { }
    protected virtual void OnSubmit(in UIInteractionContext context) { }
}
