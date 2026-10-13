// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Components.Interaction;

// What one press of a material tool reaches: the surface the ray hits, everything inside the sphere at
// the tip, the hit material everywhere in that object, or the hit material everywhere in the world.
public enum MaterialToolMode
{
    Ray,
    Area,
    Object,
    Scene,
}
