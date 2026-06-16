// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

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
}

public interface IUIAxisActionReceiver
{
    bool ProcessAxis(in UIInteractionContext context, in float2 axis);
}

public interface IUISecondaryActionReceiver
{
    bool TriggerSecondary(in UIInteractionContext context);
}
