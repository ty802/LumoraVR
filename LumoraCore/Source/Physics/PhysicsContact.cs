// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Physics;

// One side of a touch between two bodies: Self is the side being told, Other what it touched, or null
// when that is no slot of this world. Point and Normal are in world space, the normal pointing from
// Other into Self; Impulse is in newton seconds, and zero when the touch ends.
public readonly record struct PhysicsContact(Slot Self, Slot? Other, float3 Point, float3 Normal, float Impulse);
