// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Lumora.Core.Math;
using Lumora.Script;

namespace Lumora.Shading;

// Constants as shader code. Only numbers and true/false ever reach the source from a graph, spelled here
// with the invariant culture, so the text a graph compiles to is the same on every machine. A picked
// color is sRGB and shader math is linear, so a color constant is decoded on the way in, the way a
// source_color uniform is. -xlinka
public static class GlslLiteral
{
    public static string Float(double value)
    {
        float f = (float)value;
        if (!float.IsFinite(f))
            f = value > 0 ? float.MaxValue : value < 0 ? float.MinValue : 0f;
        if (f == 0f)
            return "0.0";

        var text = f.ToString("R", CultureInfo.InvariantCulture);
        int e = text.IndexOfAny(new[] { 'E', 'e' });
        if (e >= 0)
        {
            var mantissa = text[..e];
            if (!mantissa.Contains('.'))
                mantissa += ".0";
            text = mantissa + "e" + text[(e + 1)..];
        }
        else if (!text.Contains('.'))
        {
            text += ".0";
        }
        return f < 0 ? $"({text})" : text;
    }

    public static string Int(long value)
    {
        value = Math.Clamp(value, int.MinValue + 1L, int.MaxValue);
        var text = value.ToString(CultureInfo.InvariantCulture);
        return value < 0 ? $"({text})" : text;
    }

    public static string Bool(bool value) => value ? "true" : "false";

    public static GlslValue Zero(ShaderType type) => type switch
    {
        ShaderType.Bool => new GlslValue("false", type),
        ShaderType.Int => new GlslValue("0", type),
        ShaderType.Float => new GlslValue("0.0", type),
        ShaderType.Float2 => new GlslValue("vec2(0.0)", type),
        ShaderType.Float3 => new GlslValue("vec3(0.0)", type),
        ShaderType.Float4 => new GlslValue("vec4(0.0)", type),
        ShaderType.Color => new GlslValue("vec4(0.0, 0.0, 0.0, 1.0)", type),
        _ => throw new ArgumentException("a texture has no literal"),
    };

    // A constant from a graph for a port of the given type, or null when the value does not fit it.
    public static GlslValue? FromValue(GraphValue value, ShaderType type)
    {
        switch (value.Kind)
        {
            case GraphValueKind.Bool:
                return FromNumbers(new[] { value.Bool ? 1.0 : 0.0 }, type, fromBool: true, value.Bool);
            case GraphValueKind.Number:
                return FromNumbers(new[] { value.Number }, type, fromBool: false, false);
            case GraphValueKind.Numbers:
                return value.Numbers.Length == 0 ? null : FromNumbers(value.Numbers, type, fromBool: false, false);
            default:
                return null;
        }
    }

    // A constant a node's C# signature gives as a default.
    public static GlslValue? FromDefault(object? value, ShaderType type) => value switch
    {
        bool b => FromNumbers(new[] { b ? 1.0 : 0.0 }, type, fromBool: true, b),
        int i => FromNumbers(new[] { (double)i }, type, false, false),
        float f => FromNumbers(new[] { (double)f }, type, false, false),
        double d => FromNumbers(new[] { d }, type, false, false),
        float2 v => FromNumbers(new double[] { v.x, v.y }, type, false, false),
        float3 v => FromNumbers(new double[] { v.x, v.y, v.z }, type, false, false),
        float4 v => FromNumbers(new double[] { v.x, v.y, v.z, v.w }, type, false, false),
        color c => FromNumbers(new double[] { c.r, c.g, c.b, c.a }, type, false, false),
        colorHDR c => FromNumbers(new double[] { c.r, c.g, c.b, c.a }, type, false, false),
        _ => null,
    };

    private static GlslValue? FromNumbers(double[] numbers, ShaderType type, bool fromBool, bool boolValue)
    {
        foreach (var n in numbers)
        {
            if (!double.IsFinite(n))
                return null;
        }

        switch (type)
        {
            case ShaderType.Bool:
                return new GlslValue(Bool(fromBool ? boolValue : numbers[0] != 0), type);
            case ShaderType.Int:
                return new GlslValue(Int((long)Math.Round(Math.Clamp(numbers[0], int.MinValue + 1.0, int.MaxValue))), type);
            case ShaderType.Float:
                return new GlslValue(Float(numbers[0]), type);
            case ShaderType.Float2:
            case ShaderType.Float3:
            case ShaderType.Float4:
                return new GlslValue(Vector(type, numbers, 0.0), type);
            case ShaderType.Color:
                return new GlslValue(Color(numbers), type);
            default:
                return null;
        }
    }

    // Numbers exactly as given, for a uniform's default: a source_color uniform decodes its own.
    public static string Numbers(ShaderType type, double[] numbers)
    {
        foreach (var n in numbers)
        {
            if (!double.IsFinite(n))
                throw new ArgumentException("not a finite number");
        }
        return type switch
        {
            ShaderType.Float => Float(numbers[0]),
            ShaderType.Color => Vector(ShaderType.Float4, numbers, 1.0),
            _ => Vector(type, numbers, 0.0),
        };
    }

    // One number fills every part; fewer parts than the type has leave the rest at the pad value.
    private static string Vector(ShaderType type, double[] numbers, double pad)
    {
        int width = ShaderTypes.Width(type);
        var parts = new string[width];
        for (int i = 0; i < width; i++)
        {
            double n = numbers.Length == 1 ? numbers[0] : i < numbers.Length ? numbers[i] : pad;
            parts[i] = Arg(n);
        }
        return $"{ShaderTypes.GlslName(type)}({string.Join(", ", parts)})";
    }

    private static string Color(double[] numbers)
    {
        double r = numbers[0];
        double g = numbers.Length == 1 ? r : numbers.Length > 1 ? numbers[1] : 0;
        double b = numbers.Length == 1 ? r : numbers.Length > 2 ? numbers[2] : 0;
        double a = numbers.Length > 3 ? numbers[3] : 1;
        return $"vec4({Arg(Linear(r))}, {Arg(Linear(g))}, {Arg(Linear(b))}, {Arg(a)})";
    }

    private static double Linear(double c) => ColorSpace.GammaToLinear((float)c);

    // Inside a constructor's brackets a negative number needs no brackets of its own.
    private static string Arg(double value)
    {
        var text = Float(value);
        return text.StartsWith('(') ? text[1..^1] : text;
    }
}
