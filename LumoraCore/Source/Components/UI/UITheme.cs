// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

/// <summary>
/// Reusable UI theme: the dashboard "purple, clean, rounded" palette plus a shared rounded-rect
/// sprite and font, so any panel can be themed consistently. Attach one next to a panel and call
/// <see cref="ApplyTo"/>, or read its colors directly when building content.
/// </summary>
[ComponentCategory("UI")]
public sealed class UITheme : Component
{
    public readonly Sync<color> PanelBackground;
    public readonly Sync<color> Header;
    public readonly Sync<color> Separator;
    public readonly Sync<color> Border;
    public readonly Sync<color> Accent;
    public readonly Sync<color> ButtonFill;
    public readonly Sync<color> ButtonText;
    /// <summary>Confirm / create actions.</summary>
    public readonly Sync<color> PositiveFill;
    /// <summary>Cancel / destructive actions (also the close button).</summary>
    public readonly Sync<color> NegativeFill;
    public readonly Sync<color> TextPrimary;
    public readonly Sync<color> TextDim;
    public readonly Sync<float> CornerRadius;
    /// <summary>Explicit font override; when unset the theme makes its own from the default UI font.</summary>
    public readonly AssetRef<FontSet> Font;

    private RoundedRectTextureProvider? _rounded;
    private FontProvider? _font;

    public UITheme()
    {
        // Opaque + higher-contrast: an in-world panel that's translucent/too-dark reads as "barely
        // visible". Lighter purple fills, bright text. - xlinka
        PanelBackground = new Sync<color>(this, new color(0.16f, 0.15f, 0.24f, 1f));
        Header = new Sync<color>(this, new color(0.34f, 0.30f, 0.52f, 1f));
        Separator = new Sync<color>(this, new color(0.62f, 0.55f, 0.95f, 0.6f));
        Border = new Sync<color>(this, new color(0.62f, 0.55f, 0.95f, 0.55f));
        Accent = new Sync<color>(this, new color(0.66f, 0.58f, 0.98f, 1f));
        ButtonFill = new Sync<color>(this, new color(0.42f, 0.38f, 0.60f, 1f));
        ButtonText = new Sync<color>(this, new color(0.97f, 0.97f, 1f, 1f));
        PositiveFill = new Sync<color>(this, new color(0.32f, 0.68f, 0.45f, 1f));
        NegativeFill = new Sync<color>(this, new color(0.72f, 0.32f, 0.34f, 1f));
        TextPrimary = new Sync<color>(this, new color(0.97f, 0.97f, 1f, 1f));
        TextDim = new Sync<color>(this, new color(0.86f, 0.85f, 0.93f, 1f));
        CornerRadius = new Sync<float>(this, 14f);
        Font = new AssetRef<FontSet>(this);
    }

    /// <summary>Shared rounded-rect sprite for panel corners (created lazily on this slot).</summary>
    public RoundedRectTextureProvider RoundedSprite
    {
        get { EnsureAssets(); return _rounded!; }
    }

    /// <summary>The font to use (explicit override, else the lazily-created default UI font).</summary>
    public IAssetProvider<FontSet>? ThemeFont
    {
        get { EnsureAssets(); return Font.Target ?? _font; }
    }

    private void EnsureAssets()
    {
        if (_rounded == null || _rounded.IsDestroyed)
        {
            // Small texture radius (relative to size) so nine-slice rounds CLEANLY even at the small
            // border UIBuilder buttons use (6px). The panel's larger CornerRadius nine-slice still reads
            // as rounded; a big texture radius left buttons looking square. - xlinka
            _rounded = Slot.AddSlot("ThemeRounded").AttachComponent<RoundedRectTextureProvider>();
            _rounded.Size.Value = 32;
            _rounded.Radius.Value = 8;
        }
        if (Font.Target == null && (_font == null || _font.IsDestroyed))
        {
            _font = Slot.AddSlot("ThemeFont").AttachComponent<FontProvider>();
            if (ImportDialog.DefaultFontUrl != null)
            {
                _font.URL.Value = ImportDialog.DefaultFontUrl;
                _font.FallbackURLs.Add(ImportDialog.DefaultFontUrl);
            }
        }
    }

    /// <summary>Theme a <see cref="PanelShell"/>: colors, rounded corners, and font.</summary>
    public void ApplyTo(PanelShell panel)
    {
        if (panel == null || panel.IsDestroyed)
            return;
        EnsureAssets();
        panel.BackgroundColor.Value = PanelBackground.Value;
        panel.HeaderColor.Value = Header.Value;
        panel.HeaderSeparatorColor.Value = Separator.Value;
        panel.TextColor.Value = TextPrimary.Value;
        panel.CloseButtonColor.Value = NegativeFill.Value;
        panel.CloseIconColor.Value = TextPrimary.Value;
        panel.CornerRadius.Value = CornerRadius.Value;
        panel.RoundedSprite.Target = RoundedSprite;
        var font = ThemeFont;
        if (font != null)
            panel.Font.Target = font;
    }
}
