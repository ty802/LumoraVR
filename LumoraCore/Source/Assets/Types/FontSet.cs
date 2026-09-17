// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

public sealed class FontSet : DynamicAsset
{
    // The members are held as an immutable array that is SWAPPED, never edited in place. The provider
    // rebuilds this set as each shared font finishes loading, and a load completes on whatever thread
    // ran it, so the rebuild lands in the middle of a glyph walk on the world thread. A List was both
    // enumerated and Clear+AddRange'd from those two threads, which is how a text field being filled
    // during world load threw "Collection was modified" out of a component's OnInit. A swap cannot be
    // observed half-applied, so a walker always sees one whole generation. -xlinka
    private FontAsset[] _fonts = Array.Empty<FontAsset>();

    public IReadOnlyList<FontAsset> Fonts => Volatile.Read(ref _fonts);
    public bool IsValid => Volatile.Read(ref _fonts).Length > 0;

    // Aggregate atlas generation across member fonts; bumps when any gains a glyph. - xlinka
    public int CacheGeneration
    {
        get
        {
            var fonts = Volatile.Read(ref _fonts);
            int g = 0;
            for (int i = 0; i < fonts.Length; i++)
                g += fonts[i].CacheGeneration;
            return g;
        }
    }

    public void AddFont(FontAsset font)
    {
        if (font == null)
            return;

        var current = Volatile.Read(ref _fonts);
        var grown = new FontAsset[current.Length + 1];
        Array.Copy(current, grown, current.Length);
        grown[current.Length] = font;
        Volatile.Write(ref _fonts, grown);
    }

    internal void SetFonts(IReadOnlyList<FontAsset> fonts)
    {
        if (fonts == null || fonts.Count == 0)
        {
            Volatile.Write(ref _fonts, Array.Empty<FontAsset>());
            return;
        }

        var next = new FontAsset[fonts.Count];
        for (int i = 0; i < next.Length; i++)
            next[i] = fonts[i];
        Volatile.Write(ref _fonts, next);
    }

    public void RequestGlyph(int codepoint, float size)
    {
        // Per-glyph hot path: no allocation, no enumerator. If the set was replaced while we walked it,
        // walk the new one, so the glyph is resident in whatever generation ends up being drawn. -xlinka
        var fonts = Volatile.Read(ref _fonts);
        while (true)
        {
            for (int i = 0; i < fonts.Length; i++)
                fonts[i].RequestGlyph(codepoint, size);

            var current = Volatile.Read(ref _fonts);
            if (ReferenceEquals(current, fonts))
                return;
            fonts = current;
        }
    }

    public bool TryGetGlyph(int codepoint, float size, out GlyphMetrics metrics, out Rect uvRect, out FontAsset? font)
    {
        var fonts = Volatile.Read(ref _fonts);
        while (true)
        {
            for (int i = 0; i < fonts.Length; i++)
            {
                var candidate = fonts[i];
                if (candidate.TryGetGlyph(codepoint, size, out metrics, out uvRect))
                {
                    font = candidate;
                    return true;
                }
            }

            var current = Volatile.Read(ref _fonts);
            if (ReferenceEquals(current, fonts))
                break;
            fonts = current;
        }

        if (fonts.Length > 0 && codepoint != '?')
        {
            var fallback = fonts[0];
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

    public float GetLineHeight(float size)
    {
        var fonts = Volatile.Read(ref _fonts);
        return fonts.Length > 0 ? fonts[0].GetLineHeight(size) : size;
    }

    public float GetAscent(float size)
    {
        var fonts = Volatile.Read(ref _fonts);
        return fonts.Length > 0 ? fonts[0].GetAscent(size) : size * 0.8f;
    }

    public float GetDescent(float size)
    {
        var fonts = Volatile.Read(ref _fonts);
        return fonts.Length > 0 ? fonts[0].GetDescent(size) : size * 0.2f;
    }

    public float GetKerning(FontAsset? font, int leftCodepoint, int rightCodepoint, float size)
        => font?.GetKerning(leftCodepoint, rightCodepoint, size) ?? 0f;

    public override void Unload()
    {
        // The member fonts are shared and owned by the AssetManager (released via the provider's
        // ReleaseAsset); this aggregate only references them, so don't unload them here.
        Volatile.Write(ref _fonts, Array.Empty<FontAsset>());
        base.Unload();
    }
}
