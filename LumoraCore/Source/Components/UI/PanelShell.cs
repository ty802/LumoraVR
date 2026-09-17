// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Components.Meshes;
using Lumora.Core.Math;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.UI;

[ComponentCategory("UI")]
public class PanelShell : UIComponent
{
    public readonly Sync<string> Title;
    public readonly Sync<float2> Size;
    public readonly Sync<bool> AllowGrab;
    public readonly Sync<bool> Scalable;
    public readonly Sync<bool> ShowHeader;
    public readonly Sync<float> HeaderHeight;
    public readonly Sync<float> Padding;
    public readonly Sync<float> TitleTextSize;
    public readonly Sync<float> CloseButtonPadding;
    public readonly Sync<bool> ShowCloseButton;
    public readonly Sync<color> BackgroundColor;
    public readonly Sync<color> HeaderColor;
    public readonly Sync<color> HeaderSeparatorColor;
    public readonly Sync<color> BorderColor;
    public readonly Sync<color> TextColor;
    public readonly Sync<color> CloseButtonColor;
    public readonly Sync<color> CloseButtonHighlightColor;
    public readonly Sync<color> CloseButtonPressedColor;
    public readonly Sync<color> CloseButtonDisabledColor;
    public readonly Sync<color> CloseIconColor;
    public readonly AssetRef<FontSet> Font;
    // Retained for serialized panels; shell corners are now controlled by CornerRadius. -xlinka
    public readonly AssetRef<TextureAsset> RoundedSprite;
    public readonly Sync<float> CornerRadius;

    private bool _built;
    private Grabbable? _grabbable;
    private RectTransform? _rootRect;
    private RoundedPanel? _background;
    private RectTransform? _canvasRect;
    private RoundedQuadMesh? _glassMesh;
    private Slot? _glassSlot;
    private const float GlassMargin = 12f;
    private const float GlassDepth = 6f;
    private Slot? _headerSlot;
    private RectTransform? _headerRect;
    private RoundedPanel? _headerPanel;
    private InteractionElement? _headerGrabSurface;
    private Slot? _separatorSlot;
    private RectTransform? _separatorRect;
    private Image? _separatorImage;
    private RectTransform? _titleRect;
    private Text? _titleText;
    private Slot? _closeSlot;
    private RectTransform? _closeRect;
    private Button? _closeButton;
    private Image? _closeImage;
    private ColorDriver? _closeColorDriver;
    private Text? _closeText;
    private Slot? _contentSlot;
    private RectTransform? _contentRect;

    public event Action<PanelShell>? CloseRequested;

    public Slot? HeaderSlot
    {
        get
        {
            EnsureBuilt();
            return _headerSlot;
        }
    }

    public Slot? ContentSlot
    {
        get
        {
            EnsureBuilt();
            return _contentSlot;
        }
    }

    public PanelShell()
    {
        Title = new Sync<string>(this, string.Empty);
        Size = new Sync<float2>(this, new float2(640f, 420f));
        AllowGrab = new Sync<bool>(this, true);
        Scalable = new Sync<bool>(this, true);
        ShowHeader = new Sync<bool>(this, true);
        HeaderHeight = new Sync<float>(this, 52f);
        Padding = new Sync<float>(this, 8f);
        TitleTextSize = new Sync<float>(this, DashTheme.FontTitle);
        CloseButtonPadding = new Sync<float>(this, 5f);
        ShowCloseButton = new Sync<bool>(this, true);
        BackgroundColor = new Sync<color>(this, DashTheme.GlassBackdrop);
        HeaderColor = new Sync<color>(this, DashTheme.Surface);
        HeaderSeparatorColor = new Sync<color>(this, DashTheme.Divider);
        BorderColor = new Sync<color>(this, DashTheme.OutlineStrong);
        TextColor = new Sync<color>(this, DashTheme.Text);
        CloseButtonColor = new Sync<color>(this, DashTheme.Field);
        CloseButtonHighlightColor = new Sync<color>(this, DashTheme.Negative);
        CloseButtonPressedColor = new Sync<color>(this, DashTheme.SurfacePressed);
        CloseButtonDisabledColor = new Sync<color>(this, DashTheme.Surface);
        CloseIconColor = new Sync<color>(this, DashTheme.Text);
        Font = new AssetRef<FontSet>(this);
        RoundedSprite = new AssetRef<TextureAsset>(this);
        CornerRadius = new Sync<float>(this, DashTheme.RadiusPanel);
    }

