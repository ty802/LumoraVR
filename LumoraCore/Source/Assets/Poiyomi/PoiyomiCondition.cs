// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;

namespace Lumora.Core.Assets.Poiyomi;

// Poiyomi's "show only when" expressions: comparisons of a property against a number joined with && and ||,
// with ! and parentheses ("(_LightingMode==1)", "_RimStyle==0||_RimStyle==1", "(_PathALAutoCorrelatorA > 0)").
// A bare property name is true when nonzero; anything that does not parse shows the row.
public static class PoiyomiCondition
{
    public static bool Holds(string? expression, Func<string, float> value)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return true;
        var reader = new Reader(expression, value);
        try
        {
            bool result = reader.Or();
            return reader.AtEnd ? result : true;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<char> _text;
        private readonly Func<string, float> _value;
        private int _at;

        public Reader(string text, Func<string, float> value)
        {
            _text = text.AsSpan();
            _value = value;
            _at = 0;
        }

        public bool AtEnd
        {
            get
            {
                Skip();
                return _at >= _text.Length;
            }
        }

        public bool Or()
        {
            bool result = And();
            while (Take("||"))
                result |= And();
            return result;
        }

        private bool And()
        {
            bool result = Unary();
            while (Take("&&"))
                result &= Unary();
            return result;
        }

        private bool Unary()
        {
            if (Take("!"))
                return !Unary();
            if (Take("("))
            {
                bool inner = Or();
                if (!Take(")"))
                    throw new FormatException();
                return inner;
            }
            return Comparison();
        }

        private bool Comparison()
        {
            float left = Operand();
            string? op = Take("==") ? "==" : Take("!=") ? "!=" : Take(">=") ? ">=" : Take("<=") ? "<=" : Take(">") ? ">" : Take("<") ? "<" : null;
            if (op == null)
                return left != 0f;
            float right = Operand();
            return op switch
            {
                "==" => MathF.Abs(left - right) < 1e-4f,
                "!=" => MathF.Abs(left - right) >= 1e-4f,
                ">=" => left >= right,
                "<=" => left <= right,
                ">" => left > right,
                _ => left < right,
            };
        }

        private float Operand()
        {
            Skip();
            int start = _at;
            if (_at < _text.Length && (_text[_at] == '_' || char.IsLetter(_text[_at])))
            {
                while (_at < _text.Length && (_text[_at] == '_' || char.IsLetterOrDigit(_text[_at])))
                    _at++;
                return _value(_text[start.._at].ToString());
            }
            while (_at < _text.Length && (char.IsDigit(_text[_at]) || _text[_at] is '.' or '-' or '+'))
                _at++;
            if (_at == start || !float.TryParse(_text[start.._at], NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                throw new FormatException();
            return number;
        }

        private bool Take(string token)
        {
            Skip();
            if (_text.Length - _at >= token.Length && _text.Slice(_at, token.Length).SequenceEqual(token.AsSpan()))
            {
                _at += token.Length;
                return true;
            }
            return false;
        }

        private void Skip()
        {
            while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
                _at++;
        }
    }
}
