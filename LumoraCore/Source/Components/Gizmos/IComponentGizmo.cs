// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Gizmos;

// A gizmo that annotates one COMPONENT rather than a slot: the light's reach, the collider's shape,
// the camera's frustum. Separate from IGizmo because the thing it points at is a
// component, and because these never spawn on selection - they are toggled per component.
public interface IComponentGizmo
{
    bool IsActive { get; set; }

    Component? TargetComponent { get; }

    void Setup(Component target, User? owner);
}
