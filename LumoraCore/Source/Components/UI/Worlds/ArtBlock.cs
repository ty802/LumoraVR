// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI.Worlds;

// The picture part of a card or a hero: a rounded, stencil-clipped box holding either the world's
// real thumbnail or a placeholder built out of its mode tint and its initial.
//
// The clip is a Mask in stencil mode over a RoundedPanel shape, which is what lets a picture reach
// the card's edge instead of squaring off its corners. If stencil masking is off at the canvas the
// mask falls back to a rectangular clip and the corners go square, which is a look, not a break.
//
// The placeholder is deliberately NOT a photograph of anything. A world with no published thumbnail
// gets a tinted gradient with one letter on it, so nobody mistakes it for a picture of a place that
// somebody actually rendered. -xlinka
internal sealed class ArtBlock
{
    // Every producer in the tree writes 256x144, so 16:9 is where the crop starts from; SetTexture
    // re-derives it from the real texture the moment one reports its own size.
    private const float AssumedSourceAspect = 16f / 9f;

    public readonly Slot Root;

    private readonly Slot _placeholder;
    private readonly GradientPanel _placeholderFill;
    private readonly Text _placeholderLetter;
    private readonly Slot _imageSlot;
    private readonly RawImage _image;
    private readonly float _targetAspect;

    private IAssetProvider<TextureAsset>? _texture;

    public ArtBlock(BrowserParts parts, Slot parent, float radius, float letterSize, float targetAspect,
        float inset)
    {
        _targetAspect = targetAspect;
        Root = parent.AddSlot("Art");
        // Inset by exactly the frame's outline thickness: the picture then lands on the frame's inner
        // fill and the outline stays a visible ring around it.
        BrowserParts.Inset(Root.AttachComponent<RectTransform>(), inset);
        BrowserParts.Panel(Root, color.White, radius);
        var mask = Root.AttachComponent<Mask>();
        mask.StencilMasking.Value = true;
        mask.ShowMaskGraphic.Value = false;

        _placeholder = BrowserParts.Child(Root, "Placeholder");
        _placeholderFill = _placeholder.AttachComponent<GradientPanel>();
        _placeholderLetter = parts.Label(_placeholder, string.Empty, letterSize, DashTheme.TextMuted,
            BrowserParts.Weight.Bold, TextHorizontalAlignment.Center);

        _imageSlot = BrowserParts.Child(Root, "Image");
        _image = _imageSlot.AttachComponent<RawImage>();
        _imageSlot.ActiveSelf.Value = false;
    }

    public void SetPlaceholder(WorldMode? mode, string displayName)
    {
        var top = mode.HasValue ? DashTheme.ModeTint(mode.Value) : DashTheme.Surface;
        // Knocked well down: at full strength a mode tint behind a name reads as a warning banner.
        var tinted = new color(top.r * 0.42f, top.g * 0.42f, top.b * 0.42f, 1f);
        BrowserParts.SetColor(_placeholderFill.TopLeft, tinted);
        BrowserParts.SetColor(_placeholderFill.TopRight, tinted);
        BrowserParts.SetColor(_placeholderFill.BottomLeft, DashTheme.Field);
        BrowserParts.SetColor(_placeholderFill.BottomRight, DashTheme.Field);
        BrowserParts.SetText(_placeholderLetter, InitialOf(displayName));
    }

    // Point the block at a texture, or at nothing. The UV window CROPS to cover the box instead of
    // letterboxing it: RawImage.PreserveAspect fits inside the rect, which leaves bars on a card that
    // is not the picture's shape.
    public void SetTexture(IAssetProvider<TextureAsset>? texture)
    {
        if (!ReferenceEquals(texture, _texture))
        {
            _texture = texture;
            _image.Texture.Target = texture!;
        }
        bool hasArt = texture != null;
        BrowserParts.SetActive(_imageSlot, hasArt);
        BrowserParts.SetActive(_placeholder, !hasArt);
        if (!hasArt)
            return;

        var asset = _image.Texture.Asset;
        float sourceAspect = asset != null && asset.Width > 0 && asset.Height > 0
            ? asset.Width / (float)asset.Height
            : AssumedSourceAspect;
        var window = CoverWindow(_targetAspect, sourceAspect);
        if (_image.UVRect.Value != window)
            _image.UVRect.Value = window;
    }

    // The slice of the source that fills a target box without distorting it: crop the long axis, keep
    // the short one whole.
    private static Rect CoverWindow(float targetAspect, float sourceAspect)
    {
        if (sourceAspect <= 0f || targetAspect <= 0f)
            return Rect.UnitRect;
        if (sourceAspect > targetAspect)
        {
            float width = targetAspect / sourceAspect;
            return Rect.FromMinMax(new float2((1f - width) * 0.5f, 0f), new float2((1f + width) * 0.5f, 1f));
        }
        float height = sourceAspect / targetAspect;
        return Rect.FromMinMax(new float2(0f, (1f - height) * 0.5f), new float2(1f, (1f + height) * 0.5f));
    }

    private static string InitialOf(string name)
    {
        for (int i = 0; i < name.Length; i++)
        {
            if (!char.IsWhiteSpace(name[i]))
                return char.ToUpperInvariant(name[i]).ToString();
        }
        return "?";
    }
}
