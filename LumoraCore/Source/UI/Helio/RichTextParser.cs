// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumora.Core.Math;

namespace Helio.UI;

/// <summary>
/// Parses a minimal subset of inline rich-text markup into stripped visible text plus
/// per-character color, style flags, and size multiplier, so <see cref="Text"/> can
/// render styled spans. Color/style don't change layout; size does (handled in the shaper).
/// </summary>
// Supported: <color>, <alpha>, <b>/<i>/<u>/<s>, and
// <size=Nem|N%> (absolute multiple of base, or relative to the parent). Unknown
// tags pass through literally. A balanced stack restores color+style+size on close. -xlinka
public static class RichTextParser
{
    public const byte StyleBold = 1;
    public const byte StyleItalic = 2;
    public const byte StyleUnderline = 4;
    public const byte StyleStrike = 8;

    private readonly struct Span
    {
        public readonly color Color;
        public readonly byte Style;
        public readonly float Size;
        public Span(color color, byte style, float size) { Color = color; Style = style; Size = size; }
    }

    public static void Parse(string? input, in color baseColor, StringBuilder text, List<color> colors, List<byte> styles, List<float> sizes)
    {
        text.Clear();
        colors.Clear();
        styles.Clear();
        sizes.Clear();
        if (string.IsNullOrEmpty(input))
            return;

        var stack = new List<Span>();
        color current = baseColor;
        byte currentStyle = 0;
        float currentSize = 1f;

        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (c == '<')
            {
                int close = input.IndexOf('>', i + 1);
                if (close > i && HandleTag(input.Substring(i + 1, close - i - 1), in baseColor, stack, ref current, ref currentStyle, ref currentSize))
                {
                    i = close + 1;
                    continue;
                }
            }

            text.Append(c);
            colors.Add(current);
            styles.Add(currentStyle);
            sizes.Add(currentSize);
            i++;
        }
    }

    private static bool HandleTag(string tag, in color baseColor, List<Span> stack, ref color current, ref byte currentStyle, ref float currentSize)
    {
        if (tag.Length == 0)
            return false;

        if (tag[0] == '/')
        {
            string name = tag.Substring(1).Trim().ToLowerInvariant();
            if (name is "color" or "alpha" or "b" or "i" or "u" or "s" or "size")
            {
                if (stack.Count > 0)
                {
                    var prev = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    current = prev.Color;
                    currentStyle = prev.Style;
                    currentSize = prev.Size;
                }
                else
                {
                    current = baseColor;
                    currentStyle = 0;
                    currentSize = 1f;
                }
                return true;
            }
            return false;
        }

        int eq = tag.IndexOf('=');
        string key = (eq >= 0 ? tag.Substring(0, eq) : tag).Trim().ToLowerInvariant();
        string value = eq >= 0 ? tag.Substring(eq + 1).Trim() : string.Empty;

        switch (key)
        {
            case "color":
                stack.Add(new Span(current, currentStyle, currentSize));
                if (TryParseColor(value, out var col))
                    current = col;
                return true;
            case "alpha":
                stack.Add(new Span(current, currentStyle, currentSize));
                current = new color(current.r, current.g, current.b, ParseAlpha(value, current.a));
                return true;
            case "size":
                stack.Add(new Span(current, currentStyle, currentSize));
                if (TryParseSize(value, out float factor, out bool relative))
                    currentSize = relative ? currentSize * factor : factor;
                return true;
            case "b":
                stack.Add(new Span(current, currentStyle, currentSize));
                currentStyle |= StyleBold;
                return true;
            case "i":
                stack.Add(new Span(current, currentStyle, currentSize));
                currentStyle |= StyleItalic;
                return true;
            case "u":
                stack.Add(new Span(current, currentStyle, currentSize));
                currentStyle |= StyleUnderline;
                return true;
            case "s":
                stack.Add(new Span(current, currentStyle, currentSize));
                currentStyle |= StyleStrike;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParseSize(string value, out float factor, out bool relative)
    {
        factor = 1f;
        relative = true;
        if (string.IsNullOrEmpty(value))
            return false;

        value = value.Trim();
        if (value.EndsWith("%"))
        {
            if (float.TryParse(value.Substring(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float p))
            {
                factor = p / 100f;
                relative = true;
                return true;
            }
            return false;
        }

        string v = value.EndsWith("em") ? value.Substring(0, value.Length - 2) : value;
        if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
        {
            factor = a;
            relative = false; // absolute multiple of the base size
            return true;
        }
        return false;
    }

    private static bool TryParseColor(string value, out color result)
    {
        result = color.White;
        if (string.IsNullOrEmpty(value))
            return false;

        value = value.Trim().Trim('"', '\'');
        if (value.StartsWith("#"))
        {
            try
            {
                result = color.FromHex(value);
                return true;
            }
            catch
            {
                return false;
            }
        }

        switch (value.ToLowerInvariant())
        {
            case "white": result = color.White; return true;
            case "black": result = color.Black; return true;
            case "red": result = color.Red; return true;
            case "green": result = color.Green; return true;
            case "blue": result = color.Blue; return true;
            case "yellow": result = color.Yellow; return true;
            case "cyan": result = color.Cyan; return true;
            case "magenta": result = color.Magenta; return true;
            case "gray":
            case "grey": result = new color(0.5f, 0.5f, 0.5f, 1f); return true;
            case "orange": result = new color(1f, 0.5f, 0f, 1f); return true;
            default: return false;
        }
    }

    private static float ParseAlpha(string value, float fallback)
    {
        if (string.IsNullOrEmpty(value))
            return fallback;

        value = value.Trim();
        if (value.StartsWith("#"))
        {
            if (int.TryParse(value.Substring(1), NumberStyles.HexNumber, null, out int hv))
                return (hv & 0xFF) / 255f;
            return fallback;
        }

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            return f <= 1f ? f : f / 255f;
        return fallback;
    }
}
