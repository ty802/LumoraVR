// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Scripting;

// What began touching the object: its slot, and the user when it is someone's body; where they met, the
// normal pointing into the watched object, and how hard, in newton seconds.
public readonly record struct CollisionEntered(Slot? Other, User? User, float3 Point, float3 Normal, float Impulse);
