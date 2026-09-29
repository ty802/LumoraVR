// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who took the bite, and which one, counting from zero.
public readonly record struct EdibleBite(User? User, int Bite);
