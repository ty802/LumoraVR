// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Helio.UI;

// What a nine-sliced image measures its sprite's border against when it draws it. Texels draws the border
// as many canvas units wide as it is texels in the texture; FixedSize draws a whole-fraction border at the
// sprite's FixedBorderSize; RectWidth and RectHeight draw it as a share of the image's own width or height,
// which is how a pill keeps fully round ends at any size. The sprite's BorderScale multiplies all of them. -xlinka
public enum BorderSizing
{
    Texels,
    FixedSize,
    RectWidth,
    RectHeight,
}
