// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;
using Lumora.Core.Persistence;

namespace Lumora.Core.Components.UI;

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
    public readonly Sync<color> TextColor;
    public readonly Sync<color> CloseButtonColor;
    public readonly Sync<color> CloseButtonHighlightColor;
    public readonly Sync<color> CloseButtonPressedColor;
    public readonly Sync<color> CloseButtonDisabledColor;
    public readonly Sync<color> CloseIconColor;
    public readonly AssetRef<FontSet> Font;

    private bool _built;
    private Grabbable? _grabbable;
    private RectTransform? _rootRect;
    private Image? _backgroundImage;
    private Slot? _headerSlot;
    private RectTransform? _headerRect;
    private Image? _headerImage;
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
        HeaderHeight = new Sync<float>(this, 44f);
        Padding = new Sync<float>(this, 8f);
        TitleTextSize = new Sync<float>(this, 18f);
        CloseButtonPadding = new Sync<float>(this, 4f);
        ShowCloseButton = new Sync<bool>(this, true);
        BackgroundColor = new Sync<color>(this, new color(0.035f, 0.040f, 0.050f, 0.92f));
        HeaderColor = new Sync<color>(this, new color(0.090f, 0.100f, 0.120f, 0.96f));
        HeaderSeparatorColor = new Sync<color>(this, new color(0.16f, 0.18f, 0.22f, 0.95f));
        TextColor = new Sync<color>(this, color.White);
        CloseButtonColor = new Sync<color>(this, new color(0.62f, 0.16f, 0.18f, 0.98f));
        CloseButtonHighlightColor = new Sync<color>(this, new color(0.82f, 0.22f, 0.24f, 1f));
        CloseButtonPressedColor = new Sync<color>(this, new color(0.42f, 0.08f, 0.10f, 1f));
        CloseButtonDisabledColor = new Sync<color>(this, new color(0.25f, 0.25f, 0.28f, 0.7f));
        CloseIconColor = new Sync<color>(this, color.Black);
        Font = new AssetRef<FontSet>(this);
    }

    // Build lazily so callers can set fields immediately after AttachComponent. - xlinka
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
        return new UIBuilder(_contentSlot!);
    }

    public void RebuildContent(Action<UIBuilder> build)
    {
        EnsureBuilt();
        ClearContent();
        build(new UIBuilder(_contentSlot!));
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
        _ = Slot.GetOrAttachComponent<Canvas>();
        _grabbable = Slot.GetOrAttachComponent<Grabbable>();
        _backgroundImage = Slot.GetOrAttachComponent<Image>();

        _headerSlot = Slot.FindChildOrAdd("Header");
        _headerRect = _headerSlot.GetOrAttachComponent<RectTransform>();
        _headerImage = _headerSlot.GetOrAttachComponent<Image>();
        _headerGrabSurface = _headerSlot.GetOrAttachComponent<InteractionElement>();

        var titleSlot = _headerSlot.FindChildOrAdd("Title");
        _titleRect = titleSlot.GetOrAttachComponent<RectTransform>();
        _titleText = titleSlot.GetOrAttachComponent<Text>();

        _separatorSlot = _headerSlot.FindChildOrAdd("Separator");
        _separatorRect = _separatorSlot.GetOrAttachComponent<RectTransform>();
        _separatorImage = _separatorSlot.GetOrAttachComponent<Image>();

        _contentSlot = Slot.FindChildOrAdd("Content");
        _contentRect = _contentSlot.GetOrAttachComponent<RectTransform>();

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
        float headerSize = MathF.Max(0f, HeaderHeight.Value);
        float headerHeight = ShowHeader.Value ? headerSize : 0f;
        bool canGrab = AllowGrab.Value;

        if (_grabbable != null && !_grabbable.IsDestroyed)
        {
            Set(_grabbable.AllowGrab, canGrab);
            Set(_grabbable.Scalable, Scalable.Value);
        }

        if (_backgroundImage != null && !_backgroundImage.IsDestroyed)
        {
            Set(_backgroundImage.Tint, BackgroundColor.Value);
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
            Set(_headerRect.OffsetMin, new float2(0f, -headerSize));
            Set(_headerRect.OffsetMax, float2.Zero);
        }

        if (_headerImage != null && !_headerImage.IsDestroyed)
        {
            Set(_headerImage.Tint, HeaderColor.Value);
        }

        if (_separatorRect != null && !_separatorRect.IsDestroyed)
        {
            Set(_separatorRect.AnchorMin, new float2(0f, 0f));
            Set(_separatorRect.AnchorMax, new float2(1f, 0f));
            Set(_separatorRect.OffsetMin, float2.Zero);
            Set(_separatorRect.OffsetMax, new float2(0f, 2f));
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
            float headerSize = MathF.Max(0f, HeaderHeight.Value);
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

        if (_closeText != null && !_closeText.IsDestroyed)
        {
            Set(_closeText.Content, "X");
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
