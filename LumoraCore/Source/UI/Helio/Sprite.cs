// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Helio.UI;

[ComponentCategory("UI/Helio")]
public sealed class Sprite : Component
{
    public readonly AssetRef<TextureAsset> Texture;
    public readonly Sync<Rect> UVRect;
    public readonly Sync<float4> Borders;
    // Optional: a material of the sprite's own (an animated strip needs one that picks the frame).
    public readonly AssetRef<MaterialAsset> Material;

    public Sprite()
    {
        Texture = new AssetRef<TextureAsset>(this);
        Material = new AssetRef<MaterialAsset>(this);
        UVRect = new Sync<Rect>(this, Rect.UnitRect);
        Borders = new Sync<float4>(this, float4.Zero);
    }
}
