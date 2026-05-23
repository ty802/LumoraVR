// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Helio.UI;

public sealed class Sprite : Component
{
    public readonly AssetRef<TextureAsset> Texture;
    public readonly Sync<Rect> UVRect;
    public readonly Sync<float4> Borders;

    public Sprite()
    {
        Texture = new AssetRef<TextureAsset>(this);
        UVRect = new Sync<Rect>(this, Rect.UnitRect);
        Borders = new Sync<float4>(this, float4.Zero);
    }
}
