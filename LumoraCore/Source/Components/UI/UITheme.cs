// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Components.Assets;
using Lumora.Core.Components.Import;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

// Shared tool-panel palette and font. Colours use the same linear values as the dashboard. -xlinka
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
        PanelBackground = new Sync<color>(this, DashTheme.GlassBackdrop);
        Header = new Sync<color>(this, DashTheme.Surface);
        Separator = new Sync<color>(this, DashTheme.Divider);
        Border = new Sync<color>(this, DashTheme.OutlineStrong);
        Accent = new Sync<color>(this, DashTheme.Accent);
        ButtonFill = new Sync<color>(this, DashTheme.Field);
        ButtonText = new Sync<color>(this, DashTheme.Text);
        PositiveFill = new Sync<color>(this, DashTheme.Positive);
        NegativeFill = new Sync<color>(this, DashTheme.Negative);
        TextPrimary = new Sync<color>(this, DashTheme.Text);
        TextDim = new Sync<color>(this, new color(0.78f, 0.77f, 0.85f, 1f));
        CornerRadius = new Sync<float>(this, DashTheme.RadiusPanel);
        Font = new AssetRef<FontSet>(this);
    }

    /// <summary>Shared rounded-rect sprite for panel corners (created lazily on this slot).</summary>
    public RoundedRectTextureProvider RoundedSprite
    {
        get
        {
            if (_rounded == null || _rounded.IsDestroyed)
            {
                _rounded = Slot.FindChildOrAdd("ThemeRounded").GetOrAttachComponent<RoundedRectTextureProvider>();
                _rounded.Size.Value = 32;
                _rounded.Radius.Value = 8;
            }
            return _rounded;
        }
    }

    /// <summary>The font to use (explicit override, else the lazily-created default UI font).</summary>
    public IAssetProvider<FontSet>? ThemeFont
    {
        get { EnsureAssets(); return Font.Target ?? _font; }
    }

    private void EnsureAssets()
    {
        if (Font.Target == null && (_font == null || _font.IsDestroyed))
        {
            _font = Slot.FindChildOrAdd("ThemeFont").GetOrAttachComponent<FontProvider>();
            _font.URL.Value = ImportDialog.DefaultFontUrl ?? DashTheme.FontSemibold;
            if (_font.FallbackURLs.Count == 0)
                _font.FallbackURLs.Add(DashTheme.FontRegular);
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
        panel.BorderColor.Value = Border.Value;
        panel.TextColor.Value = TextPrimary.Value;
        panel.CloseButtonColor.Value = ButtonFill.Value;
        panel.CloseButtonHighlightColor.Value = NegativeFill.Value;
        panel.CloseButtonPressedColor.Value = DashTheme.SurfacePressed;
        panel.CloseIconColor.Value = TextPrimary.Value;
        panel.CornerRadius.Value = CornerRadius.Value;
        var font = ThemeFont;
        if (font != null)
            panel.Font.Target = font;
    }
}
