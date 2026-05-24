// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class PanelShell : UIComponent
{
    public readonly Sync<string> Title;
    public readonly Sync<float2> Size;
    public readonly Sync<float> HeaderHeight;
    public readonly Sync<float> Padding;
    public readonly Sync<bool> ShowCloseButton;
    public readonly Sync<color> BackgroundColor;
    public readonly Sync<color> HeaderColor;
    public readonly Sync<color> TextColor;
    public readonly AssetRef<FontSet> Font;

    private bool _built;
    private RectTransform? _rootRect;
    private Image? _backgroundImage;
    private Slot? _headerSlot;
    private RectTransform? _headerRect;
    private Image? _headerImage;
    private RectTransform? _titleRect;
    private Text? _titleText;
    private Slot? _closeSlot;
    private RectTransform? _closeRect;
    private Image? _closeImage;
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
        HeaderHeight = new Sync<float>(this, 44f);
        Padding = new Sync<float>(this, 8f);
        ShowCloseButton = new Sync<bool>(this, true);
        BackgroundColor = new Sync<color>(this, new color(0.035f, 0.040f, 0.050f, 0.92f));
        HeaderColor = new Sync<color>(this, new color(0.090f, 0.100f, 0.120f, 0.96f));
        TextColor = new Sync<color>(this, color.White);
        Font = new AssetRef<FontSet>(this);
    }

    // Build lazily so callers can set fields immediately after AttachComponent. - xlinka
    public override void OnChanges()
    {
        base.OnChanges();
        UpdateVisualState();
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

    private void EnsureBuilt()
    {
        if (_built) return;
        _built = true;

        _rootRect = RectTransform ?? Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
        _ = Slot.GetComponent<Canvas>() ?? Slot.AttachComponent<Canvas>();
        _backgroundImage = Slot.GetComponent<Image>() ?? Slot.AttachComponent<Image>();

        _headerSlot = Slot.AddSlot("Header");
        _headerRect = _headerSlot.AttachComponent<RectTransform>();
        _headerImage = _headerSlot.AttachComponent<Image>();

        var titleSlot = _headerSlot.AddSlot("Title");
        _titleRect = titleSlot.AttachComponent<RectTransform>();
        _titleText = titleSlot.AttachComponent<Text>();

        _contentSlot = Slot.AddSlot("Content");
        _contentRect = _contentSlot.AttachComponent<RectTransform>();

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

        if (_backgroundImage != null && !_backgroundImage.IsDestroyed)
        {
            Set(_backgroundImage.Tint, BackgroundColor.Value);
        }

        if (_headerRect != null && !_headerRect.IsDestroyed)
        {
            Set(_headerRect.AnchorMin, new float2(0f, 1f));
            Set(_headerRect.AnchorMax, new float2(1f, 1f));
            Set(_headerRect.OffsetMin, new float2(0f, -HeaderHeight.Value));
            Set(_headerRect.OffsetMax, float2.Zero);
        }

        if (_headerImage != null && !_headerImage.IsDestroyed)
        {
            Set(_headerImage.Tint, HeaderColor.Value);
        }

        if (_titleRect != null && !_titleRect.IsDestroyed)
        {
            Fill(_titleRect);
            Set(_titleRect.OffsetMin, new float2(Padding.Value, 0f));
            Set(_titleRect.OffsetMax, new float2(ShowCloseButton.Value ? -HeaderHeight.Value : -Padding.Value, 0f));
        }

        if (_titleText != null && !_titleText.IsDestroyed)
        {
            Set(_titleText.Content, Title.Value);
            Set(_titleText.Color, TextColor.Value);
            Set(_titleText.Size, 18f);
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
            Set(_contentRect.OffsetMax, new float2(-Padding.Value, -HeaderHeight.Value - Padding.Value));
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

        _closeSlot = _headerSlot.AddSlot("Close");
        _closeRect = _closeSlot.AttachComponent<RectTransform>();
        var closeButton = _closeSlot.AttachComponent<Button>();
        closeButton.Clicked += (_, _) => Close();
        _closeImage = _closeSlot.AttachComponent<Image>();

        var closeLabel = _closeSlot.AddSlot("Label");
        var closeLabelRect = closeLabel.AttachComponent<RectTransform>();
        Fill(closeLabelRect);
        _closeText = closeLabel.AttachComponent<Text>();
    }

    private void UpdateCloseButton()
    {
        if (_closeRect != null && !_closeRect.IsDestroyed)
        {
            Set(_closeRect.AnchorMin, new float2(1f, 0f));
            Set(_closeRect.AnchorMax, new float2(1f, 1f));
            Set(_closeRect.OffsetMin, new float2(-HeaderHeight.Value, 0f));
            Set(_closeRect.OffsetMax, float2.Zero);
        }

        if (_closeImage != null && !_closeImage.IsDestroyed)
        {
            Set(_closeImage.Tint, new color(0.12f, 0.13f, 0.15f, 1f));
        }

        if (_closeText != null && !_closeText.IsDestroyed)
        {
            Set(_closeText.Content, "x");
            Set(_closeText.Color, TextColor.Value);
            Set(_closeText.Size, 18f);
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
        _closeImage = null;
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
            field.Target = target;
        }
    }
}
