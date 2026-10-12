// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Lumora.Core.Localization;

namespace Helio.UI;

public enum FieldMathError : byte
{
    None,
    Empty,
    Syntax,
    UnknownName,
    NotFinite,
}

// What a typed number field means. A plain number reads exactly as it always did; anything else is worked
// out: + - * / %, ^ for a power, brackets, sqrt abs min max floor ceil round, sin cos tan in DEGREES, and the
// constants pi, e and tau. x (or v) is the value the field holds now.
//
// Text that opens with * / % ^ or + works on that value: "*2" doubles it, "+0.5" adds to it, "^2" squares
// it. A minus is a negative number ("-1", "-x") unless a space follows it: "- 1" takes one off. "x-1" says
// the same thing without the space. A number straight before a name or a bracket multiplies ("2pi", "3(x+1)").
//
// Our own small parser over invariant text: nothing is compiled, nothing is looked up past the names
// below, and a result that is not a finite number is refused rather than written. -xlinka
public static class FieldMath
{
    public readonly struct Result
    {
        public readonly double Value;
        public readonly FieldMathError Error;
        public readonly string? Name;

        public Result(double value, FieldMathError error, string? name = null)
        {
            Value = value;
            Error = error;
            Name = name;
        }

        public bool Ok => Error == FieldMathError.None;

        // The short note a field shows in place of a value it refused. Empty text has none.
        public string Hint => Error switch
        {
            FieldMathError.Syntax => SyntaxText.Resolve(),
            FieldMathError.UnknownName => UnknownText.WithArgs(Name ?? "?").Resolve(),
            FieldMathError.NotFinite => NotFiniteText.Resolve(),
            _ => string.Empty,
        };
    }

    private static readonly LocaleText SyntaxText = LocaleText.Keyed("FieldMath.Hint.Syntax", "not a number");
    private static readonly LocaleText UnknownText = LocaleText.Keyed("FieldMath.Hint.Unknown", "unknown {0}");
    private static readonly LocaleText NotFiniteText = LocaleText.Keyed("FieldMath.Hint.NotFinite", "no finite result");
    private static readonly LocaleText TipText = LocaleText.Keyed("FieldMath.Tip",
        "Math works here: *2 doubles, +1 adds, x is the value now, sin cos tan take degrees");

    // What a number field says when the laser rests on it.
    public static string Tip => TipText.Resolve();

    private const int MaxLength = 256;
    private const int MaxDepth = 48;

