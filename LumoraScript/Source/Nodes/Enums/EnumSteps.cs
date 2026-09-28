// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Lumora.Script;

// An enum's values in order, lowest number first, each number once: two names for one number are one
// step. Worked out once per enum, for the enum nodes to step through and check against. A flags enum
// takes any mix of its flags as valid, not only the mixes it names. -xlinka
internal static class EnumSteps<T> where T : struct, Enum
{
    public static readonly T[] Values = Order();

    private static readonly TypeCode Underlying = Type.GetTypeCode(typeof(T));
    private static readonly bool IsFlags = typeof(T).IsDefined(typeof(FlagsAttribute), false);
    private static readonly ulong AllFlags = Flags();

    // A number is an int: an unsigned 32-bit enum travels as its bits, and a wider one gives its low 32.
    public static int ToInt(T value) => unchecked((int)Bits(value));

    // False when the number fits the enum but names none of its values (or, for flags, has a bit no flag
    // has), or does not fit its underlying type at all.
    public static bool TryFromInt(int number, out T value)
    {
        value = default;
        bool fits = Underlying switch
        {
            TypeCode.SByte => number is >= sbyte.MinValue and <= sbyte.MaxValue,
            TypeCode.Byte => number is >= byte.MinValue and <= byte.MaxValue,
            TypeCode.Int16 => number is >= short.MinValue and <= short.MaxValue,
            TypeCode.UInt16 => number is >= ushort.MinValue and <= ushort.MaxValue,
            TypeCode.UInt64 => number >= 0,
            _ => true,
        };
        if (!fits)
            return false;
        var candidate = Underlying == TypeCode.UInt32
            ? (T)Enum.ToObject(typeof(T), unchecked((uint)number))
            : (T)Enum.ToObject(typeof(T), number);
        if (!IsValid(candidate))
            return false;
        value = candidate;
        return true;
    }

    public static bool IsValid(T value)
        => IsFlags ? (Bits(value) & ~AllFlags) == 0 : Array.BinarySearch(Values, value) >= 0;

    // Steps along the values, wrapping past either end. A value the enum does not name steps from where
    // it would sit among them: one step up is the next value above it.
    public static T Shift(T value, int steps)
    {
        var values = Values;
        int count = values.Length;
        if (count == 0 || steps == 0)
            return value;
        int index = Array.BinarySearch(values, value);
        long target;
        if (index >= 0)
            target = (long)index + steps;
        else
            target = steps > 0 ? (long)~index + steps - 1 : (long)~index + steps;
        return values[(int)(((target % count) + count) % count)];
    }

    // The number's bits, sign-extended from a signed underlying type.
    private static ulong Bits(T value) => Underlying switch
    {
        TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 => Convert.ToUInt64(value, CultureInfo.InvariantCulture),
        _ => unchecked((ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture)),
    };

    private static T[] Order()
    {
        var all = Enum.GetValues<T>();
        Array.Sort(all, Comparer<T>.Default);
        var distinct = new List<T>(all.Length);
        foreach (var value in all)
        {
            if (distinct.Count == 0 || !EqualityComparer<T>.Default.Equals(distinct[^1], value))
                distinct.Add(value);
        }
        return distinct.ToArray();
    }

    private static ulong Flags()
    {
        ulong all = 0;
        foreach (var value in Values)
            all |= Bits(value);
        return all;
    }
}
