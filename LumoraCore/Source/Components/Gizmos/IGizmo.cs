// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Gizmos;

/// <summary>
/// Interface for gizmo components that can be activated/deactivated.
/// </summary>
public interface IGizmo
{
    /// <summary>
    /// Whether this gizmo is currently active.
    /// </summary>
    bool IsActive { get; set; }

    /// <summary>
    /// Set up this gizmo for a target slot.
    /// </summary>
    void Setup(Slot targetSlot);

    /// <summary>
    /// The slot this gizmo is controlling.
    /// </summary>
    Slot TargetSlot { get; }
}
