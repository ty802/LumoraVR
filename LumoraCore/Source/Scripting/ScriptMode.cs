// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Scripting;

// Where a script runs.
public enum ScriptMode : byte
{
    // On the session host, as the script's author, writing replicated state through the gate.
    Shared,

    // On every peer, as its author, for local-only effects.
    Local
}
