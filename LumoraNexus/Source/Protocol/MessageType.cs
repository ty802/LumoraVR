// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Nexus.Protocol;

public enum MessageType : byte
{
    // join, leave, grant, etc.
    Control = 0,

    // Incremental property changes only, 1-5KB typical.
    Delta = 1,

    // Complete object state, 10KB-1MB typical. Sent to new users or for conflict resolution.
    Full = 2,

    // High-frequency continuous data: transforms, audio, etc. at 60+ Hz.
    Stream = 3,

    Confirmation = 4
}
