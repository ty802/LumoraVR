// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;

namespace Lumora.Core.Assets;

// Blend and culling picked on a custom material. Both are render modes, which live in the shader's own
// source, so they reach the shader by rewriting its render_mode statement before it is compiled. The
// defaults, Opaque or Cutout and Back, leave whatever the shader says alone: an imported shader or a graph
// that chose its own modes keeps them, and only a mode picked on the material overrides one. Blend here is
// how colors add up; whether a pixel is see-through at all is up to the shader writing ALPHA. -xlinka
public static class ShaderRenderModes
{
    public static string Apply(string source, BlendMode blend, Culling culling)
    {
        string? blendMode = blend switch
        {
            BlendMode.Additive => "blend_add",
            BlendMode.Multiply => "blend_mul",
            BlendMode.Alpha or BlendMode.Transparent => "blend_mix",
            _ => null,
        };
        string? cullMode = culling switch
        {
            Culling.Front => "cull_front",
            Culling.None => "cull_disabled",
            _ => null,
        };
        if ((blendMode == null && cullMode == null) || string.IsNullOrEmpty(source))
            return source;

        var code = CodeMask(source);
        int typeAt = FindWord(source, code, "shader_type", 0);
        if (typeAt < 0)
            return source;
        int typeEnd = IndexOfCode(source, code, ';', typeAt);
        if (typeEnd < 0)
            return source;
        // A canvas_item shader draws both faces always and has no culling to pick.
        if (source.AsSpan(typeAt, typeEnd - typeAt).ToString().Contains("canvas_item", StringComparison.Ordinal))
            cullMode = null;
        if (blendMode == null && cullMode == null)
            return source;

        var statements = new List<(int Start, int End, List<string> Flags)>();
        for (int at = FindWord(source, code, "render_mode", 0); at >= 0;)
        {
            int end = IndexOfCode(source, code, ';', at);
            if (end < 0)
                return source;
            var flags = new List<string>();
            var text = new StringBuilder();
            for (int i = at + "render_mode".Length; i < end; i++)
            {
                if (code[i])
                    text.Append(source[i]);
            }
            foreach (var flag in text.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (blendMode != null && flag.StartsWith("blend_", StringComparison.Ordinal))
                    continue;
                if (cullMode != null && flag.StartsWith("cull_", StringComparison.Ordinal))
                    continue;
                flags.Add(flag);
            }
            statements.Add((at, end, flags));
            at = FindWord(source, code, "render_mode", end);
        }

        var ours = new List<string>();
        if (blendMode != null)
            ours.Add(blendMode);
        if (cullMode != null)
            ours.Add(cullMode);

        if (statements.Count == 0)
            return source[..(typeEnd + 1)] + "\nrender_mode " + string.Join(", ", ours) + ";" + source[(typeEnd + 1)..];

        var result = new StringBuilder(source.Length + 32);
        int copied = 0;
        for (int i = 0; i < statements.Count; i++)
        {
            var (start, end, flags) = statements[i];
            if (i == 0)
                flags.AddRange(ours);
            result.Append(source, copied, start - copied);
            if (flags.Count > 0)
                result.Append("render_mode ").Append(string.Join(", ", flags)).Append(';');
            copied = end + 1;
        }
        result.Append(source, copied, source.Length - copied);
        return result.ToString();
    }

    // True for each character that is code rather than inside a comment.
    private static bool[] CodeMask(string source)
    {
        var mask = new bool[source.Length];
        int i = 0;
        while (i < source.Length)
        {
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                    i++;
                continue;
            }
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                int close = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 ? source.Length : close + 2;
                continue;
            }
            mask[i] = true;
            i++;
        }
        return mask;
    }

    private static int FindWord(string source, bool[] code, string word, int from)
    {
        for (int at = source.IndexOf(word, from, StringComparison.Ordinal); at >= 0; at = source.IndexOf(word, at + 1, StringComparison.Ordinal))
        {
            bool inCode = true;
            for (int i = at; i < at + word.Length && inCode; i++)
                inCode = code[i];
            bool before = at == 0 || !IsIdentifier(source[at - 1]);
            bool after = at + word.Length >= source.Length || !IsIdentifier(source[at + word.Length]);
            if (inCode && before && after)
                return at;
        }
        return -1;
    }

    private static int IndexOfCode(string source, bool[] code, char c, int from)
    {
        for (int i = from; i < source.Length; i++)
        {
            if (source[i] == c && code[i])
                return i;
        }
        return -1;
    }

    private static bool IsIdentifier(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
