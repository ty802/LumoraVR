// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Text;

namespace Lumora.Core.Localization;

// Generated test locale. Every English string comes back accented, padded and bracketed:
//   "Shadow Distance"  ->  "[!! Ŝĥàðöŵ Ðíšţàñçé ~~~ !!]"
//
// It is not a joke language, it is the standard way to prove a localization pipeline before any
// translator has been hired. Three things fall out of it on sight:
//   - text that did NOT change is text that never went through a key, so it can never be translated
//   - a label that now overflows its cell will overflow in German too (the padding is the point)
//   - the brackets show where one string ends, so concatenated strings are obvious
//
// Format placeholders are copied through untouched. Mangling a {0} would turn a formatting bug into
// a crash and hide the thing this locale exists to find. -xlinka
public static class PseudoLocale
{
    public const string Code = "qps-ploc";

    // Roughly 30% longer, which is about what European translations of English UI text run to.
    private const float PadRatio = 0.3f;
    private const string Prefix = "[!! ";
    private const string Suffix = " !!]";

    public static string Transform(string? source)
    {
        if (string.IsNullOrEmpty(source))
            return source ?? string.Empty;

        var builder = new StringBuilder(source.Length + Prefix.Length + Suffix.Length + 8);
        builder.Append(Prefix);

        bool inPlaceholder = false;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '{')
                inPlaceholder = true;
            else if (c == '}')
                inPlaceholder = false;
            builder.Append(inPlaceholder || c == '}' ? c : Accent(c));
        }

        int pad = (int)(source.Length * PadRatio);
        if (pad > 0)
        {
            builder.Append(' ');
            builder.Append('~', pad);
        }
        builder.Append(Suffix);
        return builder.ToString();
    }

    private static char Accent(char c) => c switch
    {
        'a' => 'à', 'b' => 'ƀ', 'c' => 'ç', 'd' => 'ð', 'e' => 'é', 'f' => 'ƒ', 'g' => 'ğ',
        'h' => 'ĥ', 'i' => 'í', 'j' => 'ĵ', 'k' => 'ķ', 'l' => 'ļ', 'm' => 'ɱ', 'n' => 'ñ',
        'o' => 'ö', 'p' => 'þ', 'q' => 'ɋ', 'r' => 'ŕ', 's' => 'š', 't' => 'ţ', 'u' => 'ü',
        'v' => 'ṽ', 'w' => 'ŵ', 'x' => 'ẋ', 'y' => 'ý', 'z' => 'ž',
        'A' => 'À', 'B' => 'Ɓ', 'C' => 'Ç', 'D' => 'Ð', 'E' => 'É', 'F' => 'Ƒ', 'G' => 'Ğ',
        'H' => 'Ĥ', 'I' => 'Í', 'J' => 'Ĵ', 'K' => 'Ķ', 'L' => 'Ļ', 'M' => 'Ṁ', 'N' => 'Ñ',
        'O' => 'Ö', 'P' => 'Þ', 'Q' => 'Ɋ', 'R' => 'Ŕ', 'S' => 'Ŝ', 'T' => 'Ţ', 'U' => 'Ü',
        'V' => 'Ṽ', 'W' => 'Ŵ', 'X' => 'Ẋ', 'Y' => 'Ý', 'Z' => 'Ž',
        _ => c,
    };
}
