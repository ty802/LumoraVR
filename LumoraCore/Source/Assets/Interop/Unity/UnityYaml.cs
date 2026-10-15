// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lumora.Core.Assets.Interop.Unity;

// Unity's text serialization. Each file is a run of YAML documents headed "--- !u!<class> &<fileID>"
// (plus " stripped" for a stand-in of an object that lives in another prefab), and inside them only a
// small subset of YAML appears: block mappings, block sequences written at the SAME indent as the key
// that owns them, flow mappings that wrap onto the next line when long, and plain scalars that carry on
// onto deeper-indented lines. That subset is all this reads. -xlinka 
public abstract class UnityNode
{
    public virtual UnityNode? this[string key] => null;

    public virtual string? Text => null;

    public long Long(long fallback = 0)
        => long.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public int Int(int fallback = 0)
        => int.TryParse(Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public float Float(float fallback = 0f)
        => float.TryParse(Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    // {fileID: n, guid: g, type: t}: a pointer to an object in this file (no guid) or in another asset.
    public UnityRef Ref => new(this["fileID"]?.Long() ?? 0, this["guid"]?.Text, this["type"]?.Int() ?? 0);
}

public readonly record struct UnityRef(long FileId, string? Guid, int Type)
{
    public bool IsNull => FileId == 0 && string.IsNullOrEmpty(Guid);
    public bool IsLocal => string.IsNullOrEmpty(Guid) && FileId != 0;
}

public sealed class UnityScalar : UnityNode
{
    public readonly string Value;
    public UnityScalar(string value) => Value = value;
    public override string? Text => Value;
    public override string ToString() => Value;
}

public sealed class UnityMap : UnityNode
{
    public readonly List<KeyValuePair<string, UnityNode>> Entries = new();

    public override UnityNode? this[string key]
    {
        get
        {
            foreach (var pair in Entries)
                if (pair.Key == key)
                    return pair.Value;
            return null;
        }
    }

    public override string ToString() => "{" + string.Join(", ", Entries.ConvertAll(e => e.Key + ": " + e.Value)) + "}";
}

public sealed class UnitySeq : UnityNode
{
    public readonly List<UnityNode> Items = new();
    public override string ToString() => "[" + string.Join(", ", Items) + "]";
}

public sealed class UnityDocument
{
    public int ClassId;
    public long FileId;
    public bool Stripped;
    public string TypeName = "";
    public UnityMap Body = new();

    public UnityNode? this[string key] => Body[key];
}

public static class UnityYaml
{
    public static List<UnityDocument> Parse(string text)
    {
        var documents = new List<UnityDocument>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];
            if (!line.StartsWith("--- ", StringComparison.Ordinal))
            {
                i++;
                continue;
            }
            var doc = ParseHeader(line);
            i++;
            int end = i;
            while (end < lines.Length && !lines[end].StartsWith("--- ", StringComparison.Ordinal))
                end++;
            if (doc != null)
            {
                // "TypeName:" at column zero, its fields one level in.
                for (int k = i; k < end; k++)
                {
                    var head = lines[k];
                    if (head.Length == 0 || head[0] == ' ' || head[0] == '%')
                        continue;
                    int colon = head.IndexOf(':');
                    doc.TypeName = colon > 0 ? head[..colon].Trim() : head.Trim();
                    var reader = new Reader(lines, k + 1, end);
                    if (reader.Peek(out int indent) && indent > 0)
                        doc.Body = reader.ParseMap(indent);
                    break;
                }
                documents.Add(doc);
            }
            i = end;
        }
        return documents;
    }

    // A file that is a single mapping without document headers: asset .meta files.
    public static UnityMap ParseMapping(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var reader = new Reader(lines, 0, lines.Length);
        return reader.Peek(out int indent) ? reader.ParseMap(indent) : new UnityMap();
    }

