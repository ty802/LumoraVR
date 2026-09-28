// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Linq;

namespace Lumora.Script;

// A unit named on a quantity node, found the way text with units is read: its symbol or a name, exact
// case first. Blank is the base unit; a unit the kind does not have faults the call, naming the ones it
// does.
internal static class QuantityUnitLookup
{
    private const int MaxShown = 32;

    public static ScriptUnit Find<T>(string? unit) where T : struct, IScriptQuantity<T>
    {
        var found = ScriptQuantities.FindUnit(T.Units, unit);
        if (found != null)
            return found;
        var kind = ScriptTypes.TryGet(typeof(T), out var type) ? type.Name : typeof(T).Name;
        var shown = unit!.Length > MaxShown ? unit[..MaxShown] + "..." : unit;
        throw new InvalidOperationException($"{kind} has no unit '{shown}'; it has {string.Join(", ", T.Units.Select(u => u.Symbol))}");
    }
}
