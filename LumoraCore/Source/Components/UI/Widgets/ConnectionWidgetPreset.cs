// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public sealed class ConnectionWidgetPreset : WidgetPreset
{
    public readonly AssetRef<FontSet> Font;
    public readonly Sync<string> StatusText;
    public readonly Sync<color> DotColor;
    public readonly Sync<color> LabelColor;

    public ConnectionWidgetPreset()
    {
        Font = new AssetRef<FontSet>(this);
        StatusText = new Sync<string>(this, "Connected");
        DotColor = new Sync<color>(this, new color(0.30f, 0.80f, 0.50f, 1f));
        LabelColor = new Sync<color>(this, new color(0.60f, 0.60f, 0.66f, 1f));
    }

    protected override void Build(Widget widget, Slot root)
    {
        var dot = root.AddSlot("Dot");
        var dotRect = dot.AttachComponent<RectTransform>();
        dotRect.AnchorMin.Value = new float2(0f, 0.5f);
        dotRect.AnchorMax.Value = new float2(0f, 0.5f);
        dotRect.OffsetMin.Value = new float2(14f, -5f);
        dotRect.OffsetMax.Value = new float2(24f, 5f);
        var dotImage = dot.AttachComponent<Image>();
        dotImage.Tint.Value = DotColor.Value;
        if (BackgroundSprite.Target != null)
        {
            dotImage.Texture.Target = BackgroundSprite.Target;
            dotImage.NineSlice.Value = true;
            dotImage.Borders.Value = new float4(5f, 5f, 5f, 5f);
        }

        var builder = new UIBuilder(root);
        builder.Font(Font.Target).FontSize(12f);
        var text = builder.Text(StatusText.Value, 12f, LabelColor.Value);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Left;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        var rect = text.RectTransform;
        if (rect != null)
        {
            rect.AnchorMin.Value = float2.Zero;
            rect.AnchorMax.Value = float2.One;
            rect.OffsetMin.Value = new float2(32f, 0f);
            rect.OffsetMax.Value = float2.Zero;
        }
    }
}
