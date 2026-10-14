// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who left the colliders, or the world while in them: the person and their user root, when still there.
public readonly record struct PersonExited(User? User, Slot? Body);
