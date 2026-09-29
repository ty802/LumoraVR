// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// The slot that left the watched one, and the name it had then. One that left by being destroyed reads as
// nothing.
public readonly record struct ChildRemoved(Slot? Child, string Name);
