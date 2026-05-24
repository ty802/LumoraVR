// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Assets;
using Lumora.Core.Math;

namespace Helio.UI;

// snapshot of "current" styling for the UIBuilder. pushed/popped to scope changes. - xlinka
public struct UIStyle
{
    public color TextColor;
    public color ForegroundColor;
    public color BackgroundColor;
    public float FontSize;
    public IAssetProvider<FontSet>? Font;
    public float MinHeight;
    public float MinWidth;
    public float PreferredWidth;
    public float PreferredHeight;
    public float FlexibleWidth;
    public float FlexibleHeight;
    public bool UseZeroMetrics;

    public static UIStyle Default => new UIStyle
    {
        TextColor = color.White,
        ForegroundColor = color.White,
        BackgroundColor = new color(0f, 0f, 0f, 0.7f),
        FontSize = 16f,
        Font = null,
        MinHeight = 0f,
        MinWidth = 0f,
        PreferredWidth = 0f,
        PreferredHeight = 0f,
        FlexibleWidth = 1f,
        FlexibleHeight = 1f,
        UseZeroMetrics = false,
    };
}
