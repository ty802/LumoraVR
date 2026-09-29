// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components;

// How a locomotion module moves its person: not at all, walking under gravity and collision, hopping to
// where an arc lands, or flying straight through everything. Other is a module that says none of these.
public enum LocomotionKind
{
    None,
    Walk,
    Teleport,
    Noclip,
    Other
}
