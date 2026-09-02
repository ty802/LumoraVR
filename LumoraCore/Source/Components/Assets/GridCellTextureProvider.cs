// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Assets;

// Procedural single grid-cell texture: white lines along the left and top edges over a transparent
// interior. Tiled once per grid cell by a TiledRawImage it forms continuous grid lines, each cell
// drawing its own left/top line, which doubles as the neighbour's right/bottom edge. Baked white so
// the consumer tints and fades it. Sibling of RoundedRectTextureProvider.
[ComponentCategory("Assets/Textures")]
public sealed class GridCellTextureProvider : DynamicAssetProvider<TextureAsset>
{
    public readonly Sync<int> Size;
    public readonly Sync<int> LineThickness;

    public GridCellTextureProvider()
    {
        Size = new Sync<int>(this, 64);
        LineThickness = new Sync<int>(this, 1); // 1-texel hairline; at ~1:1 cell:texel it stays crisp
    }

    protected override void OnAssetCreated(TextureAsset asset) { }

    protected override void UpdateAsset(TextureAsset asset)
    {
        int size = System.Math.Clamp(Size.Value, 8, 256);
        int thickness = System.Math.Clamp(LineThickness.Value, 1, size / 2);
        var pixels = new byte[size * size * 4];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool onLine = x < thickness || y < thickness;
                int i = (y * size + x) * 4;
                pixels[i] = 255;
                pixels[i + 1] = 255;
                pixels[i + 2] = 255;
                pixels[i + 3] = onLine ? (byte)255 : (byte)0;
            }
        }

        asset.SetImageData(pixels, size, size, false);
    }

    protected override void OnAssetCleared() { }
}