    public static bool IsNumberType(Type? type)
        => type == typeof(float) || type == typeof(double) || type == typeof(decimal)
        || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
        || type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte);

    public static bool IsIntegerType(Type? type)
        => type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
        || type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte);

    // Whether the text works on the value the field holds rather than replacing it.
    public static bool IsRelative(string? text)
    {
        if (text == null)
            return false;
        int i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
            i++;
        if (i >= text.Length)
            return false;
        char c = text[i];
        if (c is '*' or '/' or '%' or '^' or '+')
            return true;
        return c == '-' && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]);
    }

    public static Result Evaluate(string? text, double current)
    {
        string trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return new Result(0d, FieldMathError.Empty);
        if (trimmed.Length > MaxLength)
            return new Result(0d, FieldMathError.Syntax);

        bool relative = IsRelative(trimmed);
        if (!relative && double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double plain))
            return double.IsFinite(plain) ? new Result(plain, FieldMathError.None) : new Result(0d, FieldMathError.NotFinite);

        var parser = new Parser(relative ? "x" + trimmed : trimmed, current);
        double value = parser.Sum(0);
        parser.SkipSpace();
        if (parser.Error == FieldMathError.None && !parser.AtEnd)
            parser.Fail(FieldMathError.Syntax);
        if (parser.Error != FieldMathError.None)
            return new Result(0d, parser.Error, parser.ErrorName);
        return double.IsFinite(value) ? new Result(value, FieldMathError.None) : new Result(0d, FieldMathError.NotFinite);
    }

    // The value a field of this type ends up holding. Integers round half away from zero and clamp to what
    // the type holds and to min and max; a float must still be finite once it is a float. A plain whole number
    // in an integer field is read exactly, so a long keeps every digit.
    public static bool TryConvert(string? text, Type type, object? current, out object? value, out Result result,
        double min = double.NegativeInfinity, double max = double.PositiveInfinity)
    {
        value = null;
        string trimmed = text?.Trim() ?? string.Empty;
        bool relative = IsRelative(trimmed);

        if (!relative && IsIntegerType(type) && TryExactInteger(trimmed, type, min, max, out value))
        {
            result = new Result(Convert.ToDouble(value, CultureInfo.InvariantCulture), FieldMathError.None);
            return true;
        }
        if (!relative && type == typeof(decimal)
            && decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal exact))
        {
            value = exact;
            result = new Result((double)exact, FieldMathError.None);
            return true;
        }

        result = Evaluate(trimmed, ToDouble(current));
        if (!result.Ok)
            return false;

        double number = System.Math.Clamp(result.Value, min, max);
        if (IsIntegerType(type))
        {
            value = ToInteger(System.Math.Round(number, MidpointRounding.AwayFromZero), type);
            return true;
        }
        if (type == typeof(float))
        {
            float single = (float)number;
            if (!float.IsFinite(single))
            {
                result = new Result(0d, FieldMathError.NotFinite);
                return false;
            }
            value = single;
            return true;
        }
        if (type == typeof(decimal))
        {
            if (number < (double)decimal.MinValue || number > (double)decimal.MaxValue)
            {
                result = new Result(0d, FieldMathError.NotFinite);
                return false;
            }
            value = (decimal)number;
            return true;
        }
        value = number;
        return true;
    }

    public static double ToDouble(object? value)
    {
        try
        {
            return value is IConvertible convertible ? convertible.ToDouble(CultureInfo.InvariantCulture) : 0d;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return 0d;
        }
    }

    private static bool TryExactInteger(string text, Type type, double min, double max, out object? value)
    {
        value = null;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long signed))
        {
            if (signed < min)
                return false;
            if (signed > max)
                return false;
            value = ClampInteger(signed, type);
            return true;
        }
        if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong unsigned) && unsigned <= max)
        {
            value = type == typeof(ulong) ? unsigned : ClampInteger(long.MaxValue, type);
            return true;
        }
        return false;
    }

    private static object ClampInteger(long v, Type type)
    {
        if (type == typeof(int)) return (int)System.Math.Clamp(v, int.MinValue, int.MaxValue);
        if (type == typeof(uint)) return (uint)System.Math.Clamp(v, uint.MinValue, uint.MaxValue);
        if (type == typeof(short)) return (short)System.Math.Clamp(v, short.MinValue, short.MaxValue);
        if (type == typeof(ushort)) return (ushort)System.Math.Clamp(v, ushort.MinValue, ushort.MaxValue);
        if (type == typeof(byte)) return (byte)System.Math.Clamp(v, byte.MinValue, byte.MaxValue);
        if (type == typeof(sbyte)) return (sbyte)System.Math.Clamp(v, sbyte.MinValue, sbyte.MaxValue);
        if (type == typeof(ulong)) return (ulong)System.Math.Max(v, 0L);
        return v;
    }

    // From an already rounded double. 2^63 and 2^64 are exact doubles, so the edges compare cleanly.
    private static object ToInteger(double v, Type type)
    {
        if (type == typeof(ulong))
            return v <= 0d ? 0UL : v >= 18446744073709551616d ? ulong.MaxValue : (ulong)v;
        long whole = v >= 9223372036854775808d ? long.MaxValue : v <= -9223372036854775808d ? long.MinValue : (long)v;
        return ClampInteger(whole, type);
    }

    // Exact where the answer is a whole or a half (sin 30 is 0.5, tan 45 is 1, cos 90 is 0), so a field that
    // asked for a round number gets one rather than 0.49999999999999994.
    private static double SinDegrees(double degrees)
    {
        double turn = degrees % 360d;
        if (turn < 0d)
            turn += 360d;
        if (turn % 30d == 0d)
        {
            switch ((int)(turn / 30d))
            {
                case 0: case 6: return 0d;
                case 1: case 5: return 0.5d;
                case 3: return 1d;
                case 7: case 11: return -0.5d;
                case 9: return -1d;
            }
        }
        return System.Math.Sin(turn * (System.Math.PI / 180d));
    }

    private static double CosDegrees(double degrees) => SinDegrees(degrees + 90d);

    private static double TanDegrees(double degrees)
    {
        double half = degrees % 180d;
        if (half < 0d)
            half += 180d;
        if (half % 45d == 0d)
        {
            switch ((int)(half / 45d))
            {
                case 0: return 0d;
                case 1: return 1d;
                case 2: return double.PositiveInfinity;
                case 3: return -1d;
            }
        }
        return SinDegrees(degrees) / CosDegrees(degrees);
    }

    private struct Parser
    {
        private readonly string _text;
        private readonly double _current;
        private int _at;

        public FieldMathError Error;
        public string? ErrorName;

        public Parser(string text, double current)
        {
            _text = text;
            _current = current;
            _at = 0;
            Error = FieldMathError.None;
            ErrorName = null;
        }

        public bool AtEnd => _at >= _text.Length;

        public double Fail(FieldMathError error, string? name = null)
        {
            if (Error == FieldMathError.None)
            {
                Error = error;
                ErrorName = name;
            }
            _at = _text.Length;
            return 0d;
        }

        public void SkipSpace()
        {
            while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
                _at++;
        }

        private char Peek()
        {
            SkipSpace();
            return _at < _text.Length ? _text[_at] : '\0';
        }

        public double Sum(int depth)
        {
            if (depth > MaxDepth)
                return Fail(FieldMathError.Syntax);
            double left = Product(depth);
            while (Error == FieldMathError.None)
            {
                char op = Peek();
                if (op != '+' && op != '-')
                    break;
                _at++;
                double right = Product(depth);
                left = op == '+' ? left + right : left - right;
            }
            return left;
        }

        private double Product(int depth)
        {
            double left = Unary(depth);
            while (Error == FieldMathError.None)
            {
                char op = Peek();
                if (op is '*' or '/' or '%')
                {
                    _at++;
                    double right = Unary(depth);
                    left = op == '*' ? left * right : op == '/' ? left / right : left % right;
                }
                else if (op == '(' || char.IsLetter(op))
                {
                    left *= Unary(depth);
                }
                else
                {
                    break;
                }
            }
            return left;
        }

        // A sign binds looser than a power, so -2^2 is -4.
        private double Unary(int depth)
        {
            if (depth > MaxDepth)
                return Fail(FieldMathError.Syntax);
            char c = Peek();
            if (c == '-')
            {
                _at++;
                return -Unary(depth + 1);
            }
            if (c == '+')
            {
                _at++;
                return Unary(depth + 1);
            }
            return Power(depth);
        }

        private double Power(int depth)
        {
            double value = Atom(depth);
            if (Error == FieldMathError.None && Peek() == '^')
            {
                _at++;
                value = System.Math.Pow(value, Unary(depth + 1));
            }
            return value;
        }

        private double Atom(int depth)
        {
            char c = Peek();
            if (c == '(')
            {
                _at++;
                double inner = Sum(depth + 1);
                if (Peek() != ')')
                    return Fail(FieldMathError.Syntax);
                _at++;
                return inner;
            }
            if (char.IsDigit(c) || c == '.')
                return Number();
            if (char.IsLetter(c))
                return Name(depth);
            return Fail(FieldMathError.Syntax);
        }

        private double Number()
        {
            int start = _at;
            int digits = 0;
            while (_at < _text.Length && char.IsDigit(_text[_at])) { _at++; digits++; }
            if (_at < _text.Length && _text[_at] == '.')
            {
                _at++;
                while (_at < _text.Length && char.IsDigit(_text[_at])) { _at++; digits++; }
            }
            if (digits == 0)
                return Fail(FieldMathError.Syntax);
            // An exponent only when digits follow, so "2e" is two times e.
            if (_at < _text.Length && (_text[_at] == 'e' || _text[_at] == 'E'))
            {
                int mark = _at + 1;
                if (mark < _text.Length && (_text[mark] == '+' || _text[mark] == '-'))
                    mark++;
                if (mark < _text.Length && char.IsDigit(_text[mark]))
                {
                    _at = mark;
                    while (_at < _text.Length && char.IsDigit(_text[_at]))
                        _at++;
                }
            }
            return double.Parse(_text.AsSpan(start, _at - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private double Name(int depth)
        {
            int start = _at;
            while (_at < _text.Length && (char.IsLetterOrDigit(_text[_at]) || _text[_at] == '_'))
                _at++;
            int end = _at;
            string name = _text.Substring(start, end - start).ToLowerInvariant();

            if (Peek() == '(')
            {
                if (!IsFunction(name))
                    return Fail(FieldMathError.UnknownName, _text.Substring(start, end - start));
                _at++;
                return Call(name, depth);
            }
            switch (name)
            {
                case "x": case "v": return _current;
                case "pi": case "π": return System.Math.PI;
                case "tau": case "τ": return System.Math.Tau;
                case "e": return System.Math.E;
            }
            return Fail(FieldMathError.UnknownName, _text.Substring(start, end - start));
        }

        private static bool IsFunction(string name)
            => name is "sqrt" or "abs" or "min" or "max" or "floor" or "ceil" or "round" or "sin" or "cos" or "tan";

        private double Call(string name, int depth)
        {
            double first = Sum(depth + 1);
            int count = 1;
            double low = first, high = first;
            while (Error == FieldMathError.None && Peek() == ',')
            {
                _at++;
                double next = Sum(depth + 1);
                count++;
                low = System.Math.Min(low, next);
                high = System.Math.Max(high, next);
            }
            if (Error != FieldMathError.None)
                return 0d;
            if (Peek() != ')')
                return Fail(FieldMathError.Syntax);
            _at++;
            if (name is "min" or "max")
                return name == "min" ? low : high;
            if (count != 1)
                return Fail(FieldMathError.Syntax);
            return name switch
            {
                "sqrt" => System.Math.Sqrt(first),
                "abs" => System.Math.Abs(first),
                "floor" => System.Math.Floor(first),
                "ceil" => System.Math.Ceiling(first),
                "round" => System.Math.Round(first, MidpointRounding.AwayFromZero),
                "sin" => SinDegrees(first),
                "cos" => CosDegrees(first),
                _ => TanDegrees(first),
            };
        }
    }
}
