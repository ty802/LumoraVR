// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Collections.Generic;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

public sealed class FontSet : DynamicAsset
{
    private readonly List<FontAsset> _fonts = new();

    public IReadOnlyList<FontAsset> Fonts => _fonts;
    public bool IsValid => _fonts.Count > 0;

    // Aggregate atlas generation across member fonts; bumps when any gains a glyph. - xlinka
    public int CacheGeneration
    {
        get
        {
            int g = 0;
            foreach (var font in _fonts)
                g += font.CacheGeneration;
            return g;
        }
    }

    public void AddFont(FontAsset font)
    {
        if (font != null)
        {
            _fonts.Add(font);
        }
    }

    /// <summary>
    /// Replace the member fonts. Used by the provider to rebuild the set as shared fonts load.
    /// </summary>
    internal void SetFonts(IReadOnlyList<FontAsset> fonts)
    {
        _fonts.Clear();
        _fonts.AddRange(fonts);
    }

    public void RequestGlyph(int codepoint, float size)
    {
        foreach (var font in _fonts)
        {
            font.RequestGlyph(codepoint, size);
        }
    }

    public bool TryGetGlyph(int codepoint, float size, out GlyphMetrics metrics, out Rect uvRect, out FontAsset? font)
    {
        foreach (var candidate in _fonts)
        {
            if (candidate.TryGetGlyph(codepoint, size, out metrics, out uvRect))
            {
                font = candidate;
                return true;
            }
        }

        if (_fonts.Count > 0 && codepoint != '?')
        {
            var fallback = _fonts[0];
            fallback.RequestGlyph('?', size);
            if (fallback.TryGetGlyph('?', size, out metrics, out uvRect))
            {
                font = fallback;
                return true;
            }
        }

        metrics = default;
        uvRect = default;
        font = null;
        return false;
    }

    public float GetLineHeight(float size) => _fonts.Count > 0 ? _fonts[0].GetLineHeight(size) : size;
    public float GetAscent(float size) => _fonts.Count > 0 ? _fonts[0].GetAscent(size) : size * 0.8f;
    public float GetDescent(float size) => _fonts.Count > 0 ? _fonts[0].GetDescent(size) : size * 0.2f;

    public float GetKerning(FontAsset? font, int leftCodepoint, int rightCodepoint, float size)
        => font?.GetKerning(leftCodepoint, rightCodepoint, size) ?? 0f;

    public override void Unload()
    {
        // The member fonts are shared and owned by the AssetManager (released via the provider's
        // ReleaseAsset); this aggregate only references them, so don't unload them here.
        _fonts.Clear();
        base.Unload();
    }
}
