// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Helio.UI;

/// <summary>
/// Parses a minimal subset of inline rich-text markup into stripped visible text plus
/// per-character color, style flags, size multiplier, and highlight color, so <see cref="Text"/>
/// can render styled spans.
/// </summary>
// Supported: <color>, <alpha>, <b>/<i>/<u>/<s>, <size=Nem|N%>, <sub>/<sup>, <mark>/<mark=color>, <nobr>,
// <gradient=a,b>, <lowercase>/<uppercase>/<smallcaps>/<allcaps>, <br>, <noparse>...</noparse>, and <closeall>.
// Unknown tags pass through literally. A balanced stack restores every attribute on close. -xlinka
public static class RichTextParser
{
    public const byte StyleBold = 1;
    public const byte StyleItalic = 2;
    public const byte StyleUnderline = 4;
    public const byte StyleStrike = 8;
    public const byte StyleSub = 16;
    public const byte StyleSup = 32;
    public const byte StyleNoBreak = 64;

    // Case transform applied to a run: uppercase/lowercase change the visible char; smallcaps uppercases and
    // renders originally-lowercase letters smaller. -xlinka
    public const byte CaseNone = 0;
    public const byte CaseLower = 1;
    public const byte CaseUpper = 2;
    public const byte CaseSmallCaps = 3;

    // Subscript/superscript shrink and shift off the baseline; shared factor so parser and renderer agree. -xlinka
    public const float SubSupScale = 0.7f;
    // Smallcaps renders originally-lowercase letters at this fraction of the run size.
    public const float SmallCapsScale = 0.75f;

    // Default highlight when <mark> carries no color: soft translucent yellow behind the text. -xlinka
    private static readonly color DefaultMark = new(1f, 0.92f, 0.35f, 0.4f);
    private static readonly color NoMark = new(0f, 0f, 0f, 0f);

    private readonly struct Span
    {
        public readonly color Color;
        public readonly byte Style;
        public readonly float Size;
        public readonly color Mark;
        public readonly byte Case;
        public Span(color color, byte style, float size, color mark, byte @case)
        {
            Color = color; Style = style; Size = size; Mark = mark; Case = @case;
        }
    }

    // Per-line block attributes recorded as SPARSE position markers (align/line-height change per line, not per
    // char, so a marker at the change position is enough - the renderer picks the one active at each line). -xlinka
    public readonly struct AlignMark
    {
        public readonly int Index;
        public readonly byte Align; // 0 = inherit the Text default; else (TextHorizontalAlignment + 1)
        public AlignMark(int index, byte align) { Index = index; Align = align; }
    }

    public readonly struct LineHeightMark
    {
        public readonly int Index;
        public readonly float Height; // 0 = inherit; else multiplier of the base line height
        public LineHeightMark(int index, float height) { Index = index; Height = height; }
    }

    // <font=name> switches the shaping font for the run. Marker holds the font name active from Index onward
    // (null = the Text's default font); the renderer resolves the name to a FontSet and the shaper uses it. -xlinka
    public readonly struct FontMark
    {
        public readonly int Index;
        public readonly string? Name;
        public FontMark(int index, string? name) { Index = index; Name = name; }
    }

