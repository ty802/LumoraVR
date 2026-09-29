// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// The slot that came under the watched one; nothing when it is gone again by the time the call runs.
public readonly record struct ChildAdded(Slot? Child);
