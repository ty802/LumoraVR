// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;

namespace Helio.UI;

// Resolves inline sprite names for rich text: a &lt;sprite=name&gt; tag looks up name here and
// renders that sprite in the text flow. Each named sprite is a CHILD SLOT (named after the sprite) carrying a
// Sprite. A named-glyph sprite font backed by texture-region Sprites instead of an emoji
// font. Point a Text's SpriteSet ref at one of these. -xlinka
[ComponentCategory("UI/Helio")]
public sealed class SpriteSet : Component
{
    public Sprite? Get(string name)
    {
        if (string.IsNullOrEmpty(name) || Slot == null)
            return null;

        var children = Slot.Children;
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (string.Equals(child.SlotName.Value, name, StringComparison.OrdinalIgnoreCase))
                return child.GetComponent<Sprite>();
        }
        return null;
    }
}
