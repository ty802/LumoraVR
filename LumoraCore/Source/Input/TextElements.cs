// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Globalization;

namespace Lumora.Core.Input;

// Carets and deletion must stop between displayed characters, including emoji and combining marks.
internal static class TextElements
{
    public static int Previous(string text, int caret)
    {
        int previous = 0;
        foreach (int boundary in StringInfo.ParseCombiningCharacters(text))
        {
            if (boundary >= caret) break;
            previous = boundary;
        }
        return previous;
    }

    public static int Next(string text, int caret)
    {
        foreach (int boundary in StringInfo.ParseCombiningCharacters(text))
            if (boundary > caret) return boundary;
        return text.Length;
    }

    public static int Floor(string text, int caret)
        => caret >= text.Length ? text.Length : Previous(text, caret + 1);

    public static string RemoveLast(string text) => text[..Previous(text, text.Length)];
}
