// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// What a quantity measures: its script type name and its units, the base unit first.
public interface IScriptQuantityKind
{
    static abstract string Name { get; }
    static abstract ScriptUnit[] Units { get; }
}
