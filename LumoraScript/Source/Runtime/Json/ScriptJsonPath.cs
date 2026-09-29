// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lumora.Script;

// A path to one value inside JSON: names and indexes, as in users[0].name, $.a["b.c"] or [2]. A leading $
// is the value itself; a plain name runs to the next dot or bracket, and a bracketed one is quoted with '
// or " and takes \ escapes. A negative index counts from the end.
//
// Setting makes what is missing on the way, an object for a name and an array for index 0, and an index
// one past the end appends. It never replaces a value of the wrong kind to get through it: that fails and
// leaves everything as it was. -xlinka
public sealed class ScriptJsonPath
{
    public const int MaxLength = 1024;

    // What a path goes through; an index step has a null name.
    public readonly record struct Step(string? Name, int Index);

    private static readonly ConcurrentDictionary<string, ScriptJsonPath> Recent = new(StringComparer.Ordinal);

    private readonly Step[] _steps;

    private ScriptJsonPath(Step[] steps)
    {
        _steps = steps;
    }

    public IReadOnlyList<Step> Steps => _steps;

    // Read once per text: a script that walks the same path every frame parses it once.
    public static bool TryParse(string? text, out ScriptJsonPath path, out string error)
    {
        path = null!;
        error = string.Empty;
        text ??= string.Empty;
        if (Recent.TryGetValue(text, out path!))
            return true;
        if (!TryRead(text, out var steps, out error))
            return false;
        path = new ScriptJsonPath(steps.ToArray());
        if (Recent.Count >= 256)
            Recent.Clear();
        Recent.TryAdd(text, path);
        return true;
    }

