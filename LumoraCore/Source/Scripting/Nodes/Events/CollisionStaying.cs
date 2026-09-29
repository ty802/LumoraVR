// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// What is still touching the object: its slot, and the user when it is someone's body.
public readonly record struct CollisionStaying(Slot? Other, User? User);
