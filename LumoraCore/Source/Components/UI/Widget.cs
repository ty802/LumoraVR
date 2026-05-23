// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class Widget : UIComponent
{
    public readonly Sync<float2> MinSize;
    public readonly Sync<float2> PreferredSize;
    public readonly Sync<float2> MaxSize;
    public readonly Sync<float> AspectRatio;
    public readonly Sync<bool> Standalone;
    public readonly Sync<int> GridX;
    public readonly Sync<int> GridY;
    public readonly Sync<int> GridWidth;
    public readonly Sync<int> GridHeight;

    public Widget()
    {
        MinSize = new Sync<float2>(this, new float2(120f, 80f));
        PreferredSize = new Sync<float2>(this, new float2(240f, 160f));
        MaxSize = new Sync<float2>(this, new float2(900f, 700f));
        AspectRatio = new Sync<float>(this, 0f);
        Standalone = new Sync<bool>(this, false);
        GridX = new Sync<int>(this, 0);
        GridY = new Sync<int>(this, 0);
        GridWidth = new Sync<int>(this, 2);
        GridHeight = new Sync<int>(this, 2);
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureRect();
    }

    public UIBuilder CreateBuilder()
    {
        EnsureRect();
        return new UIBuilder(Slot);
    }

    public Canvas EnsureCanvas()
    {
        return Slot.GetComponent<Canvas>() ?? Slot.AttachComponent<Canvas>();
    }

    private void EnsureRect()
    {
        _ = RectTransform ?? Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
    }
}