    public static void Parse(string? input, in color baseColor, StringBuilder text, List<color> colors, List<byte> styles, List<float> sizes, List<color> marks, List<string?> sprites,
        List<AlignMark> alignMarks, List<LineHeightMark> lineHeightMarks, List<FontMark> fontMarks)
    {
        text.Clear();
        colors.Clear();
        styles.Clear();
        sizes.Clear();
        marks.Clear();
        sprites.Clear();
        alignMarks.Clear();
        lineHeightMarks.Clear();
        fontMarks.Clear();
        if (string.IsNullOrEmpty(input))
            return;

        var stack = new List<Span>();
        color current = baseColor;
        byte currentStyle = 0;
        float currentSize = 1f;
        color currentMark = NoMark;
        byte currentCase = CaseNone;
        // Gradient runs outside the attribute stack: it back-fills the color of the whole run on close, so it
        // just needs the run's start index and endpoints, not a push/pop. -xlinka
        int gradStart = -1;
        color gradA = color.White, gradB = color.White;
        // align/line-height/font nest with their own small stacks and emit a marker on open and close. -xlinka
        byte currentAlign = 0;
        float currentLineHeight = 0f;
        string? currentFontName = null;
        var alignStack = new List<byte>();
        var lhStack = new List<float>();
        var fontStack = new List<string?>();

        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (c == '<')
            {
                int close = input.IndexOf('>', i + 1);
                if (close > i)
                {
                    string body = input.Substring(i + 1, close - i - 1);
                    string trimmed = body.Trim();

                    // <br> emits a newline and <noparse> copies its content literally: both ADD characters, so
                    // they live here rather than in the attribute-only HandleTag. -xlinka
                    if (trimmed.Equals("br", StringComparison.OrdinalIgnoreCase))
                    {
                        Append('\n', text, colors, styles, sizes, marks, sprites, current, currentStyle, currentSize, currentMark, CaseNone);
                        i = close + 1;
                        continue;
                    }
                    if (trimmed.Equals("noparse", StringComparison.OrdinalIgnoreCase))
                    {
                        int contentStart = close + 1;
                        int closeTag = input.IndexOf("</noparse>", contentStart, StringComparison.OrdinalIgnoreCase);
                        int contentEnd = closeTag >= 0 ? closeTag : input.Length;
                        for (int k = contentStart; k < contentEnd; k++)
                            Append(input[k], text, colors, styles, sizes, marks, sprites, current, currentStyle, currentSize, currentMark, currentCase);
                        i = closeTag >= 0 ? closeTag + "</noparse>".Length : input.Length;
                        continue;
                    }
                    // <sprite=name> / <sprite name=name> emits one placeholder char that the renderer draws as a
                    // sprite; it ADDS a character, so it lives here rather than in HandleTag. -xlinka
                    string? spriteName = ParseSpriteName(trimmed);
                    if (spriteName != null)
                    {
                        Append((char)TextShaper.SpriteGlyph, text, colors, styles, sizes, marks, sprites,
                            current, currentStyle, currentSize, currentMark, CaseNone, spriteName);
                        i = close + 1;
                        continue;
                    }

                    if (TryHandleBlockTag(trimmed, text.Length, alignStack, ref currentAlign, alignMarks, lhStack, ref currentLineHeight, lineHeightMarks) ||
                        TryHandleFont(trimmed, text.Length, fontStack, ref currentFontName, fontMarks))
                    {
                        i = close + 1;
                        continue;
                    }

                    if (TryHandleGradient(body, colors, ref gradStart, ref gradA, ref gradB) ||
                        HandleTag(body, in baseColor, stack, ref current, ref currentStyle, ref currentSize, ref currentMark, ref currentCase))
                    {
                        i = close + 1;
                        continue;
                    }
                }
            }

            Append(c, text, colors, styles, sizes, marks, sprites, current, currentStyle, currentSize, currentMark, currentCase);
            i++;
        }
    }

    // <sprite=name>, <sprite name=name>, or the <glyph...> aliases -> the sprite name; null for any other tag.
    private static string? ParseSpriteName(string trimmed)
    {
        int eq = trimmed.IndexOf('=');
        if (eq < 0)
            return null;
        string key = trimmed.Substring(0, eq).Trim().ToLowerInvariant();
        if (key is not ("sprite" or "glyph" or "sprite name" or "glyph name"))
            return null;
        string val = trimmed.Substring(eq + 1).Trim().Trim('"', '\'');
        return val.Length > 0 ? val : null;
    }

    // Append one visible char with the current attributes, applying the case transform (and the smallcaps
    // size shrink for originally-lowercase letters). -xlinka
    private static void Append(char c, StringBuilder text, List<color> colors, List<byte> styles, List<float> sizes, List<color> marks,
        List<string?> sprites, color color, byte style, float size, color mark, byte caseMode, string? spriteName = null)
    {
        char visible = caseMode switch
        {
            CaseLower => char.ToLowerInvariant(c),
            CaseUpper or CaseSmallCaps => char.ToUpperInvariant(c),
            _ => c,
        };
        float charSize = size;
        if (caseMode == CaseSmallCaps && char.IsLower(c))
            charSize *= SmallCapsScale;

        text.Append(visible);
        colors.Add(color);
        styles.Add(style);
        sizes.Add(charSize);
        marks.Add(mark);
        sprites.Add(spriteName);
    }

    // <gradient=a,b> ... </gradient> : lerp each char's color from a (run start) to b (run end). Interpolated by
    // char index, which is spatially exact for a monospace font and close enough otherwise. -xlinka
    private static bool TryHandleGradient(string body, List<color> colors, ref int gradStart, ref color gradA, ref color gradB)
    {
        string trimmed = body.Trim();
        if (trimmed.Equals("/gradient", StringComparison.OrdinalIgnoreCase))
        {
            if (gradStart >= 0)
            {
                int end = colors.Count - 1;
                int span = end - gradStart;
                for (int k = gradStart; k <= end; k++)
                {
                    float t = span > 0 ? (float)(k - gradStart) / span : 0f;
                    colors[k] = LerpColor(gradA, gradB, t);
                }
                gradStart = -1;
            }
            return true;
        }

        int eq = trimmed.IndexOf('=');
        if (eq < 0 || !trimmed.Substring(0, eq).Trim().Equals("gradient", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = trimmed.Substring(eq + 1).Split(',');
        if (parts.Length < 2 || !TryParseColor(parts[0], out gradA) || !TryParseColor(parts[1], out gradB))
            return false;

        gradStart = colors.Count;
        return true;
    }

    private static color LerpColor(in color a, in color b, float t)
        => new(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);

    // <align=left|center|right|justify> and <line-height=N> - per-LINE attributes, so we record a marker at the
    // change position (nested via their own small stacks) and the renderer resolves the value active per line. -xlinka
    private static bool TryHandleBlockTag(string trimmed, int pos, List<byte> alignStack, ref byte currentAlign, List<AlignMark> alignMarks,
        List<float> lhStack, ref float currentLineHeight, List<LineHeightMark> lineHeightMarks)
    {
        if (trimmed.Equals("/align", StringComparison.OrdinalIgnoreCase))
        {
            currentAlign = alignStack.Count > 0 ? Pop(alignStack) : (byte)0;
            alignMarks.Add(new AlignMark(pos, currentAlign));
            return true;
        }
        if (trimmed.Equals("/line-height", StringComparison.OrdinalIgnoreCase))
        {
            currentLineHeight = lhStack.Count > 0 ? Pop(lhStack) : 0f;
            lineHeightMarks.Add(new LineHeightMark(pos, currentLineHeight));
            return true;
        }

        int eq = trimmed.IndexOf('=');
        if (eq < 0)
            return false;
        string key = trimmed.Substring(0, eq).Trim().ToLowerInvariant();
        string val = trimmed.Substring(eq + 1).Trim();

        if (key == "align")
        {
            alignStack.Add(currentAlign);
            currentAlign = ParseAlign(val);
            alignMarks.Add(new AlignMark(pos, currentAlign));
            return true;
        }
        if (key == "line-height")
        {
            lhStack.Add(currentLineHeight);
            currentLineHeight = ParseLineHeight(val);
            lineHeightMarks.Add(new LineHeightMark(pos, currentLineHeight));
            return true;
        }
        return false;
    }

    // <font=name> / </font> : like the block tags but carries a name; the renderer resolves it to a FontSet. -xlinka
    private static bool TryHandleFont(string trimmed, int pos, List<string?> fontStack, ref string? currentFontName, List<FontMark> fontMarks)
    {
        if (trimmed.Equals("/font", StringComparison.OrdinalIgnoreCase))
        {
            currentFontName = fontStack.Count > 0 ? Pop(fontStack) : null;
            fontMarks.Add(new FontMark(pos, currentFontName));
            return true;
        }
        int eq = trimmed.IndexOf('=');
        if (eq < 0 || trimmed.Substring(0, eq).Trim().ToLowerInvariant() != "font")
            return false;
        fontStack.Add(currentFontName);
        string name = trimmed.Substring(eq + 1).Trim().Trim('"', '\'');
        currentFontName = name.Length > 0 ? name : null;
        fontMarks.Add(new FontMark(pos, currentFontName));
        return true;
    }

    // 0 = inherit; otherwise (TextHorizontalAlignment + 1) so the renderer can tell "no override" apart from Left.
    private static byte ParseAlign(string value) => value.ToLowerInvariant() switch
    {
        "left" => 1,
        "center" or "centre" => 2,
        "right" => 3,
        "justify" or "justified" => 4,
        _ => 0,
    };

    // Reference uses percent (150 -> 1.5). Accept a bare multiplier too (1.5 -> 1.5): treat values >= 3 as percent.
    private static float ParseLineHeight(string value)
    {
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float n) || n <= 0f)
            return 0f;
        return n >= 3f ? n * 0.01f : n;
    }

    private static T Pop<T>(List<T> stack)
    {
        var v = stack[stack.Count - 1];
        stack.RemoveAt(stack.Count - 1);
        return v;
    }

    private static bool HandleTag(string tag, in color baseColor, List<Span> stack,
        ref color current, ref byte currentStyle, ref float currentSize, ref color currentMark, ref byte currentCase)
    {
        if (tag.Length == 0)
            return false;

        if (tag[0] == '/')
        {
            string closeName = tag.Substring(1).Trim().ToLowerInvariant();
            if (closeName is "color" or "alpha" or "b" or "i" or "u" or "s" or "size" or "sub" or "sup"
                or "mark" or "nobr" or "lowercase" or "uppercase" or "smallcaps" or "allcaps")
            {
                if (stack.Count > 0)
                {
                    var prev = stack[stack.Count - 1];
                    stack.RemoveAt(stack.Count - 1);
                    current = prev.Color;
                    currentStyle = prev.Style;
                    currentSize = prev.Size;
                    currentMark = prev.Mark;
                    currentCase = prev.Case;
                }
                else
                {
                    ResetToBase(in baseColor, ref current, ref currentStyle, ref currentSize, ref currentMark, ref currentCase);
                }
                return true;
            }
            return false;
        }

        int eq = tag.IndexOf('=');
        string key = (eq >= 0 ? tag.Substring(0, eq) : tag).Trim().ToLowerInvariant();
        string value = eq >= 0 ? tag.Substring(eq + 1).Trim() : string.Empty;

        // Every open tag pushes the pre-change state so its close can restore it.
        switch (key)
        {
            case "color":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                if (TryParseColor(value, out var col))
                    current = col;
                return true;
            case "alpha":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                current = new color(current.r, current.g, current.b, ParseAlpha(value, current.a));
                return true;
            case "size":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                if (TryParseSize(value, out float factor, out bool relative))
                    currentSize = relative ? currentSize * factor : factor;
                return true;
            case "b":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                currentStyle |= StyleBold;
                return true;
            case "i":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                currentStyle |= StyleItalic;
                return true;
            case "u":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                currentStyle |= StyleUnderline;
                return true;
            case "s":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                currentStyle |= StyleStrike;
                return true;
            case "sub":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                currentStyle |= StyleSub;
                currentSize *= SubSupScale;
                return true;
            case "sup":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                currentStyle |= StyleSup;
                currentSize *= SubSupScale;
                return true;
            case "mark":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                currentMark = TryParseColor(value, out var mk) ? mk : DefaultMark;
                return true;
            case "nobr":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                currentStyle |= StyleNoBreak;
                return true;
            case "lowercase":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                currentCase = CaseLower;
                return true;
            case "uppercase":
            case "allcaps":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                currentCase = CaseUpper;
                return true;
            case "smallcaps":
                Push(stack, current, currentStyle, currentSize, currentMark, currentCase);
                currentCase = CaseSmallCaps;
                return true;
            case "closeall":
                // Drop every open tag and return to the base state, without a matching close. -xlinka
                stack.Clear();
                ResetToBase(in baseColor, ref current, ref currentStyle, ref currentSize, ref currentMark, ref currentCase);
                return true;
            default:
                return false;
        }
    }

    private static void ResetToBase(in color baseColor, ref color current, ref byte currentStyle, ref float currentSize, ref color currentMark, ref byte currentCase)
    {
        current = baseColor;
        currentStyle = 0;
        currentSize = 1f;
        currentMark = NoMark;
        currentCase = CaseNone;
    }

    private static void Push(List<Span> stack, color current, byte style, float size, color mark, byte @case)
        => stack.Add(new Span(current, style, size, mark, @case));

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
