// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;

namespace Lumora.Core.Assets;

[ComponentCategory("Assets/Textures")]
public sealed class RoundedRectTextureProvider : DynamicAssetProvider<TextureAsset>
{
    public readonly Sync<int> Size;
    public readonly Sync<int> Radius;

    public RoundedRectTextureProvider()
    {
        Size = new Sync<int>(this, 64);
        Radius = new Sync<int>(this, 20);
    }

    protected override void OnAssetCreated(TextureAsset asset) { }

    protected override void UpdateAsset(TextureAsset asset)
    {
        int size = System.Math.Clamp(Size.Value, 8, 256);
        float radius = System.Math.Clamp(Radius.Value, 1, size / 2);
        var pixels = new byte[size * size * 4];

        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f - half;
                float py = y + 0.5f - half;
                float qx = System.Math.Abs(px) - half + radius;
                float qy = System.Math.Abs(py) - half + radius;
                float outside = MathF.Sqrt(MathF.Max(qx, 0f) * MathF.Max(qx, 0f) + MathF.Max(qy, 0f) * MathF.Max(qy, 0f));
                float inside = MathF.Min(MathF.Max(qx, qy), 0f);
                float d = outside + inside - radius;
                float alpha = System.Math.Clamp(0.5f - d, 0f, 1f);

                int i = (y * size + x) * 4;
                pixels[i] = 255;
                pixels[i + 1] = 255;
                pixels[i + 2] = 255;
                pixels[i + 3] = (byte)(alpha * 255f + 0.5f);
            }
        }

        asset.SetImageData(pixels, size, size, false);
    }

    protected override void OnAssetCleared() { }
}
