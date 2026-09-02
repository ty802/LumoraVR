// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Components.Interaction;

namespace Lumora.Core.Components.UI;

// The pointer face of a WidgetGrid. It covers the whole grid rect and sits under every widget in the
// hit order, so it only gets the pointer where no live control took it: in edit mode that is the whole
// grid, because the widgets switch their controls off. While a panel is being carried it captures the
// subtree outright, so the landing preview and the put-down never get stolen by a control that stayed
// live. The grab action asks it for a widget to lift, the release offers it what the hand holds; both
// walk up from whatever was hovered, so a grab that landed on a widget's own control still picks that
// widget up. -xlinka
[ComponentCategory("Hidden")]
public sealed class WidgetGridInteraction : InteractionElement, IUIInteractionCapture, IUIGrabbable, IUIGrabReceiver
{
    private WidgetGrid? _grid;

    private WidgetGrid? Grid
    {
        get
        {
            if (_grid == null || _grid.IsDestroyed)
                _grid = Slot.GetComponent<WidgetGrid>();
            return _grid;
        }
    }

    public bool CapturesSubtree(in UIInteractionContext context) => Grid?.IsCarrying(context.Source) == true;

    protected override void OnHoverEnter(in UIInteractionContext context)
        => Grid?.ProcessPointer(in context, GridPointerPhase.Begin, GridPointerPhase.None);

    // While pressed the drag carries the pointer each frame; forwarding the hover as well would run the
    // gesture twice a frame.
    protected override void OnHoverMove(in UIInteractionContext context)
    {
        if (!IsPressed.Value)
            Grid?.ProcessPointer(in context, GridPointerPhase.Stay, GridPointerPhase.None);
    }

    protected override void OnHoverExit(in UIInteractionContext context)
    {
        if (!IsPressed.Value)
            Grid?.ProcessPointer(in context, GridPointerPhase.End, GridPointerPhase.None);
    }

    protected override void OnPress(in UIInteractionContext context)
        => Grid?.ProcessPointer(in context, GridPointerPhase.Stay, GridPointerPhase.Begin);

    protected override void OnDrag(in UIInteractionContext context)
        => Grid?.ProcessPointer(in context, IsHovering.Value ? GridPointerPhase.Stay : GridPointerPhase.End, GridPointerPhase.Stay);

    protected override void OnRelease(in UIInteractionContext context)
        => Grid?.ProcessPointer(in context, IsHovering.Value ? GridPointerPhase.Stay : GridPointerPhase.End, GridPointerPhase.End);

    public IGrabbable? TryGrab(in UIInteractionContext context) => Grid?.TryPickUp(in context);

    public bool TryReceive(IReadOnlyList<IGrabbable> items, in UIInteractionContext context)
        => Grid?.TryReceive(items, in context) == true;
}
