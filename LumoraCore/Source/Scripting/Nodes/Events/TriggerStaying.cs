// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// What is still inside: the collider's slot, or a person's user root with the user.
public readonly record struct TriggerStaying(Slot? Other, User? User);
