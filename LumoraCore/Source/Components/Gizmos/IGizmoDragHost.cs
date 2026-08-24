// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Gizmos;

// The gizmo a handle belongs to, as far as the handle needs to know it: something that wants telling
// when a drag session opens or closes so it can freeze its chrome.
//
// Handles are shared between the slot gizmo and the per-component gizmos, and the two have nothing
// else in common, so this is the whole contract rather than a base class neither of them wants.
// -xlinka
public interface IGizmoDragHost
{
    // handles cache the lookup and re-resolve once this reads true
    bool IsDestroyed { get; }

    void NotifyHandleDrag(bool dragging);
}
