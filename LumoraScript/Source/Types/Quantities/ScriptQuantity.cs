// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Script;

// An amount of one kind of thing, in that kind's base unit: metres, radians, kilograms. A distance and
// an angle are different types, so one cannot be wired where the other is meant.
public readonly struct ScriptQuantity<TKind> : IEquatable<ScriptQuantity<TKind>>, IScriptQuantity<ScriptQuantity<TKind>>
    where TKind : IScriptQuantityKind
{
    public double BaseValue { get; }

    public ScriptQuantity(double baseValue)
    {
        BaseValue = baseValue;
    }

    public static ScriptQuantity<TKind> FromBase(double value) => new(value);

    public static ScriptUnit[] Units => TKind.Units;

    public bool Equals(ScriptQuantity<TKind> other) => BaseValue.Equals(other.BaseValue);

    public override bool Equals(object? obj) => obj is ScriptQuantity<TKind> other && Equals(other);

    public override int GetHashCode() => BaseValue.GetHashCode();

    public override string ToString() => ScriptQuantities.Format(BaseValue, TKind.Units, TKind.Units[0], -1);
}
