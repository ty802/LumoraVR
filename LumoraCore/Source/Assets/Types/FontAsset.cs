// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System.Threading.Tasks;
using Lumora.Core.Math;

namespace Lumora.Core.Assets;

/// <summary>
/// A font loaded from a URL. Self-loads its file (via the hook) on request and is shared across
/// every requester for the same font file. Glyph rasterization and the shared atlas live in the
/// hook. Always a URL (static) asset - fonts are not created procedurally.
/// </summary>
public class FontAsset : ImplementableAsset<IFontAssetHook>
{
    public bool IsValid => Hook?.IsValid ?? false;

    // shared atlas texture for all glyphs at the currently-resident sizes. - xlinka
    public TextureAsset? AtlasTexture => Hook?.AtlasTexture;

    // Changes whenever the atlas gains a glyph. Text shaping caches against it. - xlinka
    public int CacheGeneration => Hook?.CacheGeneration ?? 0;

    /// <summary>
    /// Load the font from its resolved file path through the hook. The provider resolves the URL
    /// to a local file before requesting, so this expects a file URL (fonts are local resources).
    /// </summary>
    protected override Task LoadSelf()
    {
        if (AssetURL == null || !AssetURL.IsFile)
        {
            FailLoad($"FontAsset requires a resolved file path, got {AssetURL?.Scheme}");
            return Task.CompletedTask;
        }

        Hook?.LoadFromFile(AssetURL.LocalPath);
        Version++;
        return Task.CompletedTask;
    }

    public virtual bool TryGetGlyph(int codepoint, float size, out GlyphMetrics metrics, out Rect uvRect)
    {
        if (Hook != null && Hook.TryGetGlyph(codepoint, size, out metrics, out uvRect))
        {
            return true;
        }

        metrics = default;
        uvRect = default;
        return false;
    }

    public virtual void RequestGlyph(int codepoint, float size) => Hook?.RequestGlyph(codepoint, size);

    public virtual float GetLineHeight(float size) => Hook?.GetLineHeight(size) ?? size;
    public virtual float GetAscent(float size) => Hook?.GetAscent(size) ?? size * 0.8f;
    public virtual float GetDescent(float size) => Hook?.GetDescent(size) ?? size * 0.2f;
    public virtual int PixelRange => Hook?.PixelRange ?? 4;

    public virtual float GetKerning(int leftCodepoint, int rightCodepoint, float size)
        => Hook?.GetKerning(leftCodepoint, rightCodepoint, size) ?? 0f;
}
