// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Who is eating it, the bite under way counting from zero, and how far into that bite, 0 to 1.
public readonly record struct EdibleEating(User? User, int Bite, float Progress);
