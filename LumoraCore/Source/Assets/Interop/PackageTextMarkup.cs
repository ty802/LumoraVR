// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Lumora.Core.Assets.Interop;

// Text from a package names colours in its inline tags by a palette family and hue, which our text parser
// does not know. Every string the import reads passes through here and those names become the hex
// they stand for, so a rules board, a format string or a value a script writes all colour as authored.
// Hex, plain names and anything else are left as they are.
//
// A size tag given as a bare number or in em is, in the package, a tenth of that number in the text's own
// size units (a world text's units are a tenth of its size too); ours reads a bare number as the size
// itself. The number is divided by ten here. Percentages mean the same on both sides. -xlinka
internal static class PackageTextMarkup
{
    private static readonly Dictionary<string, string> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["neutrals.dark"] = "#11151d",
        ["neutrals.mid"] = "#2b2f35",
        ["neutrals.midlight"] = "#86888b",
        ["neutrals.light"] = "#e1e1e0",
        ["hero.yellow"] = "#f8f770",
        ["hero.green"] = "#59eb5c",
        ["hero.red"] = "#ff7676",
        ["hero.purple"] = "#ba64f2",
        ["hero.cyan"] = "#61d1fa",
        ["hero.orange"] = "#e69e50",
        ["mid.yellow"] = "#a0a14e",
        ["mid.green"] = "#3f9e44",
        ["mid.red"] = "#ae5458",
        ["mid.purple"] = "#824aab",
        ["mid.cyan"] = "#458fab",
        ["mid.orange"] = "#976c3d",
        ["sub.yellow"] = "#484a2c",
        ["sub.green"] = "#24512c",
        ["sub.red"] = "#5d323a",
        ["sub.purple"] = "#492f64",
        ["sub.cyan"] = "#284c5d",
        ["sub.orange"] = "#48392a",
        ["dark.yellow"] = "#2b2e26",
        ["dark.green"] = "#192d24",
        ["dark.red"] = "#1a1318",
        ["dark.purple"] = "#241e35",
        ["dark.cyan"] = "#1a2a36",
        ["dark.orange"] = "#292423",
    };

    private static readonly Regex ColourTag = new(@"<(color|mark)\s*=\s*([""']?)([A-Za-z]+\.[A-Za-z]+)\2\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SizeTag = new(@"<size\s*=\s*([0-9.]+)\s*(em)?\s*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Translate(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('<') < 0)
            return text;
        if (text.IndexOf('.') >= 0)
        {
            text = ColourTag.Replace(text, match => Palette.TryGetValue(match.Groups[3].Value, out var hex)
                ? $"<{match.Groups[1].Value}={hex}>"
                : match.Value);
        }
        if (text.IndexOf("size", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            text = SizeTag.Replace(text, match => float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float size)
                ? $"<size={(size * 0.1f).ToString("0.####", CultureInfo.InvariantCulture)}>"
                : match.Value);
        }
        return text;
    }
}