    // Build lazily so callers can set fields immediately after AttachComponent. - xlinka
    public override void OnStart()
    {
        base.OnStart();
        EnsureBuilt();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        UpdateVisualState();
    }

    public override void Load(DataTreeNode node, LoadControl control)
    {
        base.Load(node, control);
        // The structure is serialized as our children, but the cached part references and _built
        // flag are not. Reconnect to the loaded children once the whole subtree is in place
        // (deferred), so title/colour updates take effect again instead of no-op'ing on _built.
        control.OnLoaded(EnsureBuilt);
    }

    public UIBuilder CreateContentBuilder()
    {
        EnsureBuilt();
        return new UIBuilder(_contentSlot!).Font(Font.Target).TextColor(TextColor.Value)
            .BackgroundColor(DashTheme.Field).CornerRadius(DashTheme.RadiusControl);
    }

    public void RebuildContent(Action<UIBuilder> build)
    {
        EnsureBuilt();
        ClearContent();
        build(CreateContentBuilder());
    }

    public void ClearContent()
    {
        if (_contentSlot == null) return;
        DestroyChildren(_contentSlot);
    }

    public void Close()
    {
        CloseRequested?.Invoke(this);
        Slot.Destroy();
    }

    // Bound to the close button as a duplicable action (not a closure) so a
    // cloned panel's close button destroys the clone, not the original.
    [SyncMethod]
    public void OnClosePressed(Button button, UIInteractionContext context)
    {
        Close();
    }

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        // find-or-create: a fresh panel builds the structure; after a load these reconnect to the
        // already-deserialized children/components instead of duplicating them.
        _rootRect = RectTransform ?? Slot.GetOrAttachComponent<RectTransform>();
        _grabbable = Slot.GetOrAttachComponent<Grabbable>();
        if (Font.Target == null)
        {
            var theme = Slot.GetComponentInParent<UITheme>() ?? Slot.GetOrAttachComponent<UITheme>();
            SetTarget(Font, theme.ThemeFont);
        }

        // The glass mesh and canvas share a parent, so grabbing/scaling moves both without the
        // canvas layout or graphic batching taking ownership of the glass. -xlinka
        var canvasSlot = Slot.FindChildOrAdd("PanelCanvas");
        _canvasRect = canvasSlot.GetOrAttachComponent<RectTransform>();
        canvasSlot.GetOrAttachComponent<Canvas>();
        _background = canvasSlot.GetOrAttachComponent<RoundedPanel>();

        // Reconnect saved shells and preserve their content while retiring the old sprite backing. -xlinka
        Slot.GetComponent<Canvas>()?.Destroy();
        Slot.GetComponent<Image>()?.Destroy();
        var oldHeader = Slot.FindChild("Header", recursive: false);
        oldHeader?.SetParent(canvasSlot, preserveGlobalTransform: false);
        var oldContent = Slot.FindChild("Content", recursive: false);
        oldContent?.SetParent(canvasSlot, preserveGlobalTransform: false);

        // Keep the shell's two-sided backing and depth occlusion. RoundedPanel now emits an actual
        // border ring beside the fill, so these no longer overlap and fight in the depth buffer.
        // Other controls retain the normal one-sided canvas material, on the same side as text.
        var backing = Slot.GetOrAttachComponent<UIUnlitMaterial>();
        backing.ZWrite.Value = Lumora.Core.Assets.ZWrite.On;
        _background.Material.Target = backing;

