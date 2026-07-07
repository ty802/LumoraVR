// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;

namespace Helio.UI;

/// <summary>
/// Resolves inline sprite names for rich text: a <c>&lt;sprite=name&gt;</c> tag looks up <c>name</c> here and
/// renders that sprite in the text flow. Each named sprite is a CHILD SLOT (named after the sprite) carrying a
/// <see cref="Sprite"/>. This mirrors the reference's named-glyph sprite font, backed by our texture-region
/// Sprite instead of an emoji font. Point a <see cref="Text"/>'s SpriteSet ref at one of these. -xlinka
/// </summary>
[ComponentCategory("UI/Helio")]
public sealed class SpriteSet : Component
{
    /// <summary>The Sprite on the nearest child slot named <paramref name="name"/>, or null if there isn't one.</summary>
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
