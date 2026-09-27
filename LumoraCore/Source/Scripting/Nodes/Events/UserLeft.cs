// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// The user is on their way out: their name still reads, their body may already be gone.
public readonly record struct UserLeft(User? User);
