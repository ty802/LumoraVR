// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

public enum ScriptCallStatus : byte
{
    Ok,

    // The call stopped at a trap: a bad operation, a cap, or a node's C# throwing. Writes made before
    // it stay made; there is no rollback.
    Trap,

    // The call used up its fuel.
    OutOfFuel,

    NoSuchEvent,

    // The call ran past its time on the clock.
    OutOfTime
}
