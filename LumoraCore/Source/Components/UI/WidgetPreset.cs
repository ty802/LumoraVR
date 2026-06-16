// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public abstract class WidgetPreset : Component
{
    public readonly Sync<float2> MinSize;
    public readonly Sync<float2> PreferredSize;
    public readonly Sync<float2> MaxSize;
    public readonly Sync<color> Background;
    public readonly Sync<color> BorderColor;
    public readonly AssetRef<TextureAsset> BackgroundSprite;
    public readonly Sync<float> CornerRadius;
    public readonly Sync<int> GridX;
    public readonly Sync<int> GridY;
    public readonly Sync<int> GridWidth;
    public readonly Sync<int> GridHeight;

    private bool _built;

    protected WidgetPreset()
    {
        MinSize = new Sync<float2>(this, new float2(120f, 80f));
        PreferredSize = new Sync<float2>(this, new float2(240f, 160f));
        MaxSize = new Sync<float2>(this, new float2(900f, 700f));
        Background = new Sync<color>(this, new color(0.05f, 0.06f, 0.08f, 0.85f));
        BorderColor = new Sync<color>(this, new color(0f, 0f, 0f, 0f));
        BackgroundSprite = new AssetRef<TextureAsset>(this);
        CornerRadius = new Sync<float>(this, 14f);
        GridX = new Sync<int>(this, 0);
        GridY = new Sync<int>(this, 0);
        GridWidth = new Sync<int>(this, 1);
        GridHeight = new Sync<int>(this, 1);
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureBuilt();
    }

    public Widget EnsureBuilt()
    {
        var widget = Slot.GetComponent<Widget>() ?? Slot.AttachComponent<Widget>();
        if (_built)
            return widget;
        _built = true;

        widget.MinSize.Value = MinSize.Value;
        widget.PreferredSize.Value = PreferredSize.Value;
        widget.MaxSize.Value = MaxSize.Value;
        widget.GridX.Value = GridX.Value;
        widget.GridY.Value = GridY.Value;
        widget.GridWidth.Value = GridWidth.Value;
        widget.GridHeight.Value = GridHeight.Value;

        _ = Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();

        var content = Slot.AddSlot("Content");
        FillRect(content.AttachComponent<RectTransform>());
        float radius = CornerRadius.Value;
        bool hasBorder = BorderColor.Value.a > 0.01f;

        var bg = content.AttachComponent<BorderedImage>();
        bg.Tint.Value = Background.Value;
        bg.BorderTint.Value = hasBorder ? BorderColor.Value : new color(0f, 0f, 0f, 0f);
        bg.BorderThickness.Value = hasBorder ? 3f : 0f;
        if (BackgroundSprite.Target != null)
        {
            bg.Texture.Target = BackgroundSprite.Target;
            bg.NineSlice.Value = true;
            bg.Borders.Value = new float4(radius, radius, radius, radius);
        }

        var fillRoot = content;
        if (hasBorder)
        {
            fillRoot = content.AddSlot("Inner");
            var rect = fillRoot.AttachComponent<RectTransform>();
            rect.AnchorMin.Value = float2.Zero;
            rect.AnchorMax.Value = float2.One;
            rect.OffsetMin.Value = new float2(3f, 3f);
            rect.OffsetMax.Value = new float2(-3f, -3f);
        }

        Build(widget, fillRoot);
        return widget;
    }

    protected abstract void Build(Widget widget, Slot root);

    protected static void FillRect(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