        _glassSlot = Slot.AddLocalSlot("PanelGlass");
        _glassSlot.LocalPosition.Value = new float3(0f, 0f, -GlassDepth);
        _glassMesh = _glassSlot.AttachComponent<RoundedQuadMesh>();
        _glassMesh.CornerSegments.Value = 8;
        _glassMesh.DualSided.Value = false;
        var glassRenderer = _glassSlot.AttachComponent<MeshRenderer>();
        glassRenderer.Mesh.Target = _glassMesh;
        var glassMaterial = DashGlass.Attach(_glassSlot);
        // Glass must draw before the canvas backdrop, whose root priority is -1.
        glassMaterial.RenderQueue.Value = 2872;
        glassRenderer.Material.Target = glassMaterial;
        glassRenderer.ShadowCastMode.Value = ShadowCastMode.Off;

        _headerSlot = canvasSlot.FindChildOrAdd("Header");
        _headerRect = _headerSlot.GetOrAttachComponent<RectTransform>();
        _headerSlot.GetComponent<Image>()?.Destroy();
        _headerSlot.GetOrAttachComponent<GraphicChunkRoot>();
        _headerPanel = _headerSlot.GetOrAttachComponent<RoundedPanel>();
        _headerGrabSurface = _headerSlot.GetOrAttachComponent<InteractionElement>();

        var titleSlot = _headerSlot.FindChildOrAdd("Title");
        _titleRect = titleSlot.GetOrAttachComponent<RectTransform>();
        _titleText = titleSlot.GetOrAttachComponent<Text>();
        titleSlot.GetOrAttachComponent<GraphicChunkRoot>();

        _separatorSlot = _headerSlot.FindChildOrAdd("Separator");
        _separatorRect = _separatorSlot.GetOrAttachComponent<RectTransform>();
        _separatorImage = _separatorSlot.GetOrAttachComponent<Image>();

        _contentSlot = canvasSlot.FindChildOrAdd("Content");
        _contentRect = _contentSlot.GetOrAttachComponent<RectTransform>();
        _contentSlot.GetOrAttachComponent<GraphicChunkRoot>();

