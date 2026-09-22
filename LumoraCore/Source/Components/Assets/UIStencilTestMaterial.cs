// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

// Stencil-TESTED content variant of UIUnlitMaterial. Selects the UI_UnlitStencil shader
// (stencil_mode read, compare_equal), so content only draws where the stencil equals the mask reference
// written by UIStencilWriteMaterial. Drawn after the writer via the chunk's render-priority ordering. -xlinka
[ComponentCategory("Assets/Materials/UI")]
public sealed class UIStencilTestMaterial : UIUnlitMaterial
{
    protected override MaterialType MaterialType => MaterialType.UI_StencilTest;
}
