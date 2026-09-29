// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lumora.Script;

// JSON as text: compact, or indented two spaces a level with a space after each colon. Text is escaped as
// little as JSON allows, so accents and emoji stay as they are; quotes, backslashes, control characters and
// half a surrogate pair are escaped. Writing stops as soon as the text passes its limit, so a value far too
// big costs no more than the limit to find out.
public static class ScriptJsonWriter
{
    private sealed class Full : Exception
    {
    }

    // The whole text, or ScriptLimitException past maxLength characters.
    public static string Write(ScriptJson value, bool indented, int maxLength)
    {
        var text = new StringBuilder();
        try
        {
            Value(text, value, indented, 0, maxLength);
        }
        catch (Full)
        {
            throw new ScriptLimitException($"text longer than {maxLength}");
        }
        return text.ToString();
    }

    // Compact, cut short with "..." past maxLength characters.
    public static string Preview(ScriptJson value, int maxLength)
    {
        var text = new StringBuilder();
        try
        {
            Value(text, value, false, 0, maxLength);
            return text.ToString();
        }
        catch (Full)
        {
            return text.ToString(0, System.Math.Min(text.Length, System.Math.Max(0, maxLength - 3))) + "...";
        }
    }

    private static void Value(StringBuilder text, ScriptJson value, bool indented, int level, int maxLength)
    {
        switch (value)
        {
            case ScriptJsonObject obj:
                if (obj.Count == 0)
                {
                    text.Append("{}");
                    break;
                }
                text.Append('{');
                bool firstMember = true;
                foreach (var (key, member) in obj.Members)
                {
                    if (!firstMember)
                        text.Append(',');
                    firstMember = false;
                    NewLine(text, indented, level + 1);
                    String(text, key);
                    text.Append(indented ? ": " : ":");
                    Check(text, maxLength);
                    Value(text, member, indented, level + 1, maxLength);
                }
                NewLine(text, indented, level);
                text.Append('}');
                break;
            case ScriptJsonArray array:
                if (array.Count == 0)
                {
                    text.Append("[]");
                    break;
                }
                text.Append('[');
                bool firstItem = true;
                foreach (var item in array.Items)
                {
                    if (!firstItem)
                        text.Append(',');
                    firstItem = false;
                    NewLine(text, indented, level + 1);
                    Value(text, item, indented, level + 1, maxLength);
                }
                NewLine(text, indented, level);
                text.Append(']');
                break;
            case ScriptJsonValue plain:
                switch (plain.Kind)
                {
                    case ScriptJsonKind.Bool:
                        text.Append(plain.Bool ? "true" : "false");
                        break;
                    case ScriptJsonKind.Number:
                        text.Append(plain.NumberText);
                        break;
                    case ScriptJsonKind.String:
                        String(text, plain.Text!);
                        break;
                    default:
                        text.Append("null");
                        break;
                }
                break;
        }
        Check(text, maxLength);
    }

    private static void NewLine(StringBuilder text, bool indented, int level)
    {
        if (!indented)
            return;
        text.Append('\n');
        text.Append(' ', 2 * level);
    }

    private static void Check(StringBuilder text, int maxLength)
    {
        if (text.Length > maxLength)
            throw new Full();
    }

    // A string with its quotes. Runs that need nothing are copied whole.
    internal static void String(StringBuilder text, string value)
    {
        text.Append('"');
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            string? escape = c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\b' => "\\b",
                '\f' => "\\f",
                _ when c < 0x20 => $"\\u{(int)c:x4}",
                _ when char.IsHighSurrogate(c) && (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) => $"\\u{(int)c:x4}",
                _ when char.IsLowSurrogate(c) && (i == 0 || !char.IsHighSurrogate(value[i - 1])) => $"\\u{(int)c:x4}",
                _ => null,
            };
            if (escape == null)
                continue;
            text.Append(value, start, i - start);
            text.Append(escape);
            start = i + 1;
        }
        text.Append(value, start, value.Length - start);
        text.Append('"');
    }
}