    private static bool TryRead(string text, out List<Step> steps, out string error)
    {
        steps = new List<Step>();
        error = string.Empty;
        int at = 0;
        if (at < text.Length && text[at] == '$')
            at++;
        bool first = true;
        while (at < text.Length)
        {
            char c = text[at];
            if (c == '.')
            {
                at++;
                if (!ReadPlainName(text, ref at, out var name))
                {
                    error = $"a name is missing after the dot at {at}";
                    return false;
                }
                steps.Add(new Step(name, 0));
            }
            else if (c == '[')
            {
                at++;
                SkipSpace(text, ref at);
                if (at < text.Length && text[at] is '\'' or '"')
                {
                    if (!ReadQuoted(text, ref at, out var name, out error))
                        return false;
                    steps.Add(new Step(name, 0));
                }
                else
                {
                    int start = at;
                    if (at < text.Length && text[at] == '-')
                        at++;
                    while (at < text.Length && char.IsAsciiDigit(text[at]))
                        at++;
                    if (!int.TryParse(text.AsSpan(start, at - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int index))
                    {
                        error = $"an index or a quoted name belongs in the brackets at {start}";
                        return false;
                    }
                    steps.Add(new Step(null, index));
                }
                SkipSpace(text, ref at);
                if (at >= text.Length || text[at] != ']')
                {
                    error = $"a ] is missing at {at}";
                    return false;
                }
                at++;
            }
            else if (first && ReadPlainName(text, ref at, out var name))
            {
                steps.Add(new Step(name, 0));
            }
            else
            {
                error = $"'{c}' at {at} is not part of a path";
                return false;
            }
            first = false;
        }
        return true;
    }

    private static bool ReadPlainName(string text, ref int at, out string name)
    {
        int start = at;
        while (at < text.Length && text[at] is not ('.' or '[' or ']' or '\'' or '"') && !char.IsWhiteSpace(text[at]))
            at++;
        name = text[start..at];
        return name.Length > 0;
    }

    internal static bool ReadQuoted(string text, ref int at, out string value, out string error)
    {
        value = string.Empty;
        error = string.Empty;
        char quote = text[at++];
        var built = new StringBuilder();
        while (at < text.Length && text[at] != quote)
        {
            char c = text[at++];
            if (c != '\\')
            {
                built.Append(c);
                continue;
            }
            if (at >= text.Length)
                break;
            char escaped = text[at++];
            switch (escaped)
            {
                case 'n': built.Append('\n'); break;
                case 't': built.Append('\t'); break;
                case 'r': built.Append('\r'); break;
                case 'b': built.Append('\b'); break;
                case 'f': built.Append('\f'); break;
                case 'u' when at + 4 <= text.Length
                              && ushort.TryParse(text.AsSpan(at, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code):
                    built.Append((char)code);
                    at += 4;
                    break;
                default: built.Append(escaped); break;
            }
        }
        if (at >= text.Length)
        {
            error = "a quoted name is not closed";
            return false;
        }
        at++;
        value = built.ToString();
        return true;
    }

    internal static void SkipSpace(string text, ref int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
            at++;
    }

    public bool TryGet(ScriptJson root, out ScriptJson value)
    {
        value = root;
        foreach (var step in _steps)
        {
            if (!TryStep(value, step, out value))
                return false;
        }
        return true;
    }

    private static bool TryStep(ScriptJson from, Step step, out ScriptJson value)
    {
        value = null!;
        if (step.Name != null)
            return from is ScriptJsonObject obj && obj.TryGet(step.Name, out value);
        if (from is not ScriptJsonArray array)
            return false;
        int index = step.Index < 0 ? array.Count + step.Index : step.Index;
        var found = array.Get(index);
        value = found!;
        return found != null;
    }

    public bool TrySet(ScriptCollectionBudget budget, ScriptJsonObject root, ScriptJson value, out ScriptJsonObject result)
    {
        result = root;
        if (_steps.Length == 0 || !TrySetAt(budget, root, 0, value, out var changed) || changed is not ScriptJsonObject obj)
            return false;
        result = obj;
        return true;
    }

    // container with the rest of the path from step on set to value; null container is one made to fit.
    private bool TrySetAt(ScriptCollectionBudget budget, ScriptJson? container, int step, ScriptJson value, out ScriptJson result)
    {
        result = null!;
        var at = _steps[step];
        bool last = step == _steps.Length - 1;
        if (at.Name != null)
        {
            var obj = container switch
            {
                null => ScriptJsonObject.Empty,
                ScriptJsonObject existing => existing,
                _ => null,
            };
            if (obj == null)
                return false;
            ScriptJson inner = value;
            if (!last && !TrySetAt(budget, obj.TryGet(at.Name, out var below) ? below : null, step + 1, value, out inner))
                return false;
            result = obj.Set(budget, at.Name, inner);
            return true;
        }

        var array = container switch
        {
            null when at.Index == 0 => ScriptJsonArray.Empty,
            ScriptJsonArray existing => existing,
            _ => null,
        };
        if (array == null)
            return false;
        int index = at.Index < 0 ? array.Count + at.Index : at.Index;
        if (index < 0 || index > array.Count)
            return false;
        var item = value;
        if (!last && !TrySetAt(budget, array.Get(index), step + 1, value, out item))
            return false;
        if (index == array.Count)
            result = array.Add(budget, item);
        else if (array.TrySet(budget, index, item, out var set))
            result = set;
        return result != null;
    }

    public bool TryRemove(ScriptCollectionBudget budget, ScriptJsonObject root, out ScriptJsonObject result)
    {
        result = root;
        if (_steps.Length == 0 || !TryRemoveAt(budget, root, 0, out var changed) || changed is not ScriptJsonObject obj)
            return false;
        result = obj;
        return true;
    }

    private bool TryRemoveAt(ScriptCollectionBudget budget, ScriptJson container, int step, out ScriptJson result)
    {
        result = container;
        var at = _steps[step];
        bool last = step == _steps.Length - 1;
        if (at.Name != null)
        {
            if (container is not ScriptJsonObject obj || !obj.TryGet(at.Name, out var below))
                return false;
            if (last)
            {
                result = obj.Remove(budget, at.Name, out _);
                return true;
            }
            if (!TryRemoveAt(budget, below, step + 1, out var changed))
                return false;
            result = obj.Set(budget, at.Name, changed);
            return true;
        }
        if (container is not ScriptJsonArray array)
            return false;
        int index = at.Index < 0 ? array.Count + at.Index : at.Index;
        if (last)
        {
            if (!array.TryRemoveAt(budget, index, out var removed))
                return false;
            result = removed;
            return true;
        }
        var item = array.Get(index);
        if (item == null || !TryRemoveAt(budget, item, step + 1, out var inner) || !array.TrySet(budget, index, inner, out var set))
            return false;
        result = set;
        return true;
    }
}
