// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Script;

// Every char of text in order. A character that takes two chars comes out as both, and Characters To
// Text puts it back together.
[Node("Lists/Text/Text To Characters")]
public sealed class TextToCharacters : ReadNode
{
    [return: Port("characters")]
    public static ScriptList<char> Run(ScriptContextBase ctx, string? text)
    {
        var characters = new ScriptList<char>(ctx.Collections);
        foreach (char c in text ?? string.Empty)
            characters.Add(c);
        return characters;
    }
}
