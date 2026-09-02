// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Components.Interaction;
using Lumora.Core.Math;

namespace Helio.UI;

public interface IUIInteractable
{
    RectTransform? RectTransform { get; }
    bool CanInteract { get; }

    bool IsPointInside(in float2 point);

    void NotifyHoverEnter(in UIInteractionContext context);
    void NotifyHoverExit(in UIInteractionContext context);
    void NotifyPress(in UIInteractionContext context);
    void NotifyDrag(in UIInteractionContext context);
    void NotifyRelease(in UIInteractionContext context);
    void NotifySubmit(in UIInteractionContext context);

    // Every frame the pointer sits over this element, pressed or not. Enter/Exit only fire on
    // the edges and Drag only while pressed, so anything that tracks the cursor while idle
    // (the widget grid's placement preview) needs this. Default no-op so the hundred existing
    // elements do not care. -xlinka
    void NotifyHoverMove(in UIInteractionContext context) { }
}

// An interactable that can claim the whole subtree under its slot: while CapturesSubtree says yes the
// hit scan does not descend past it, so buttons and fields below cannot steal the pointer. The widget
// grid uses it in edit mode (and while a widget is being carried) so a grab anywhere on a widget picks
// the widget up instead of clicking whatever is under the cursor. -xlinka
public interface IUIInteractionCapture : IUIInteractable
{
    bool CapturesSubtree(in UIInteractionContext context);
}

// A canvas element that hands out something grabbable when the pointer's grab action starts over it.
// Returns null to decline; the pointer then falls back to grabbing whatever physical thing it hit. -xlinka
public interface IUIGrabbable
{
    IGrabbable? TryGrab(in UIInteractionContext context);
}

// A canvas element that can take held items when the pointer's grab action ends over it. Returns true when
// it took at least one, in which case the pointer must not drop the item into the world. -xlinka
public interface IUIGrabReceiver
{
    bool TryReceive(IReadOnlyList<IGrabbable> items, in UIInteractionContext context);
}

public interface IUIAxisActionReceiver
{
    bool ProcessAxis(in UIInteractionContext context, in float2 axis);
}

public interface IUISecondaryActionReceiver
{
    bool TriggerSecondary(in UIInteractionContext context);
}
