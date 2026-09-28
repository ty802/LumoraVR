// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// A measured amount, held in its kind's base unit. The quantity nodes are generic over this.
public interface IScriptQuantity<TSelf> where TSelf : struct, IScriptQuantity<TSelf>
{
    double BaseValue { get; }

    static abstract TSelf FromBase(double value);

    static abstract ScriptUnit[] Units { get; }
}
