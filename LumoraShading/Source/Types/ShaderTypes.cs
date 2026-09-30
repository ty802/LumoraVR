// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Shading;

// The script types a shader port can carry and what a wire does between two of them. A wire converts on
// its own the way people expect from other node editors: a number spreads over a vector, a vector hands
// a number its first part, a color hands a number its brightness, and a missing alpha is 1. Only
// textures never convert. -xlinka
public static class ShaderTypes
{
    public static bool TryFrom(ScriptType type, out ShaderType shaderType)
    {
        switch (type.Name)
        {
            case "bool": shaderType = ShaderType.Bool; return true;
            case "int": shaderType = ShaderType.Int; return true;
            case "float": shaderType = ShaderType.Float; return true;
            case "float2": shaderType = ShaderType.Float2; return true;
            case "float3": shaderType = ShaderType.Float3; return true;
            case "float4": shaderType = ShaderType.Float4; return true;
            case "color":
            case "colorHDR": shaderType = ShaderType.Color; return true;
            case "texture": shaderType = ShaderType.Texture; return true;
            default: shaderType = default; return false;
        }
    }

    public static string GlslName(ShaderType type) => type switch
    {
        ShaderType.Bool => "bool",
        ShaderType.Int => "int",
        ShaderType.Float => "float",
        ShaderType.Float2 => "vec2",
        ShaderType.Float3 => "vec3",
        ShaderType.Float4 or ShaderType.Color => "vec4",
        _ => "sampler2D",
    };

    public static int Width(ShaderType type) => type switch
    {
        ShaderType.Float2 => 2,
        ShaderType.Float3 => 3,
        ShaderType.Float4 or ShaderType.Color => 4,
        ShaderType.Texture => 0,
        _ => 1,
    };

    public static bool IsVector(ShaderType type) => type is ShaderType.Float2 or ShaderType.Float3 or ShaderType.Float4 or ShaderType.Color;

    public static bool CanConvert(ShaderType from, ShaderType to)
        => from == to || (from != ShaderType.Texture && to != ShaderType.Texture);

    // Whether the editor may lay a wire from a port of one script type onto a port of another.
    public static bool Accepts(ScriptType wanted, ScriptType given)
        => TryFrom(wanted, out var to) && TryFrom(given, out var from) && CanConvert(from, to);

    public static GlslValue Convert(GlslValue value, ShaderType to)
    {
        var from = value.Type;
        var code = value.Code;
        if (from == to)
            return value;
        if (from == ShaderType.Color && to == ShaderType.Float4 || from == ShaderType.Float4 && to == ShaderType.Color)
            return new GlslValue(code, to);

        switch (to)
        {
            case ShaderType.Float:
                return new GlslValue(ToFloat(code, from), to);
            case ShaderType.Int:
                return new GlslValue($"int({ToFloat(code, from)})", to);
            case ShaderType.Bool:
                return new GlslValue(from == ShaderType.Int ? $"({code} != 0)" : $"({ToFloat(code, from)} > 0.0)", to);
            case ShaderType.Float2:
                return new GlslValue(Width(from) >= 2 ? $"{code}.xy" : $"vec2({ToFloat(code, from)})", to);
            case ShaderType.Float3:
                return new GlslValue(from switch
                {
                    ShaderType.Float4 or ShaderType.Color => $"{code}.xyz",
                    ShaderType.Float2 => $"vec3({code}, 0.0)",
                    _ => $"vec3({ToFloat(code, from)})",
                }, to);
            case ShaderType.Float4:
                return new GlslValue(from switch
                {
                    ShaderType.Float3 => $"vec4({code}, 0.0)",
                    ShaderType.Float2 => $"vec4({code}, 0.0, 0.0)",
                    _ => $"vec4({ToFloat(code, from)})",
                }, to);
            case ShaderType.Color:
                return new GlslValue(from switch
                {
                    ShaderType.Float3 => $"vec4({code}, 1.0)",
                    ShaderType.Float2 => $"vec4({code}, 0.0, 1.0)",
                    _ => $"vec4(vec3({ToFloat(code, from)}), 1.0)",
                }, to);
            default:
                throw new System.InvalidOperationException($"{from} does not become {to}");
        }
    }

    private static string ToFloat(string code, ShaderType from) => from switch
    {
        ShaderType.Float => code,
        ShaderType.Bool or ShaderType.Int => $"float({code})",
        ShaderType.Color => $"dot({code}.rgb, vec3(0.2126, 0.7152, 0.0722))",
        _ => $"{code}.x",
    };
}
