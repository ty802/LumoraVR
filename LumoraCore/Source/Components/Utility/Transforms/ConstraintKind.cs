// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Utility;

// What a TransformConstraint takes from its sources.
public enum ConstraintKind
{
    // Position and rotation, as if the slot were a child of each source at that source's offset.
    Parent,
    Position,
    Rotation,
    Scale,
    // Turns AimAxis toward the sources, keeping UpAxis toward the up UpMode picks.
    Aim,
    // Aim with +Z forward and +Y up, plus a roll about the forward axis.
    LookAt,
}
