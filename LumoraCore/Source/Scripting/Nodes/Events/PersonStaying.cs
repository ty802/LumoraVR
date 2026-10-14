// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who is still in the colliders: the person and their user root.
public readonly record struct PersonStaying(User? User, Slot? Body);
