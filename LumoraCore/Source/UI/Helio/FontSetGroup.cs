// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;

namespace Helio.UI;

/// <summary>
/// Resolves inline font names for rich text: a <c>&lt;font=name&gt;</c> tag looks up <c>name</c> here and shapes
/// that run with the matching font. Each named font is a CHILD SLOT (named after the font) carrying a
/// <see cref="FontProvider"/>. Point a <see cref="Text"/>'s Fonts ref at one of these. Mirrors
/// <see cref="SpriteSet"/>, for fonts. -xlinka
/// </summary>
[ComponentCategory("UI/Helio")]
public sealed class FontSetGroup : Component
{
    /// <summary>The FontSet on the nearest child slot named <paramref name="name"/>, or null if there isn't one.</summary>
    public FontSet? Get(string name)
    {
        if (string.IsNullOrEmpty(name) || Slot == null)
            return null;

        var children = Slot.Children;
        for (int i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (string.Equals(child.SlotName.Value, name, StringComparison.OrdinalIgnoreCase))
                return child.GetComponent<FontProvider>()?.Asset;
        }
        return null;
    }
}
