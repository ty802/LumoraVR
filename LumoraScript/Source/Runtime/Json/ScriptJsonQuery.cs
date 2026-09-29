// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;

namespace Lumora.Script;

// A JSONPath query, read once and run against a value. It knows
//   $                      the value itself; a query that starts without it starts there anyway
//   .name  ['name']        a key; ["a","b"] several
//   [2]  [-1]  [0,2]       an index, from the end when negative, several
//   [1:5]  [::2]  [::-1]   a slice: start, end and step, any left out
//   .*  [*]                every member or item
//   ..name  ..*  ..[0]     the same at every depth below, the value itself first
//   [?(@.price < 10)]      the members or items a filter keeps: == != < <= > >=, && || !, parentheses,
//                          @ for the one being tested and $ for the whole, numbers, 'text', true, false,
//                          null, and a path on its own for "has it"
// Matches come in document order. A comparison takes the first value its path finds; one side finding
// nothing equals only the other finding nothing too, and < and > compare numbers with numbers and text
// with text only. -xlinka
public sealed class ScriptJsonQuery
{
    private const int MaxNesting = 32;

    private static readonly ConcurrentDictionary<string, ScriptJsonQuery> Recent = new(StringComparer.Ordinal);

    private readonly Segment[] _segments;

    private ScriptJsonQuery(Segment[] segments)
    {
        _segments = segments;
    }

