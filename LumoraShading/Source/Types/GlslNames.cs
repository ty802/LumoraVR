// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Lumora.Shading;

// Names people type (a property's name) turned into names shader code can use. Lower case letters,
// digits and single underscores, starting with a letter, never a word the language or its built-in
// functions own or the helper library uses, never one of the compiler's own prefixes. What cannot be
// made usable falls back to the given fallback. -xlinka
public static class GlslNames
{
    public const int MaxLength = 32;

    // Keywords, types and built-in function names of the Godot shading language, plus words the sandbox
    // refuses to see at all (while, do).
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "attribute", "break", "case", "const", "continue", "default", "discard", "do", "else", "false", "flat",
        "for", "global", "highp", "if", "in", "inout", "instance", "lowp", "mediump", "out", "precision",
        "render_mode", "return", "shader_type", "smooth", "struct", "switch", "true", "uniform", "varying",
        "void", "while", "group_uniforms", "stencil_mode",
        "bool", "bvec2", "bvec3", "bvec4", "int", "ivec2", "ivec3", "ivec4", "uint", "uvec2", "uvec3", "uvec4",
        "float", "vec2", "vec3", "vec4", "mat2", "mat3", "mat4", "sampler2D", "isampler2D", "usampler2D",
        "sampler2DArray", "sampler3D", "samplerCube", "samplerCubeArray", "samplerExternalOES",
        "radians", "degrees", "sin", "cos", "tan", "asin", "acos", "atan", "sinh", "cosh", "tanh", "asinh",
        "acosh", "atanh", "pow", "exp", "log", "exp2", "log2", "sqrt", "inversesqrt", "abs", "sign", "floor",
        "trunc", "round", "roundeven", "ceil", "fract", "mod", "modf", "min", "max", "clamp", "mix", "step",
        "smoothstep", "isnan", "isinf", "floatbitstoint", "floatbitstouint", "intbitstofloat",
        "uintbitstofloat", "length", "distance", "dot", "cross", "normalize", "reflect", "refract",
        "faceforward", "matrixcompmult", "outerproduct", "transpose", "determinant", "inverse",
        "lessthan", "greaterthan", "lessthanequal", "greaterthanequal", "equal", "notequal", "any", "all",
        "not", "texturesize", "texturequerylod", "texturequerylevels", "texture", "textureproj", "texturelod",
        "textureprojlod", "texelfetch", "texturegrad", "textureprojgrad", "texturegather", "dfdx", "dfdy",
        "dfdxcoarse", "dfdycoarse", "dfdxfine", "dfdyfine", "fwidth", "fwidthcoarse", "fwidthfine",
        "packhalf2x16", "unpackhalf2x16", "packunorm2x16", "unpackunorm2x16", "packsnorm2x16",
        "unpacksnorm2x16", "packunorm4x8", "unpackunorm4x8", "packsnorm4x8", "unpacksnorm4x8",
        "bitfieldextract", "bitfieldinsert", "bitfieldreverse", "bitcount", "findlsb", "findmsb",
        "imulextended", "umulextended", "uaddcarry", "usubborrow", "ldexp", "frexp", "fma",
        "vertex", "fragment", "light",
    };

    // Every word the helper library's code uses. Its parameters and locals share a scope with the uniforms,
    // and Godot refuses a uniform that one of them would redefine: a property called Power broke every
    // graph with a Fresnel in it. Read from the code itself, so a helper added later is covered too.
    private static readonly Lazy<HashSet<string>> LibraryWords = new(() =>
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in GlslLibrary.Names)
        {
            if (GlslLibrary.TryGet(name, out var function))
            {
                foreach (Match word in Regex.Matches(function.Code, "[A-Za-z_][A-Za-z0-9_]*"))
                    words.Add(word.Value);
            }
        }
        return words;
    });

    public static bool IsReserved(string name)
        => Reserved.Contains(name) || Reserved.Contains(name.ToLowerInvariant()) || LibraryWords.Value.Contains(name);

    // The compiler's own names start with these; a property never takes one.
    public static bool IsCompilerName(string name)
        => name.StartsWith("lm_", StringComparison.Ordinal) || name.StartsWith("gl_", StringComparison.Ordinal)
            || name.StartsWith("tex_", StringComparison.Ordinal) || IsLocalName(name);

    public static string FromText(string? text, string fallback)
    {
        var builder = new StringBuilder();
        bool underscore = false;
        foreach (char raw in text ?? string.Empty)
        {
            char c = char.ToLowerInvariant(raw);
            if (c is >= 'a' and <= 'z' || c is >= '0' and <= '9' && builder.Length > 0)
            {
                builder.Append(c);
                underscore = false;
            }
            else if (builder.Length > 0 && !underscore)
            {
                builder.Append('_');
                underscore = true;
            }
            if (builder.Length >= MaxLength)
                break;
        }
        var name = builder.ToString().TrimEnd('_');
        if (name.Length == 0 || IsCompilerName(name))
            return fallback;
        return IsReserved(name) ? name + "_value" : name;
    }

    // n12_result: a node's output, local to the stage function.
    private static bool IsLocalName(string name)
    {
        if (name.Length < 3 || name[0] != 'n' || !char.IsAsciiDigit(name[1]))
            return false;
        int i = 1;
        while (i < name.Length && char.IsAsciiDigit(name[i]))
            i++;
        return i < name.Length && name[i] == '_';
    }
}
