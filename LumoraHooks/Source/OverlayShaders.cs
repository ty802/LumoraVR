// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

namespace Lumora.Godot.Hooks;

// A shader that draws in the transparent pass, made from one that draws in the opaque pass, for a mesh
// lifted over the dash. The opaque pass is drawn before every transparent surface whatever priority or
// offset a material carries, and the dash is a transparent surface that ignores depth, so an opaque hand
// stayed under it however high it was lifted. Our material families switch to their transparent build on
// TRANSPARENT_PASS; a shader with its fragment in the file gets an alpha of one written at its end. The
// depth prepass keeps a lifted mesh's own faces in order. Built once per shader and kept. -xlinka
//
// Compiled by CustomShaderCache on its builders, as every custom shader text is: the lifted hand's lit
// transparent build was a full compile on the main thread the first time the dash opened in VR. -xlinka
internal static class OverlayShaders
{
    // The overlay source per (shader, depth test); null when the shader needs no overlay build.
    private static readonly Dictionary<(Shader, bool), string?> Codes = new();
    private static readonly Dictionary<(Shader, bool), Shader> Made = new();
    private static readonly Regex WritesAlpha = new(@"\bALPHA\s*=", RegexOptions.Compiled);
    private static readonly Regex RenderMode = new(@"render_mode\s+([^;]*);", RegexOptions.Compiled);
    private static readonly Regex ShaderType = new(@"shader_type\s+spatial\s*;", RegexOptions.Compiled);
    private static readonly Regex Include = new(@"^[ \t]*#include", RegexOptions.Compiled | RegexOptions.Multiline);

    // The overlay build of source when it is compiled. Null either when source draws over the dash as it is
    // or while the build compiles, and then ready hears it on a later frame.
    public static Shader? For(Shader source, bool depthTest, Action<Shader> ready)
    {
        var key = (source, depthTest);
        if (Made.TryGetValue(key, out var made) && GodotObject.IsInstanceValid(made))
            return made;
        if (!Codes.TryGetValue(key, out var code))
        {
            code = Build(source, depthTest);
            Codes[key] = code;
        }
        if (code == null)
            return null;
        var shader = CustomShaderCache.Get(code, built =>
        {
            Made[key] = built;
            ready(built);
        });
        if (shader != null)
            Made[key] = shader;
        return shader;
    }

    private static string? Build(Shader source, bool depthTest)
    {
        string code;
        try
        {
            code = source.Code;
        }
        catch
        {
            return null;
        }
        if (string.IsNullOrEmpty(code) || !ShaderType.IsMatch(code))
            return null;
        var original = code;

        bool transparent = WritesAlpha.IsMatch(code) || code.Contains("#define TRANSPARENT_PASS");
        if (transparent && depthTest)
            return null;

        if (!transparent)
        {
            var include = Include.Match(code);
            if (include.Success)
                code = code.Insert(include.Index, "#define TRANSPARENT_PASS\n");
            else if (!AppendAlpha(ref code))
                return null;
        }

        var flags = new List<string>();
        if (!transparent)
            flags.Add("depth_prepass_alpha");
        if (!depthTest)
            flags.Add("depth_test_disabled");
        var mode = RenderMode.Match(code);
        if (mode.Success)
        {
            var existing = mode.Groups[1].Value;
            foreach (var flag in flags.ToArray())
            {
                if (existing.Contains(flag, System.StringComparison.Ordinal))
                    flags.Remove(flag);
            }
            if (flags.Count > 0)
                code = code.Remove(mode.Index, mode.Length).Insert(mode.Index, $"render_mode {existing}, {string.Join(", ", flags)};");
        }
        else if (flags.Count > 0)
        {
            var type = ShaderType.Match(code);
            code = code.Insert(type.Index + type.Length, $"\nrender_mode {string.Join(", ", flags)};");
        }

        // A shader that already blends and already ignores depth (the laser family) draws over the dash as it
        // is; a copy of it would only be a second compile of the same text.
        return code == original ? null : code;
    }

    // ALPHA = 1.0 as the last line of the fragment function, found by matching its braces.
    private static bool AppendAlpha(ref string code)
    {
        int start = code.IndexOf("void fragment()", System.StringComparison.Ordinal);
        if (start < 0)
            return false;
        int open = code.IndexOf('{', start);
        if (open < 0)
            return false;
        int depth = 0;
        for (int i = open; i < code.Length; i++)
        {
            char c = code[i];
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                int end = code.IndexOf('\n', i);
                if (end < 0)
                    return false;
                i = end;
                continue;
            }
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                int end = code.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                if (end < 0)
                    return false;
                i = end + 1;
                continue;
            }
            if (c == '{')
                depth++;
            else if (c == '}' && --depth == 0)
            {
                code = code.Insert(i, "\tALPHA = 1.0;\n");
                return true;
            }
        }
        return false;
    }
}
