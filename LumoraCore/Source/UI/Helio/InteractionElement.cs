// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Lumora.Core;
using Lumora.Core.Math;

namespace Helio.UI;

public class InteractionElement : UIComponent, IUIInteractable
{
    public readonly Sync<bool> Interactable;
    public readonly Sync<color> BaseColor;
    public readonly Sync<bool> IsHovering;
    public readonly Sync<bool> IsPressed;

    public event Action<UIInteractionContext>? HoverEntered;
    public event Action<UIInteractionContext>? HoverExited;
    public event Action<UIInteractionContext>? Pressed;
    public event Action<UIInteractionContext>? Dragged;
    public event Action<UIInteractionContext>? Released;
    public event Action<UIInteractionContext>? Submitted;

    public InteractionElement()
    {
        Interactable = new Sync<bool>(this, true);
        BaseColor = new Sync<color>(this, color.White);
        IsHovering = new Sync<bool>(this, false);
        IsPressed = new Sync<bool>(this, false);
    }

    public bool CanInteract => Enabled.Value && Interactable.Value && Slot != null && Slot.IsActive;
    public InteractionState CurrentInteractionState
    {
        get
        {
            if (!CanInteract) return InteractionState.Disabled;
            if (IsPressed.Value) return InteractionState.Pressed;
            if (IsHovering.Value) return InteractionState.Highlight;
            return InteractionState.Normal;
        }
    }

    public IEnumerable<ColorDriver> ColorDrivers => Slot?.GetComponents<ColorDriver>() ?? Array.Empty<ColorDriver>();

    public ColorDriver AddColorDriver(IField<color> target, color? normalColor = null,
        InteractionColorMode mode = InteractionColorMode.Explicit)
    {
        var driver = Slot.AttachComponent<ColorDriver>();
        driver.Interaction.Target = this;
        driver.Target.Target = target;
        driver.TintColorMode.Value = mode;
        driver.SetColors(normalColor ?? target.Value);
        driver.Apply(this);
        return driver;
    }

    public virtual bool IsPointInside(in float2 point)
    {
        return RectTransform?.LocalComputeRect.Contains(point) ?? false;
    }

    public void NotifyHoverEnter(in UIInteractionContext context)
    {
        using var actorScope = EnterInteractionActor(in context);
        if (!CanInteract) return;
        IsHovering.Value = true;
        ApplyColorDrivers();
        OnHoverEnter(in context);
        HoverEntered?.Invoke(context);
    }

    public void NotifyHoverExit(in UIInteractionContext context)
    {
        using var actorScope = EnterInteractionActor(in context);
        IsHovering.Value = false;
        ApplyColorDrivers();
        OnHoverExit(in context);
        HoverExited?.Invoke(context);
    }

    public void NotifyPress(in UIInteractionContext context)
    {
        using var actorScope = EnterInteractionActor(in context);
        if (!CanInteract) return;
        IsPressed.Value = true;
        ApplyColorDrivers();
        OnPress(in context);
        Pressed?.Invoke(context);
    }

    public void NotifyDrag(in UIInteractionContext context)
    {
        using var actorScope = EnterInteractionActor(in context);
        if (!CanInteract) return;
        OnDrag(in context);
        Dragged?.Invoke(context);
    }

    public void NotifyRelease(in UIInteractionContext context)
    {
        using var actorScope = EnterInteractionActor(in context);
        IsPressed.Value = false;
        ApplyColorDrivers();
        OnRelease(in context);
        Released?.Invoke(context);
    }

    public void NotifySubmit(in UIInteractionContext context)
    {
        using var actorScope = EnterInteractionActor(in context);
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

    private IDisposable? EnterInteractionActor(in UIInteractionContext context)
    {
        var world = Slot?.World ?? context.Canvas?.World;
        return world?.DataModelPermissions.EnterActor(context.Actor);
    }

    public override void OnChanges()
    {
        base.OnChanges();
        ApplyColorDrivers();
    }

    public override void OnDisabled()
    {
        base.OnDisabled();
        ApplyColorDrivers();
    }

    public override void OnEnabled()
    {
        base.OnEnabled();
        ApplyColorDrivers();
    }

    protected void ApplyColorDrivers()
    {
        foreach (var driver in ColorDrivers)
        {
            driver.Apply(this);
        }
    }
}
