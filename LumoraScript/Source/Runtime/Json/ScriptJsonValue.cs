// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;

namespace Lumora.Script;

// A JSON value that holds no others: null, true or false, a number or text. A whole number stays exact, as
// a long or, past a long, an unsigned long; any other number is a double. JSON has no NaN or infinity, so
// a number that is not finite goes in as null.
public sealed class ScriptJsonValue : ScriptJson
{
    private enum Form : byte
    {
        None,
        Long,
        ULong,
        Double
    }

    // Null, true, false and the empty object and array are the same everywhere, so every instance shares
    // them and none counts them.
    internal static readonly ScriptCollectionBudget Shared = new(long.MaxValue);

    public static readonly ScriptJsonValue Null = new(Shared, ScriptJsonKind.Null, Form.None, 0, null, false);
    public static readonly ScriptJsonValue True = new(Shared, ScriptJsonKind.Bool, Form.None, 0, null, true);
    public static readonly ScriptJsonValue False = new(Shared, ScriptJsonKind.Bool, Form.None, 0, null, false);

    private readonly ScriptJsonKind _kind;
    private readonly Form _form;
    private readonly ulong _bits;
    private readonly string? _text;
    private readonly bool _flag;

    private ScriptJsonValue(ScriptCollectionBudget budget, ScriptJsonKind kind, Form form, ulong bits, string? text, bool flag)
        : base(budget)
    {
        _kind = kind;
        _form = form;
        _bits = bits;
        _text = text;
        _flag = flag;
        TakeWhole(text == null ? 32 : 56 + 2L * text.Length);
    }

    public static ScriptJsonValue Of(bool flag) => flag ? True : False;

    public static ScriptJsonValue Of(ScriptCollectionBudget budget, string? text)
        => text == null ? Null : new(budget, ScriptJsonKind.String, Form.None, 0, text, false);

    public static ScriptJsonValue Of(ScriptCollectionBudget budget, long number)
        => new(budget, ScriptJsonKind.Number, Form.Long, unchecked((ulong)number), null, false);

    public static ScriptJsonValue Of(ScriptCollectionBudget budget, ulong number)
        => number <= long.MaxValue ? Of(budget, (long)number) : new(budget, ScriptJsonKind.Number, Form.ULong, number, null, false);

    public static ScriptJsonValue Of(ScriptCollectionBudget budget, double number)
        => double.IsFinite(number)
            ? new(budget, ScriptJsonKind.Number, Form.Double, unchecked((ulong)BitConverter.DoubleToInt64Bits(number)), null, false)
            : Null;

    // A float by the shortest decimal that reads back as it, so 0.1f is 0.1 and not 0.10000000149011612.
    public static ScriptJsonValue Of(ScriptCollectionBudget budget, float number)
        => float.IsFinite(number)
            ? Of(budget, double.Parse(number.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture))
            : Null;

    // A value no instance owns, such as a query's literal: kept with the shared ones and counted by none.
    // Queries are read on whichever thread runs the script, so the shared budget is taken under a lock.
    internal static ScriptJsonValue Unowned(string text)
    {
        lock (Shared)
            return Of(Shared, text);
    }

    internal static ScriptJsonValue Unowned(long number)
    {
        lock (Shared)
            return Of(Shared, number);
    }

    internal static ScriptJsonValue Unowned(double number)
    {
        lock (Shared)
            return Of(Shared, number);
    }

    public override ScriptJsonKind Kind => _kind;

    public override long Size => 1;

    public override int Depth => 0;

    public override int Count => 0;

    public bool Bool => _flag;

    // The text of a string value; null for any other.
    public string? Text => _text;

    // A number as a double, which is exact for every whole number up to 2^53.
    public double Number => _form switch
    {
        Form.Long => unchecked((long)_bits),
        Form.ULong => _bits,
        Form.Double => BitConverter.Int64BitsToDouble(unchecked((long)_bits)),
        _ => 0d,
    };

    public bool IsWhole => _form is Form.Long or Form.ULong || (_form == Form.Double && Number == System.Math.Floor(Number));

    public bool TryGetLong(out long value)
    {
        switch (_form)
        {
            case Form.Long:
                value = unchecked((long)_bits);
                return true;
            case Form.Double:
                double number = Number;
                // 2^63 itself is past a long; the largest double below it is not.
                if (number == System.Math.Floor(number) && number >= -9223372036854775808d && number < 9223372036854775808d)
                {
                    value = (long)number;
                    return true;
                }
                break;
        }
        value = 0;
        return false;
    }

    public bool TryGetULong(out ulong value)
    {
        switch (_form)
        {
            case Form.Long when unchecked((long)_bits) >= 0:
                value = _bits;
                return true;
            case Form.ULong:
                value = _bits;
                return true;
            case Form.Double:
                double number = Number;
                if (number == System.Math.Floor(number) && number >= 0 && number < 18446744073709551616d)
                {
                    value = (ulong)number;
                    return true;
                }
                break;
        }
        value = 0;
        return false;
    }

    // The number as JSON writes it.
    public string NumberText => _form switch
    {
        Form.Long => unchecked((long)_bits).ToString(CultureInfo.InvariantCulture),
        Form.ULong => _bits.ToString(CultureInfo.InvariantCulture),
        Form.Double => Number.ToString("R", CultureInfo.InvariantCulture),
        _ => "0",
    };

    private protected override bool Same(ScriptJson other)
    {
        var value = (ScriptJsonValue)other;
        switch (_kind)
        {
            case ScriptJsonKind.Bool:
                return _flag == value._flag;
            case ScriptJsonKind.String:
                return string.Equals(_text, value._text, StringComparison.Ordinal);
            case ScriptJsonKind.Number:
                // Whole numbers held exactly compare exactly; a double on either side compares as doubles.
                if (_form != Form.Double && value._form != Form.Double)
                    return _form == value._form && _bits == value._bits;
                return Number == value.Number;
            default:
                return true;
        }
    }

    private protected override int Hash() => _kind switch
    {
        ScriptJsonKind.Bool => _flag ? 0x2B1 : 0x2B3,
        ScriptJsonKind.String => StringComparer.Ordinal.GetHashCode(_text!),
        // Equal numbers are equal as doubles, so they hash as doubles, and -0 as 0.
        ScriptJsonKind.Number => Number is var number && number == 0 ? 0x3C5 : number.GetHashCode(),
        _ => 0x1D7,
    };
}