        UpdateVisualState();
    }

    private void UpdateVisualState()
    {
        if (!_built)
        {
            return;
        }

        _rootRect ??= RectTransform ?? Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
        SetCenteredRect(_rootRect, Size.Value);
        if (_canvasRect != null) SetCenteredRect(_canvasRect, Size.Value);
        if (_glassMesh != null && !_glassMesh.IsDestroyed)
        {
            Set(_glassMesh.Size, Size.Value + new float2(GlassMargin * 2f, GlassMargin * 2f));
            Set(_glassMesh.CornerRadius, MathF.Max(0f, CornerRadius.Value) + GlassMargin);
        }
        float headerSize = MathF.Max(0f, HeaderHeight.Value);
        float headerHeight = ShowHeader.Value ? headerSize : 0f;
        bool canGrab = AllowGrab.Value;

        if (_grabbable != null && !_grabbable.IsDestroyed)
        {
            Set(_grabbable.AllowGrab, canGrab);
            Set(_grabbable.Scalable, Scalable.Value);
        }

        if (_background != null && !_background.IsDestroyed)
        {
            Set(_background.Color, BackgroundColor.Value);
            Set(_background.OutlineColor, BorderColor.Value);
            Set(_background.OutlineThickness, DashTheme.OutlineWidth);
            Set(_background.CornerRadius, CornerRadius.Value);
        }

        if (_headerSlot != null && !_headerSlot.IsDestroyed)
        {
            Set(_headerSlot.ActiveSelf, ShowHeader.Value);
        }

        if (_headerGrabSurface != null && !_headerGrabSurface.IsDestroyed)
        {
            Set(_headerGrabSurface.Interactable, canGrab);
        }

        if (_headerRect != null && !_headerRect.IsDestroyed)
        {
            Set(_headerRect.AnchorMin, new float2(0f, 1f));
            Set(_headerRect.AnchorMax, new float2(1f, 1f));
            Set(_headerRect.OffsetMin, new float2(Padding.Value, -headerSize));
            Set(_headerRect.OffsetMax, new float2(-Padding.Value, -Padding.Value));
        }

        if (_headerPanel != null && !_headerPanel.IsDestroyed)
        {
            Set(_headerPanel.Color, HeaderColor.Value);
            Set(_headerPanel.CornerRadius, MathF.Min(CornerRadius.Value, DashTheme.RadiusControl));
            Set(_headerPanel.OutlineColor, BorderColor.Value);
            Set(_headerPanel.OutlineThickness, DashTheme.OutlineWidth);
        }

        if (_separatorRect != null && !_separatorRect.IsDestroyed)
        {
            Set(_separatorRect.AnchorMin, new float2(0f, 0f));
            Set(_separatorRect.AnchorMax, new float2(1f, 0f));
            Set(_separatorRect.OffsetMin, new float2(DashTheme.RadiusControl, -4f));
            Set(_separatorRect.OffsetMax, new float2(-DashTheme.RadiusControl, -3f));
        }

        if (_separatorImage != null && !_separatorImage.IsDestroyed)
        {
            Set(_separatorImage.Tint, HeaderSeparatorColor.Value);
        }

        if (_titleRect != null && !_titleRect.IsDestroyed)
        {
            float closeWidth = ShowCloseButton.Value ? headerSize : 0f;
            Fill(_titleRect);
            Set(_titleRect.OffsetMin, new float2(Padding.Value, 0f));
            Set(_titleRect.OffsetMax, new float2(ShowCloseButton.Value ? -closeWidth : -Padding.Value, 0f));
        }

        if (_titleText != null && !_titleText.IsDestroyed)
        {
            Set(_titleText.Content, Title.Value);
            Set(_titleText.Color, TextColor.Value);
            Set(_titleText.Size, TitleTextSize.Value);
            Set(_titleText.HorizontalAlignment, TextHorizontalAlignment.Left);
            Set(_titleText.VerticalAlignment, TextVerticalAlignment.Middle);
            SetTarget(_titleText.Font, Font.Target);
        }

        if (ShowCloseButton.Value)
        {
            EnsureCloseButton();
            UpdateCloseButton();
        }
        else
        {
            DestroyCloseButton();
        }

        if (_contentRect != null && !_contentRect.IsDestroyed)
        {
            Fill(_contentRect);
            Set(_contentRect.OffsetMin, new float2(Padding.Value, Padding.Value));
            Set(_contentRect.OffsetMax, new float2(-Padding.Value, -headerHeight - Padding.Value));
        }
    }

    private void EnsureCloseButton()
    {
        if (_headerSlot == null || _headerSlot.IsDestroyed)
        {
            return;
        }

        if (_closeSlot != null && !_closeSlot.IsDestroyed)
        {
            return;
        }

        _closeSlot = _headerSlot.FindChildOrAdd("Close");
        _closeSlot.GetOrAttachComponent<GraphicChunkRoot>();
        _closeRect = _closeSlot.GetOrAttachComponent<RectTransform>();
        _closeImage = _closeSlot.GetOrAttachComponent<Image>();
        _closeButton = _closeSlot.GetOrAttachComponent<Button>();
        _closeButton.SetAction(OnClosePressed);
        _closeColorDriver = _closeSlot.GetComponent<ColorDriver>() ?? _closeButton.SetupBackgroundColor(_closeImage.Tint);

        var closeLabel = _closeSlot.FindChildOrAdd("Label");
        var closeLabelRect = closeLabel.GetOrAttachComponent<RectTransform>();
        Fill(closeLabelRect);
        _closeText = closeLabel.GetOrAttachComponent<Text>();
    }

    private void UpdateCloseButton()
    {
        if (_closeRect != null && !_closeRect.IsDestroyed)
        {
            float headerSize = MathF.Max(0f, HeaderHeight.Value - Padding.Value);
            float inset = MathF.Max(0f, MathF.Min(CloseButtonPadding.Value, headerSize * 0.45f));
            float buttonSize = MathF.Max(0f, headerSize - inset * 2f);
            Set(_closeRect.AnchorMin, new float2(1f, 0f));
            Set(_closeRect.AnchorMax, new float2(1f, 1f));
            Set(_closeRect.OffsetMin, new float2(-buttonSize - inset, inset));
            Set(_closeRect.OffsetMax, new float2(-inset, -inset));
        }

        if (_closeColorDriver != null && !_closeColorDriver.IsDestroyed)
        {
            Set(_closeColorDriver.TintColorMode, InteractionColorMode.Direct);
            Set(_closeColorDriver.NormalColor, CloseButtonColor.Value);
            Set(_closeColorDriver.HighlightColor, CloseButtonHighlightColor.Value);
            Set(_closeColorDriver.PressedColor, CloseButtonPressedColor.Value);
            Set(_closeColorDriver.DisabledColor, CloseButtonDisabledColor.Value);
            if (_closeButton != null && !_closeButton.IsDestroyed)
            {
                _closeColorDriver.Apply(_closeButton);
            }
        }
        else if (_closeImage != null && !_closeImage.IsDestroyed)
        {
            Set(_closeImage.Tint, CloseButtonColor.Value);
        }

        if (_closeImage != null && !_closeImage.IsDestroyed)
        {
            SetTarget(_closeImage.Texture, null);
            Set(_closeImage.NineSlice, false);
            Set(_closeImage.CornerRadius, DashTheme.RadiusChip);
        }

        if (_closeText != null && !_closeText.IsDestroyed)
        {
            Set(_closeText.Content, "×");
            Set(_closeText.Color, CloseIconColor.Value);
            Set(_closeText.Size, TitleTextSize.Value);
            Set(_closeText.HorizontalAlignment, TextHorizontalAlignment.Center);
            Set(_closeText.VerticalAlignment, TextVerticalAlignment.Middle);
            SetTarget(_closeText.Font, Font.Target);
        }
    }

    private void DestroyCloseButton()
    {
        if (_closeSlot != null && !_closeSlot.IsDestroyed)
        {
            _closeSlot.Destroy();
        }

        _closeSlot = null;
        _closeRect = null;
        _closeButton = null;
        _closeImage = null;
        _closeColorDriver = null;
        _closeText = null;
    }

    private static void DestroyChildren(Slot slot)
    {
        var children = new List<Slot>(slot.Children);
        foreach (var child in children)
        {
            child.Destroy();
        }

        var localChildren = new List<Slot>(slot.LocalChildren);
        foreach (var child in localChildren)
        {
            child.Destroy();
        }
    }

    public override void OnDestroy()
    {
        if (_glassSlot != null && !_glassSlot.IsDestroyed)
            _glassSlot.Destroy();
        base.OnDestroy();
    }

    private static void SetCenteredRect(RectTransform rect, in float2 size)
    {
        Set(rect.AnchorMin, new float2(0.5f, 0.5f));
        Set(rect.AnchorMax, new float2(0.5f, 0.5f));
        Set(rect.OffsetMin, new float2(size.x * -0.5f, size.y * -0.5f));
        Set(rect.OffsetMax, new float2(size.x * 0.5f, size.y * 0.5f));
    }

    private static void Fill(RectTransform rect)
    {
        Set(rect.AnchorMin, float2.Zero);
        Set(rect.AnchorMax, float2.One);
        Set(rect.OffsetMin, float2.Zero);
        Set(rect.OffsetMax, float2.Zero);
    }

    private static void Set<T>(Sync<T> field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field.Value, value))
        {
            field.Value = value;
        }
    }

    private static void SetTarget<T>(AssetRef<T> field, IAssetProvider<T>? target) where T : Asset
    {
        if (!ReferenceEquals(field.Target, target))
        {
            field.Target = target!;
        }
    }
}
