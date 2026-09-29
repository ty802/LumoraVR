// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// The values a Shared script keeps on its object for every machine to read: numbers, vectors, colours,
// true or false, text and the coded values (dates, spans of time, quantities), each under a name, held as
// text on the wire. Past WarnValues on one object the script is warned. Text reads any value as it is
// written; numbers read across number types, whole ones only into int and long; anything else reads only
// as the type it was written as. -xlinka
internal static class ScriptSynced
{
    public const int WarnValues = 64;

    // A name a value may be kept under. None at all is not a value.
    public static bool CheckName(string? name) => !string.IsNullOrEmpty(name);

    public static string TypeName<T>() => ScriptTypes.Get(typeof(T)).Name;

    public static string Format<T>(T value) => ScriptTextForm.Format(ScriptTypes.Get(typeof(T)), value);

    // The value written as storedType, read as T. Boxed, since the entry keeps what it last read.
    public static bool TryParse<T>(string storedType, string text, out object? value)
    {
        value = null;
        var wanted = typeof(T);
        if (wanted == typeof(string))
        {
            value = text;
            return true;
        }
        if (IsNumber(storedType) && IsNumber(wanted))
            return TryNumber(wanted, text, out value);
        if (!ScriptTypes.TryGet(storedType, out var stored) || stored.ClrType != wanted)
            return false;
        if (wanted == typeof(bool))
        {
            if (text is not ("true" or "false"))
                return false;
            value = text == "true";
            return true;
        }
        return ScriptTextForm.TryParse(stored, text, out value);
    }

    private static bool IsNumber(string type) => type is "int" or "long" or "float" or "double";

    private static bool IsNumber(Type type) => type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double);

    private static bool TryNumber(Type wanted, string text, out object? value)
    {
        value = null;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            return false;
        if (wanted == typeof(double))
            value = number;
        else if (wanted == typeof(float))
            value = (float)number;
        else if (System.Math.Floor(number) != number)
            return false;
        else if (wanted == typeof(int) && number is >= int.MinValue and <= int.MaxValue)
            value = (int)number;
        else if (wanted == typeof(long) && number > long.MinValue && number < long.MaxValue)
            value = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long exact) ? exact : (long)number;
        return value != null;
    }
}
