// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// One unit of a quantity: base = value * Factor + Offset. Offset is only for temperatures.
public sealed class ScriptUnit
{
    public string Symbol { get; }
    public double Factor { get; }
    public double Offset { get; }
    public string[] Aliases { get; }

    // The unit written out, for one of it and for any other amount: "metre", "metres". Text reads either.
    public string? Name { get; init; }
    public string? Plural { get; init; }

    // One of the steps an automatic choice of unit picks between, such as mm, cm, m and km.
    public bool Auto { get; init; }

    public ScriptUnit(string symbol, double factor, params string[] aliases)
        : this(symbol, factor, 0d, aliases)
    {
    }

    public ScriptUnit(string symbol, double factor, double offset, params string[] aliases)
    {
        Symbol = symbol;
        Factor = factor;
        Offset = offset;
        Aliases = aliases ?? Array.Empty<string>();
    }

    public double ToBase(double value) => value * Factor + Offset;

    public double FromBase(double value) => (value - Offset) / Factor;

    public bool Names(string text, StringComparison comparison)
    {
        if (string.Equals(Symbol, text, comparison) || string.Equals(Name, text, comparison) || string.Equals(Plural, text, comparison))
            return true;
        foreach (var alias in Aliases)
        {
            if (string.Equals(alias, text, comparison))
                return true;
        }
        return false;
    }
}
