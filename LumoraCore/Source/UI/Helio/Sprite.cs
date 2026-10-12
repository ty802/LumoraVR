// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Helio.UI;

// A region of a texture for images to show, with an optional nine-slice frame. Borders are left, bottom,
// right, top: texels of the texture, or with FractionalBorders shares of UVRect (0.5 is half of it). How wide
// the frame draws is the showing image's BorderSizing, times BorderScale; FixedBorderSize is the canvas
// width a whole-fraction border draws at under FixedSize sizing. -xlinka
[ComponentCategory("UI/Helio")]
public sealed class Sprite : Component
{
    public readonly AssetRef<TextureAsset> Texture;
    public readonly Sync<Rect> UVRect;
    public readonly Sync<float4> Borders;
    // Optional: a material of the sprite's own (an animated strip needs one that picks the frame).
    public readonly AssetRef<MaterialAsset> Material;
    public readonly Sync<bool> FractionalBorders;
    public readonly Sync<float> BorderScale;
    public readonly Sync<float> FixedBorderSize;

    public Sprite()
    {
        Texture = new AssetRef<TextureAsset>(this);
        Material = new AssetRef<MaterialAsset>(this);
        UVRect = new Sync<Rect>(this, Rect.UnitRect);
        Borders = new Sync<float4>(this, float4.Zero);
        FractionalBorders = new Sync<bool>(this, false);
        BorderScale = new Sync<float>(this, 1f);
        FixedBorderSize = new Sync<float>(this, 8f);
    }
}
