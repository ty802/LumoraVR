// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Text;

namespace Lumora.Core.Assets;

// Tokenizes the text and wraps each token in a rich-text <color=#rrggbb> span so a rich-text Text can
// render it colored. Re-runs on every edit, so it stays one linear pass with a single StringBuilder and no
// regex.
// House-dark palette. Every literal '<' in the source is neutralized so user code (a comment holding a
// literal "<color=...>", or a '<' comparison operator) can never inject a real tag: the '<' is emitted
// immediately before a </color>, which the rich-text parser reads as a literal '<' and a span close, then
// the span reopens. That adds only tags (zero visible characters), so the stripped display text stays
// byte-for-byte equal to the raw source and lines up under the editor's caret. -xlinka
public static class GdShaderHighlighter
{
    private const string KeywordHex = "c792ea";
    private const string TypeHex = "82aaff";
    private const string BuiltinHex = "ffcb6b";
    private const string NumberHex = "f78c6c";
    private const string StringHex = "c3e88d";
    private const string CommentHex = "5f6b8c";
    private const string HintHex = "7fdbca";
    private const string OperatorHex = "8a91a7";

    public static string Highlight(string? source)
    {
        if (string.IsNullOrEmpty(source))
            return string.Empty;

        int n = source.Length;
        var sb = new StringBuilder(n + n / 4 + 64);
        int i = 0;
        while (i < n)
        {
            char c = source[i];

            // Whitespace copies through untouched (no '<', so no escaping needed).
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
            {
                sb.Append(c);
                i++;
                continue;
            }

            // Line comment: // to end of line.
            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                int s = i;
                i += 2;
                while (i < n && source[i] != '\n')
                    i++;
                AppendToken(sb, source, s, i - s, CommentHex);
                continue;
            }

            // Block comment: /* to */ (or end of source).
            if (c == '/' && i + 1 < n && source[i + 1] == '*')
            {
                int s = i;
                i += 2;
                while (i < n && !(source[i] == '*' && i + 1 < n && source[i + 1] == '/'))
                    i++;
                if (i < n)
                    i += 2; // consume the closing */
                AppendToken(sb, source, s, i - s, CommentHex);
                continue;
            }

            // String literal (gdshader has none, but handle it so a stray quote can't eat the rest).
            if (c == '"')
            {
                int s = i;
                i++;
                while (i < n && source[i] != '"')
                {
                    if (source[i] == '\\' && i + 1 < n)
                        i++;
                    i++;
                }
                if (i < n)
                    i++; // closing quote
                AppendToken(sb, source, s, i - s, StringHex);
                continue;
            }

            // Number (int/float/hex, with exponent and f/u/l suffixes).
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(source[i + 1])))
            {
                int s = i;
                i = ConsumeNumber(source, i, n);
                AppendToken(sb, source, s, i - s, NumberHex);
                continue;
            }

            // Identifier -> keyword / type / builtin / hint, else plain (base text color).
            if (IsIdentStart(c))
            {
                int s = i;
                i++;
                while (i < n && IsIdentPart(source[i]))
                    i++;
                int len = i - s;
                string? hex = Classify(source.Substring(s, len));
                if (hex == null)
                    sb.Append(source, s, len); // identifiers carry no '<', append raw
                else
                    AppendToken(sb, source, s, len, hex);
                continue;
            }

            // Operator / punctuation run, dim. Stops before whitespace, identifiers, numbers, a comment
            // opener, a '.N' number, or a string so those get their own token.
            {
                int s = i;
                while (i < n)
                {
                    char d = source[i];
                    if (d == ' ' || d == '\t' || d == '\r' || d == '\n')
                        break;
                    if (IsIdentStart(d) || char.IsDigit(d) || d == '"')
                        break;
                    if (d == '/' && i + 1 < n && (source[i + 1] == '/' || source[i + 1] == '*'))
                        break;
                    if (d == '.' && i + 1 < n && char.IsDigit(source[i + 1]))
                        break;
                    i++;
                }
                if (i == s)
                    i++; // never stall on an unexpected char
                AppendToken(sb, source, s, i - s, OperatorHex);
            }
        }

        return sb.ToString();
    }

    private static int ConsumeNumber(string s, int i, int n)
    {
        if (s[i] == '0' && i + 1 < n && (s[i + 1] == 'x' || s[i + 1] == 'X'))
        {
            i += 2;
            while (i < n && Uri.IsHexDigit(s[i]))
                i++;
        }
        else
        {
            while (i < n && char.IsDigit(s[i]))
                i++;
            if (i < n && s[i] == '.')
            {
                i++;
                while (i < n && char.IsDigit(s[i]))
                    i++;
            }
            if (i < n && (s[i] == 'e' || s[i] == 'E'))
            {
                int j = i + 1;
                if (j < n && (s[j] == '+' || s[j] == '-'))
                    j++;
                if (j < n && char.IsDigit(s[j]))
                {
                    i = j;
                    while (i < n && char.IsDigit(s[i]))
                        i++;
                }
            }
        }
        while (i < n && (s[i] == 'f' || s[i] == 'F' || s[i] == 'u' || s[i] == 'U' || s[i] == 'l' || s[i] == 'L'))
            i++;
        return i;
    }

    private static string? Classify(string word)
    {
        if (Hints.Contains(word))
            return HintHex;
        if (Keywords.Contains(word))
            return KeywordHex;
        if (Types.Contains(word))
            return TypeHex;
        if (Builtins.Contains(word))
            return BuiltinHex;
        return null;
    }

    private static bool IsIdentStart(char c) => c == '_' || char.IsLetter(c);
    private static bool IsIdentPart(char c) => c == '_' || char.IsLetterOrDigit(c);

    private static void AppendToken(StringBuilder sb, string src, int start, int len, string hex)
    {
        if (len <= 0)
            return;
        sb.Append("<color=#").Append(hex).Append('>');
        AppendEscaped(sb, src, start, len, hex);
        sb.Append("</color>");
    }

    // Emit the token body, defusing every literal '<'. A '<' directly before a </color> is read by the
    // rich-text parser as a literal '<' plus a close, so it can never begin a tag; the span is reopened so
    // the rest of the token keeps its color. No visible characters are added. -xlinka
    private static void AppendEscaped(StringBuilder sb, string src, int start, int len, string hex)
    {
        int end = start + len;
        for (int i = start; i < end; i++)
        {
            char c = src[i];
            sb.Append(c);
            if (c == '<')
                sb.Append("</color><color=#").Append(hex).Append('>');
        }
    }

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "shader_type", "render_mode", "uniform", "varying", "const", "in", "out", "inout",
        "if", "else", "for", "while", "do", "return", "break", "continue", "discard",
        "switch", "case", "default", "struct", "group_uniforms", "global", "instance",
        "precision", "lowp", "mediump", "highp", "true", "false",
    };

    private static readonly HashSet<string> Types = new(StringComparer.Ordinal)
    {
        "void", "bool", "int", "uint", "float", "double",
        "vec2", "vec3", "vec4", "bvec2", "bvec3", "bvec4",
        "ivec2", "ivec3", "ivec4", "uvec2", "uvec3", "uvec4", "dvec2", "dvec3", "dvec4",
        "mat2", "mat3", "mat4",
        "mat2x2", "mat2x3", "mat2x4", "mat3x2", "mat3x3", "mat3x4", "mat4x2", "mat4x3", "mat4x4",
        "sampler2D", "isampler2D", "usampler2D",
        "sampler2DArray", "isampler2DArray", "usampler2DArray",
        "sampler3D", "isampler3D", "usampler3D",
        "samplerCube", "samplerCubeArray", "samplerExternalOES",
    };

    private static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        // Vertex / fragment / light I/O.
        "VERTEX", "NORMAL", "TANGENT", "BINORMAL", "POSITION", "UV", "UV2", "COLOR",
        "POINT_SIZE", "INSTANCE_ID", "INSTANCE_CUSTOM", "VERTEX_ID",
        "MODELVIEW_MATRIX", "MODELVIEW_NORMAL_MATRIX", "MODEL_MATRIX", "MODEL_NORMAL_MATRIX",
        "VIEW_MATRIX", "INV_VIEW_MATRIX", "PROJECTION_MATRIX", "INV_PROJECTION_MATRIX",
        "VIEWPORT_SIZE", "TIME", "PI", "TAU", "E", "FRAGCOORD", "FRONT_FACING", "VIEW",
        "ALBEDO", "ALPHA", "ALPHA_SCISSOR_THRESHOLD", "METALLIC", "SPECULAR", "ROUGHNESS",
        "RIM", "RIM_TINT", "CLEARCOAT", "CLEARCOAT_ROUGHNESS", "ANISOTROPY", "ANISOTROPY_FLOW",
        "SSS_STRENGTH", "BACKLIGHT", "AO", "AO_LIGHT_AFFECT", "EMISSION",
        "NORMAL_MAP", "NORMAL_MAP_DEPTH", "DEPTH", "SCREEN_UV", "POINT_COORD",
        "LIGHT", "LIGHT_COLOR", "ATTENUATION", "DIFFUSE_LIGHT", "SPECULAR_LIGHT",
        "LIGHT_IS_DIRECTIONAL", "CAMERA_POSITION_WORLD", "CAMERA_DIRECTION_WORLD",
        "NODE_POSITION_WORLD", "OUTPUT_IS_SRGB",
        // canvas_item.
        "MODULATE", "TEXTURE", "TEXTURE_PIXEL_SIZE", "SCREEN_PIXEL_SIZE", "NORMAL_TEXTURE",
        "SPECULAR_SHININESS", "SPECULAR_SHININESS_TEXTURE", "CANVAS_MATRIX", "SCREEN_MATRIX",
        "AT_LIGHT_PASS",
        // Functions.
        "radians", "degrees", "sin", "cos", "tan", "asin", "acos", "atan",
        "sinh", "cosh", "tanh", "asinh", "acosh", "atanh",
        "pow", "exp", "log", "exp2", "log2", "sqrt", "inversesqrt",
        "abs", "sign", "floor", "trunc", "round", "roundEven", "ceil", "fract",
        "mod", "modf", "min", "max", "clamp", "mix", "step", "smoothstep",
        "isnan", "isinf", "floatBitsToInt", "floatBitsToUint", "intBitsToFloat", "uintBitsToFloat",
        "length", "distance", "dot", "cross", "normalize", "faceforward", "reflect", "refract",
        "matrixCompMult", "outerProduct", "transpose", "determinant", "inverse",
        "lessThan", "lessThanEqual", "greaterThan", "greaterThanEqual", "equal", "notEqual",
        "any", "all", "not",
        "texture", "textureProj", "textureLod", "textureProjLod", "textureGrad",
        "texelFetch", "textureSize", "textureQueryLod", "textureGather",
        "dFdx", "dFdy", "fwidth",
    };

    private static readonly HashSet<string> Hints = new(StringComparer.Ordinal)
    {
        "hint_range", "hint_color", "source_color", "hint_normal",
        "hint_default_white", "hint_default_black", "hint_default_transparent",
        "hint_anisotropy", "hint_albedo", "hint_black_albedo",
        "hint_roughness", "hint_roughness_r", "hint_roughness_g", "hint_roughness_b",
        "hint_roughness_a", "hint_roughness_normal", "hint_roughness_gray",
        "filter_nearest", "filter_linear", "filter_nearest_mipmap", "filter_linear_mipmap",
        "filter_nearest_mipmap_anisotropic", "filter_linear_mipmap_anisotropic",
        "repeat_enable", "repeat_disable",
        "hint_screen_texture", "hint_depth_texture", "hint_normal_roughness_texture",
        "instance_index",
    };
}
