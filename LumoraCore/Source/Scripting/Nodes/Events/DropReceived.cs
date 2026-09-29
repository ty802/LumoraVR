// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// What came to rest on the surface, and who let go of it.
public readonly record struct DropReceived(Slot? Dropped, User? User);
