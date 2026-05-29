// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public abstract class TextWidgetPreset : WidgetPreset
{
    public readonly AssetRef<FontSet> Font;
    public readonly Sync<float> TextSize;
    public readonly Sync<color> TextColor;

    protected TextWidgetPreset()
    {
        Font = new AssetRef<FontSet>(this);
        TextSize = new Sync<float>(this, 18f);
        TextColor = new Sync<color>(this, new color(0.85f, 0.90f, 0.95f, 1f));
        MinSize.Value = new float2(90f, 40f);
        PreferredSize.Value = new float2(120f, 44f);
        MaxSize.Value = new float2(260f, 64f);
    }

    protected override void Build(Widget widget, Slot root)
    {
        var builder = new UIBuilder(root);
        builder.Font(Font.Target).FontSize(TextSize.Value);
        var text = builder.Text(string.Empty, TextSize.Value, TextColor.Value);
        text.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        text.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        var rect = text.RectTransform;
        if (rect != null)
            FillRect(rect);
        SetupText(text);
    }

    protected abstract void SetupText(Text text);
}
