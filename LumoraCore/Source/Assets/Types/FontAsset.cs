// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Math;

namespace Lumora.Core.Assets;

public class FontAsset : DynamicImplementableAsset<IFontAssetHook>
{
    private int _activeRequestCount;

    public override int ActiveRequestCount => _activeRequestCount;

    public bool IsValid => Hook?.IsValid ?? false;

    // shared atlas texture for all glyphs at the currently-resident sizes. - xlinka
    public TextureAsset? AtlasTexture => Hook?.AtlasTexture;

    public bool TryGetGlyph(int codepoint, float size, out GlyphMetrics metrics, out Rect uvRect)
    {
        if (Hook != null && Hook.TryGetGlyph(codepoint, size, out metrics, out uvRect))
        {
            return true;
        }

        metrics = default;
        uvRect = default;
        return false;
    }

    public float GetLineHeight(float size) => Hook?.GetLineHeight(size) ?? size;
    public float GetAscent(float size) => Hook?.GetAscent(size) ?? size * 0.8f;
    public float GetDescent(float size) => Hook?.GetDescent(size) ?? size * 0.2f;
    public int PixelRange => Hook?.PixelRange ?? 4;

    public float GetKerning(int leftCodepoint, int rightCodepoint, float size)
        => Hook?.GetKerning(leftCodepoint, rightCodepoint, size) ?? 0f;

    public void LoadFromFile(string path)
    {
        Hook?.LoadFromFile(path);
        Version++;
    }

    public void AddRequest() => _activeRequestCount++;

    public void RemoveRequest() => _activeRequestCount = System.Math.Max(0, _activeRequestCount - 1);

    public override void Unload()
    {
        _activeRequestCount = 0;
        base.Unload();
    }
}