    private static UnityDocument? ParseHeader(string line)
    {
        // --- !u!<class> &<fileID>[ stripped]
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[1].StartsWith("!u!", StringComparison.Ordinal) || !parts[2].StartsWith('&'))
            return null;
        if (!int.TryParse(parts[1].AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int classId))
            return null;
        if (!long.TryParse(parts[2].AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out long fileId))
            return null;
        return new UnityDocument
        {
            ClassId = classId,
            FileId = fileId,
            Stripped = parts.Length > 3 && parts[3] == "stripped",
        };
    }

    private sealed class Reader
    {
        private readonly string[] _lines;
        private readonly int _end;
        private int _i;

        public Reader(string[] lines, int start, int end)
        {
            _lines = lines;
            _i = start;
            _end = end;
        }

        // The next line with content and its indent, without consuming it.
        public bool Peek(out int indent)
        {
            while (_i < _end)
            {
                var line = _lines[_i];
                int n = Indent(line);
                if (n < line.Length)
                {
                    indent = n;
                    return true;
                }
                _i++;
            }
            indent = 0;
            return false;
        }

        private static int Indent(string line)
        {
            int n = 0;
            while (n < line.Length && line[n] == ' ')
                n++;
            return n;
        }

        private static bool IsItem(string line, int indent)
            => indent < line.Length && line[indent] == '-' && (indent + 1 == line.Length || line[indent + 1] == ' ');

        private UnityNode ParseBlock(int indent)
        {
            return IsItem(_lines[_i], indent) ? ParseSeq(indent) : ParseMap(indent);
        }

        public UnityMap ParseMap(int indent)
        {
            var map = new UnityMap();
            while (Peek(out int n) && n == indent && !IsItem(_lines[_i], n))
            {
                var line = _lines[_i];
                int colon = FindKeyColon(line, n);
                if (colon < 0)
                {
                    // Not a key: a stray continuation, skip it rather than lose the rest of the mapping.
                    _i++;
                    continue;
                }
                string key = Unquote(line.Substring(n, colon - n).Trim());
                string rest = line[(colon + 1)..].Trim();
                _i++;
                map.Entries.Add(new KeyValuePair<string, UnityNode>(key, ParseValue(rest, indent)));
            }
            return map;
        }

        private UnitySeq ParseSeq(int indent)
        {
            var seq = new UnitySeq();
            while (Peek(out int n) && n == indent && IsItem(_lines[_i], n))
            {
                var line = _lines[_i];
                string content = n + 1 < line.Length ? line[(n + 2 > line.Length ? line.Length : n + 2)..] : "";
                if (content.Trim().Length == 0)
                {
                    _i++;
                    seq.Items.Add(Peek(out int child) && child > indent ? ParseBlock(child) : new UnityScalar(""));
                    continue;
                }
                if (content[0] is '{' or '[' or '"' or '\'' || FindKeyColon(content, 0) < 0)
                {
                    _i++;
                    seq.Items.Add(ParseValue(content.Trim(), indent));
                    continue;
                }
                // "- key: value" opens a mapping whose first entry sits on the dash line: read it as if
                // the dash were spaces, one level in.
                _lines[_i] = new string(' ', n + 2) + content;
                seq.Items.Add(ParseMap(n + 2));
            }
            return seq;
        }

        // The value after "key:". Empty means a nested block, which may be a sequence at the key's own
        // indent; flow collections and quoted strings may run onto later lines; plain scalars continue
        // onto any deeper-indented line.
        private UnityNode ParseValue(string rest, int indent)
        {
            if (rest.Length == 0)
            {
                if (Peek(out int n))
                {
                    if (n > indent)
                        return ParseBlock(n);
                    if (n == indent && IsItem(_lines[_i], n))
                        return ParseSeq(n);
                }
                return new UnityScalar("");
            }
            if (rest[0] is '{' or '[')
            {
                var text = new StringBuilder(rest);
                while (!Balanced(text) && _i < _end)
                {
                    text.Append(' ').Append(_lines[_i].Trim());
                    _i++;
                }
                int pos = 0;
                return ParseFlow(text.ToString(), ref pos);
            }
            if (rest[0] is '"' or '\'')
            {
                var text = new StringBuilder(rest);
                while (!QuoteClosed(text.ToString()) && _i < _end)
                {
                    text.Append(' ').Append(_lines[_i].Trim());
                    _i++;
                }
                return new UnityScalar(Unquote(text.ToString().Trim()));
            }
            var plain = new StringBuilder(rest);
            while (_i < _end)
            {
                var next = _lines[_i];
                int n = Indent(next);
                if (n >= next.Length || n <= indent)
                    break;
                plain.Append(' ').Append(next.Trim());
                _i++;
            }
            return new UnityScalar(plain.ToString());
        }

        // The ": " (or a trailing ":") that ends a key, outside quotes. -1 when the line is not a key.
        private static int FindKeyColon(string line, int start)
        {
            char quote = '\0';
            for (int k = start; k < line.Length; k++)
            {
                char c = line[k];
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                    continue;
                }
                if (k == start && (c == '"' || c == '\''))
                {
                    quote = c;
                    continue;
                }
                if (c == '{' || c == '[')
                    return -1;
                if (c == ':' && (k + 1 == line.Length || line[k + 1] == ' '))
                    return k;
            }
            return -1;
        }

        private static bool Balanced(StringBuilder text)
        {
            int depth = 0;
            char quote = '\0';
            for (int k = 0; k < text.Length; k++)
            {
                char c = text[k];
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                    continue;
                }
                if (c == '"' || c == '\'')
                    quote = c;
                else if (c == '{' || c == '[')
                    depth++;
                else if (c == '}' || c == ']')
                    depth--;
            }
            return depth <= 0;
        }

        private static bool QuoteClosed(string text)
        {
            char quote = text[0];
            for (int k = 1; k < text.Length; k++)
            {
                if (quote == '"' && text[k] == '\\')
                {
                    k++;
                    continue;
                }
                if (text[k] == quote)
                {
                    if (quote == '\'' && k + 1 < text.Length && text[k + 1] == '\'')
                    {
                        k++;
                        continue;
                    }
                    return true;
                }
            }
            return false;
        }
    }

    private static UnityNode ParseFlow(string s, ref int pos)
    {
        SkipSpace(s, ref pos);
        if (pos >= s.Length)
            return new UnityScalar("");
        if (s[pos] == '{')
        {
            pos++;
            var map = new UnityMap();
            while (true)
            {
                SkipSpace(s, ref pos);
                if (pos >= s.Length)
                    break;
                if (s[pos] == '}')
                {
                    pos++;
                    break;
                }
                string key = ReadFlowScalar(s, ref pos, keyMode: true);
                SkipSpace(s, ref pos);
                UnityNode value = new UnityScalar("");
                if (pos < s.Length && s[pos] == ':')
                {
                    pos++;
                    value = ParseFlow(s, ref pos);
                }
                map.Entries.Add(new KeyValuePair<string, UnityNode>(key, value));
                SkipSpace(s, ref pos);
                if (pos < s.Length && s[pos] == ',')
                    pos++;
            }
            return map;
        }
        if (s[pos] == '[')
        {
            pos++;
            var seq = new UnitySeq();
            while (true)
            {
                SkipSpace(s, ref pos);
                if (pos >= s.Length)
                    break;
                if (s[pos] == ']')
                {
                    pos++;
                    break;
                }
                seq.Items.Add(ParseFlow(s, ref pos));
                SkipSpace(s, ref pos);
                if (pos < s.Length && s[pos] == ',')
                    pos++;
            }
            return seq;
        }
        return new UnityScalar(ReadFlowScalar(s, ref pos, keyMode: false));
    }

    private static string ReadFlowScalar(string s, ref int pos, bool keyMode)
    {
        SkipSpace(s, ref pos);
        if (pos < s.Length && (s[pos] == '"' || s[pos] == '\''))
        {
            char quote = s[pos];
            int start = pos;
            pos++;
            while (pos < s.Length)
            {
                if (quote == '"' && s[pos] == '\\')
                {
                    pos += 2;
                    continue;
                }
                if (s[pos] == quote)
                {
                    pos++;
                    break;
                }
                pos++;
            }
            return Unquote(s[start..System.Math.Min(pos, s.Length)]);
        }
        int begin = pos;
        while (pos < s.Length)
        {
            char c = s[pos];
            if (c == ',' || c == '}' || c == ']')
                break;
            if (keyMode && c == ':')
                break;
            pos++;
        }
        return s[begin..pos].Trim();
    }

    private static void SkipSpace(string s, ref int pos)
    {
        while (pos < s.Length && s[pos] == ' ')
            pos++;
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'')
            return s[1..^1].Replace("''", "'");
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            var body = s[1..^1];
            if (body.IndexOf('\\') < 0)
                return body;
            var sb = new StringBuilder(body.Length);
            for (int k = 0; k < body.Length; k++)
            {
                char c = body[k];
                if (c != '\\' || k + 1 >= body.Length)
                {
                    sb.Append(c);
                    continue;
                }
                char e = body[++k];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '0': sb.Append('\0'); break;
                    case ' ': sb.Append(' '); break;
                    case 'u' when k + 4 < body.Length
                        && int.TryParse(body.AsSpan(k + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code):
                        sb.Append((char)code);
                        k += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }
        return s;
    }
}
