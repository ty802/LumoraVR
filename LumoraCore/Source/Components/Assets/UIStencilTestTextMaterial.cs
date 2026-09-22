// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

// Stencil-TESTED TEXT variant of UITextMaterial. Selects the UI_TextStencil shader so glyphs inside a shaped
// (circle/rounded) mask are clipped to the shape, not squared off at the mask's AABB. -xlinka
[ComponentCategory("Assets/Materials/UI/Text")]
public sealed class UIStencilTestTextMaterial : UITextMaterial
{
    protected override MaterialType MaterialType => MaterialType.UI_TextStencil;
}