    public static bool TryParse(string? text, out ScriptJsonQuery query, out string error)
    {
        query = null!;
        error = string.Empty;
        text = (text ?? string.Empty).Trim();
        if (Recent.TryGetValue(text, out query!))
            return true;
        try
        {
            var parser = new Parser(text);
            query = new ScriptJsonQuery(parser.Query());
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
        if (Recent.Count >= 256)
            Recent.Clear();
        Recent.TryAdd(text, query);
        return true;
    }

    public void Run(ScriptJson root, List<ScriptJson> results)
    {
        results.Clear();
        results.AddRange(Select(_segments, root, root));
    }

    private static List<ScriptJson> Select(Segment[] segments, ScriptJson start, ScriptJson root)
    {
        var nodes = new List<ScriptJson> { start };
        foreach (var segment in segments)
        {
            var next = new List<ScriptJson>();
            foreach (var node in nodes)
            {
                if (segment.Descendant)
                    Descend(node, segment, root, next);
                else
                    Apply(node, segment, root, next);
            }
            nodes = next;
        }
        return nodes;
    }

    // The node, then everything below it, each before its own children.
    private static void Descend(ScriptJson node, Segment segment, ScriptJson root, List<ScriptJson> into)
    {
        Apply(node, segment, root, into);
        switch (node)
        {
            case ScriptJsonObject obj:
                foreach (var (_, member) in obj.Members)
                    Descend(member, segment, root, into);
                break;
            case ScriptJsonArray array:
                foreach (var item in array.Items)
                    Descend(item, segment, root, into);
                break;
        }
    }

    private static void Apply(ScriptJson node, Segment segment, ScriptJson root, List<ScriptJson> into)
    {
        foreach (var selector in segment.Selectors)
            selector.Select(node, root, into);
    }

    private sealed class Segment
    {
        public bool Descendant;
        public Selector[] Selectors = Array.Empty<Selector>();
    }

    private abstract class Selector
    {
        public abstract void Select(ScriptJson node, ScriptJson root, List<ScriptJson> into);
    }

    private sealed class NameSelector : Selector
    {
        private readonly string _name;

        public NameSelector(string name) => _name = name;

        public override void Select(ScriptJson node, ScriptJson root, List<ScriptJson> into)
        {
            if (node is ScriptJsonObject obj && obj.TryGet(_name, out var value))
                into.Add(value);
        }
    }

    private sealed class WildcardSelector : Selector
    {
        public override void Select(ScriptJson node, ScriptJson root, List<ScriptJson> into) => Children(node, into);
    }

    private sealed class IndexSelector : Selector
    {
        private readonly int _index;

        public IndexSelector(int index) => _index = index;

        public override void Select(ScriptJson node, ScriptJson root, List<ScriptJson> into)
        {
            if (node is not ScriptJsonArray array)
                return;
            var item = array.Get(_index < 0 ? array.Count + _index : _index);
            if (item != null)
                into.Add(item);
        }
    }

    private sealed class SliceSelector : Selector
    {
        private readonly int? _start;
        private readonly int? _end;
        private readonly int _step;

        public SliceSelector(int? start, int? end, int step)
        {
            _start = start;
            _end = end;
            _step = step;
        }

        public override void Select(ScriptJson node, ScriptJson root, List<ScriptJson> into)
        {
            if (node is not ScriptJsonArray array || _step == 0)
                return;
            int count = array.Count;
            int Normal(int i) => i < 0 ? count + i : i;
            if (_step > 0)
            {
                int lower = System.Math.Clamp(Normal(_start ?? 0), 0, count);
                int upper = System.Math.Clamp(Normal(_end ?? count), 0, count);
                for (long i = lower; i < upper; i += _step)
                    into.Add(array.Items[(int)i]);
            }
            else
            {
                int upper = System.Math.Clamp(Normal(_start ?? count - 1), -1, count - 1);
                int lower = System.Math.Clamp(_end.HasValue ? Normal(_end.Value) : -1, -1, count - 1);
                for (long i = upper; lower < i; i += _step)
                    into.Add(array.Items[(int)i]);
            }
        }
    }

    private sealed class FilterSelector : Selector
    {
        private readonly Expression _test;

        public FilterSelector(Expression test) => _test = test;

        public override void Select(ScriptJson node, ScriptJson root, List<ScriptJson> into)
        {
            var children = new List<ScriptJson>();
            Children(node, children);
            foreach (var child in children)
            {
                if (_test.Test(child, root))
                    into.Add(child);
            }
        }
    }

    private static void Children(ScriptJson node, List<ScriptJson> into)
    {
        switch (node)
        {
            case ScriptJsonObject obj:
                foreach (var (_, member) in obj.Members)
                    into.Add(member);
                break;
            case ScriptJsonArray array:
                into.AddRange(array.Items);
                break;
        }
    }

    private abstract class Expression
    {
        public abstract bool Test(ScriptJson current, ScriptJson root);
    }

    private sealed class Either : Expression
    {
        private readonly Expression _a;
        private readonly Expression _b;

        public Either(Expression a, Expression b)
        {
            _a = a;
            _b = b;
        }

        public override bool Test(ScriptJson current, ScriptJson root) => _a.Test(current, root) || _b.Test(current, root);
    }

    private sealed class Both : Expression
    {
        private readonly Expression _a;
        private readonly Expression _b;

        public Both(Expression a, Expression b)
        {
            _a = a;
            _b = b;
        }

        public override bool Test(ScriptJson current, ScriptJson root) => _a.Test(current, root) && _b.Test(current, root);
    }

    private sealed class Not : Expression
    {
        private readonly Expression _inner;

        public Not(Expression inner) => _inner = inner;

        public override bool Test(ScriptJson current, ScriptJson root) => !_inner.Test(current, root);
    }

    private sealed class Exists : Expression
    {
        private readonly Operand _path;

        public Exists(Operand path) => _path = path;

        public override bool Test(ScriptJson current, ScriptJson root) => _path.Value(current, root) != null;
    }

    private sealed class Compare : Expression
    {
        private readonly Operand _left;
        private readonly string _op;
        private readonly Operand _right;

        public Compare(Operand left, string op, Operand right)
        {
            _left = left;
            _op = op;
            _right = right;
        }

        public override bool Test(ScriptJson current, ScriptJson root)
        {
            var a = _left.Value(current, root);
            var b = _right.Value(current, root);
            return _op switch
            {
                "==" => Equal(a, b),
                "!=" => !Equal(a, b),
                "<" => Less(a, b),
                "<=" => Less(a, b) || Equal(a, b),
                ">" => Less(b, a),
                _ => Less(b, a) || Equal(a, b),
            };
        }

        private static bool Equal(ScriptJson? a, ScriptJson? b) => a == null ? b == null : a.Equals(b);

        private static bool Less(ScriptJson? a, ScriptJson? b)
        {
            if (a is not ScriptJsonValue x || b is not ScriptJsonValue y || x.Kind != y.Kind)
                return false;
            return x.Kind switch
            {
                ScriptJsonKind.Number => x.Number < y.Number,
                ScriptJsonKind.String => string.CompareOrdinal(x.Text, y.Text) < 0,
                _ => false,
            };
        }
    }

    // What one side of a comparison stands for; null is nothing found.
    private abstract class Operand
    {
        public abstract ScriptJson? Value(ScriptJson current, ScriptJson root);
    }

    private sealed class Literal : Operand
    {
        private readonly ScriptJson _value;

        public Literal(ScriptJson value) => _value = value;

        public override ScriptJson? Value(ScriptJson current, ScriptJson root) => _value;
    }

    private sealed class PathOperand : Operand
    {
        private readonly bool _fromRoot;
        private readonly Segment[] _segments;

        public PathOperand(bool fromRoot, Segment[] segments)
        {
            _fromRoot = fromRoot;
            _segments = segments;
        }

        public override ScriptJson? Value(ScriptJson current, ScriptJson root)
        {
            var found = Select(_segments, _fromRoot ? root : current, root);
            return found.Count > 0 ? found[0] : null;
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private int _at;
        private int _nesting;

        public Parser(string text) => _text = text;

        public Segment[] Query()
        {
            var segments = new List<Segment>();
            if (Peek() == '$')
            {
                _at++;
            }
            else if (_at < _text.Length && Peek() is not ('.' or '['))
            {
                // A bare first name, the way a path starts.
                segments.Add(new Segment { Selectors = new Selector[] { new NameSelector(Name()) } });
            }
            segments.AddRange(Segments());
            if (_at < _text.Length)
                throw Fail($"'{_text[_at]}' at {_at} is not part of a query");
            return segments.ToArray();
        }

        private List<Segment> Segments()
        {
            var segments = new List<Segment>();
            while (_at < _text.Length)
            {
                if (Peek() == '.')
                {
                    _at++;
                    bool descendant = Peek() == '.';
                    if (descendant)
                        _at++;
                    Selector[] selectors;
                    if (Peek() == '*')
                    {
                        _at++;
                        selectors = new Selector[] { new WildcardSelector() };
                    }
                    else if (descendant && Peek() == '[')
                    {
                        selectors = Brackets();
                    }
                    else
                    {
                        selectors = new Selector[] { new NameSelector(Name()) };
                    }
                    segments.Add(new Segment { Descendant = descendant, Selectors = selectors });
                }
                else if (Peek() == '[')
                {
                    segments.Add(new Segment { Selectors = Brackets() });
                }
                else
                {
                    break;
                }
            }
            return segments;
        }

        private string Name()
        {
            int start = _at;
            while (_at < _text.Length && _text[_at] is not ('.' or '[' or ']' or '\'' or '"' or '=' or '!' or '<' or '>' or '&' or '|' or '('
                       or ')' or ',' or '*') && !char.IsWhiteSpace(_text[_at]))
                _at++;
            if (_at == start)
                throw Fail($"a name is missing at {start}");
            return _text[start.._at];
        }

        private Selector[] Brackets()
        {
            _at++;
            var selectors = new List<Selector>();
            while (true)
            {
                Space();
                selectors.Add(Selector());
                Space();
                if (Peek() == ',')
                {
                    _at++;
                    continue;
                }
                if (Peek() != ']')
                    throw Fail($"a ] is missing at {_at}");
                _at++;
                return selectors.ToArray();
            }
        }

        private Selector Selector()
        {
            char c = Peek();
            if (c is '\'' or '"')
                return new NameSelector(Quoted());
            if (c == '*')
            {
                _at++;
                return new WildcardSelector();
            }
            if (c == '?')
            {
                _at++;
                Space();
                return new FilterSelector(Or());
            }
            int? start = Int();
            Space();
            if (Peek() != ':')
            {
                if (start == null)
                    throw Fail($"a selector is missing at {_at}");
                return new IndexSelector(start.Value);
            }
            _at++;
            Space();
            int? end = Int();
            Space();
            int step = 1;
            if (Peek() == ':')
            {
                _at++;
                Space();
                step = Int() ?? 1;
            }
            return new SliceSelector(start, end, step);
        }

        private int? Int()
        {
            int start = _at;
            if (Peek() == '-')
                _at++;
            while (_at < _text.Length && char.IsAsciiDigit(_text[_at]))
                _at++;
            if (_at == start)
                return null;
            if (!int.TryParse(_text.AsSpan(start, _at - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
                throw Fail($"the number at {start} is not a whole number in range");
            return value;
        }

        private string Quoted()
        {
            if (!ScriptJsonPath.ReadQuoted(_text, ref _at, out var value, out var error))
                throw Fail(error);
            return value;
        }

        private Expression Or()
        {
            var left = And();
            while (Take("||"))
                left = new Either(left, And());
            return left;
        }

        private Expression And()
        {
            var left = Unary();
            while (Take("&&"))
                left = new Both(left, Unary());
            return left;
        }

        private Expression Unary()
        {
            Space();
            if (Peek() == '!' && Peek(1) != '=')
            {
                _at++;
                return new Not(Nested(Unary));
            }
            if (Peek() == '(')
            {
                _at++;
                var inner = Nested(Or);
                if (!Take(")"))
                    throw Fail($"a ) is missing at {_at}");
                return inner;
            }
            var left = Operand();
            Space();
            foreach (var op in new[] { "==", "!=", "<=", ">=", "<", ">" })
            {
                if (Take(op))
                    return new Compare(left, op, Operand());
            }
            if (left is not PathOperand)
                throw Fail($"a comparison is missing at {_at}");
            return new Exists(left);
        }

        private Expression Nested(Func<Expression> read)
        {
            if (++_nesting > MaxNesting)
                throw Fail($"a filter nests at most {MaxNesting} deep");
            var expression = read();
            _nesting--;
            return expression;
        }

        private Operand Operand()
        {
            Space();
            char c = Peek();
            if (c is '@' or '$')
            {
                _at++;
                if (++_nesting > MaxNesting)
                    throw Fail($"a filter nests at most {MaxNesting} deep");
                var segments = Segments();
                _nesting--;
                return new PathOperand(c == '$', segments.ToArray());
            }
            if (c is '\'' or '"')
                return new Literal(ScriptJsonValue.Unowned(Quoted()));
            if (Word("true"))
                return new Literal(ScriptJsonValue.True);
            if (Word("false"))
                return new Literal(ScriptJsonValue.False);
            if (Word("null"))
                return new Literal(ScriptJsonValue.Null);
            int start = _at;
            if (Peek() == '-')
                _at++;
            while (_at < _text.Length && (char.IsAsciiDigit(_text[_at]) || _text[_at] is '.' or 'e' or 'E'
                       || (_text[_at] is '+' or '-' && _at > start && _text[_at - 1] is 'e' or 'E')))
                _at++;
            var number = _text.AsSpan(start, _at - start);
            if (long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long whole))
                return new Literal(ScriptJsonValue.Unowned(whole));
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double real) && double.IsFinite(real))
                return new Literal(ScriptJsonValue.Unowned(real));
            throw Fail($"a value is missing at {start}");
        }

        private bool Word(string word)
        {
            if (string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0)
                return false;
            int after = _at + word.Length;
            if (after < _text.Length && (char.IsAsciiLetterOrDigit(_text[after]) || _text[after] == '_'))
                return false;
            _at = after;
            return true;
        }

        private bool Take(string token)
        {
            Space();
            if (string.CompareOrdinal(_text, _at, token, 0, token.Length) != 0)
                return false;
            _at += token.Length;
            return true;
        }

        private void Space() => ScriptJsonPath.SkipSpace(_text, ref _at);

        private char Peek(int ahead = 0) => _at + ahead < _text.Length ? _text[_at + ahead] : '\0';

        private static FormatException Fail(string message) => new(message);
    }
}
