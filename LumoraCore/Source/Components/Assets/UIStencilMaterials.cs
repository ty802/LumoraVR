// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Assets;

// Stencil mask WRITER variant of UIUnlitMaterial. Selects the UI_StencilWrite shader (stencil_mode write),
// so its (usually invisible) geometry stamps the stencil reference value into the stencil buffer. Content
// drawn afterwards with UIStencilTestMaterial is then clipped to this mask's exact SHAPE. The reference is
// fixed at 1 in the shader (single-depth - non-overlapping masks reuse it safely). MaterialProvider only
// calls SetMaterialType once (OnAssetCreated), so the variant must be its own class with a fixed type rather
// than a runtime-switchable field. -xlinka
[ComponentCategory("Assets/Materials/UI")]
public sealed class UIStencilWriteMaterial : UIUnlitMaterial
{
    // Draw the mask graphic itself (true) or keep it invisible and write stencil only (false, common case).
    public readonly Sync<bool> ShowMaskGraphic;

    public UIStencilWriteMaterial()
    {
        ShowMaskGraphic = new Sync<bool>(this, false);
    }

    protected override MaterialType MaterialType => MaterialType.UI_StencilWrite;

    protected override void UpdateMaterial(MaterialAsset asset)
    {
        base.UpdateMaterial(asset);
        asset.SetBool("ShowMaskGraphic", ShowMaskGraphic.Value);
    }
}

// Stencil-TESTED content variant of UIUnlitMaterial. Selects the UI_UnlitStencil shader
// (stencil_mode read, compare_equal), so content only draws where the stencil equals the mask reference
// written by UIStencilWriteMaterial. Drawn after the writer via the chunk's render-priority ordering. -xlinka
[ComponentCategory("Assets/Materials/UI")]
public sealed class UIStencilTestMaterial : UIUnlitMaterial
{
    protected override MaterialType MaterialType => MaterialType.UI_StencilTest;
}

// Stencil-TESTED TEXT variant of UITextMaterial. Selects the UI_TextStencil shader so glyphs inside a shaped
// (circle/rounded) mask are clipped to the shape, not squared off at the mask's AABB. -xlinka
[ComponentCategory("Assets/Materials/UI/Text")]
public sealed class UIStencilTestTextMaterial : UITextMaterial
{
    protected override MaterialType MaterialType => MaterialType.UI_TextStencil;
}
