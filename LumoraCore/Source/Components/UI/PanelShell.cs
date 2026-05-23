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
    public readonly AssetRef<FontAsset> Font;

    private bool _built;
    private Slot? _headerSlot;
    private Slot? _contentSlot;

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
        Font = new AssetRef<FontAsset>(this);
    }

    // Note: deliberately NOT calling EnsureBuilt() from OnStart. The component lifecycle runs
    // OnStart synchronously during AttachComponent, which happens BEFORE the caller sets
    // Title/Size/Colors/etc on the freshly-attached component. Snapshotting those into the
    // visual tree at that point captures the defaults (empty title, default size, default
    // colors) and never refreshes. Defer the build until RebuildContent or a HeaderSlot/
    // ContentSlot getter is actually called — by then the caller's config block is done. - xlinka

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

        var rootRect = RectTransform ?? Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
        SetCenteredRect(rootRect, Size.Value);

        _ = Slot.GetComponent<Canvas>() ?? Slot.AttachComponent<Canvas>();

        var background = Slot.GetComponent<Image>() ?? Slot.AttachComponent<Image>();
        background.Tint.Value = BackgroundColor.Value;

        _headerSlot = Slot.AddSlot("Header");
        var headerRect = _headerSlot.AttachComponent<RectTransform>();
        headerRect.AnchorMin.Value = new float2(0f, 1f);
        headerRect.AnchorMax.Value = new float2(1f, 1f);
        headerRect.OffsetMin.Value = new float2(0f, -HeaderHeight.Value);
        headerRect.OffsetMax.Value = float2.Zero;
        _headerSlot.AttachComponent<Image>().Tint.Value = HeaderColor.Value;

        var titleSlot = _headerSlot.AddSlot("Title");
        var titleRect = titleSlot.AttachComponent<RectTransform>();
        Fill(titleRect);
        titleRect.OffsetMin.Value = new float2(Padding.Value, 0f);
        titleRect.OffsetMax.Value = new float2(ShowCloseButton.Value ? -HeaderHeight.Value : -Padding.Value, 0f);
        var titleText = titleSlot.AttachComponent<Text>();
        titleText.Content.Value = Title.Value;
        titleText.Color.Value = TextColor.Value;
        titleText.Size.Value = 18f;
        titleText.Font.Target = Font.Target;

        if (ShowCloseButton.Value)
        {
            var closeSlot = _headerSlot.AddSlot("Close");
            var closeRect = closeSlot.AttachComponent<RectTransform>();
            closeRect.AnchorMin.Value = new float2(1f, 0f);
            closeRect.AnchorMax.Value = new float2(1f, 1f);
            closeRect.OffsetMin.Value = new float2(-HeaderHeight.Value, 0f);
            closeRect.OffsetMax.Value = float2.Zero;
            var closeButton = closeSlot.AttachComponent<Button>();
            closeButton.Clicked += (_, _) => Close();
            closeSlot.AttachComponent<Image>().Tint.Value = new color(0.12f, 0.13f, 0.15f, 1f);

            var closeLabel = closeSlot.AddSlot("Label");
            var closeLabelRect = closeLabel.AttachComponent<RectTransform>();
            Fill(closeLabelRect);
            var closeText = closeLabel.AttachComponent<Text>();
            closeText.Content.Value = "x";
            closeText.Color.Value = TextColor.Value;
            closeText.Size.Value = 18f;
            closeText.Font.Target = Font.Target;
        }

        _contentSlot = Slot.AddSlot("Content");
        var contentRect = _contentSlot.AttachComponent<RectTransform>();
        Fill(contentRect);
        contentRect.OffsetMin.Value = new float2(Padding.Value, Padding.Value);
        contentRect.OffsetMax.Value = new float2(-Padding.Value, -HeaderHeight.Value - Padding.Value);
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
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(size.x * -0.5f, size.y * -0.5f);
        rect.OffsetMax.Value = new float2(size.x * 0.5f, size.y * 0.5f);
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
