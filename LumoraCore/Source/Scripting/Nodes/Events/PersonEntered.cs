// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who came into the colliders: the person and their user root.
public readonly record struct PersonEntered(User? User, Slot? Body);
