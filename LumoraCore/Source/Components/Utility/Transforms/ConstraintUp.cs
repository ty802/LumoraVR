// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

// Where an aiming constraint's up points.
public enum ConstraintUp
{
    // World +Y.
    World,
    // Toward UpObject.
    ObjectUp,
    // UpObject's own rotation applied to WorldUp.
    ObjectRotationUp,
    // WorldUp as given, in world space.
    Vector,
    // No up: the shortest turn that aims.
    None,
}
