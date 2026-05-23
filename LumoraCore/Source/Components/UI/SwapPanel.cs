// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public enum SwapDirection
{
    None,
    Forward,
    Back,
}

public class SwapPanel : UIComponent
{
    private Slot? _containerSlot;
    private Slot? _currentPageSlot;

    public Slot? ContainerSlot
    {
        get
        {
            EnsureContainer();
            return _containerSlot;
        }
    }

    public Slot? CurrentPageSlot => _currentPageSlot;

    public override void OnStart()
    {
        base.OnStart();
        EnsureContainer();
    }

    public UIBuilder Show(Action<UIBuilder> build, SwapDirection direction = SwapDirection.None)
    {
        EnsureContainer();

        var oldPage = _currentPageSlot;
        var page = _containerSlot!.AddSlot(direction == SwapDirection.Back ? "PageBack" : "Page");
        Fill(page.AttachComponent<RectTransform>());
        _currentPageSlot = page;

        var builder = new UIBuilder(page);
        build(builder);
        oldPage?.Destroy();
        return builder;
    }

    public void Clear()
    {
        _currentPageSlot?.Destroy();
        _currentPageSlot = null;
    }

    private void EnsureContainer()
    {
        if (_containerSlot != null) return;

        _containerSlot = Slot.AddSlot("Pages");
        Fill(_containerSlot.AttachComponent<RectTransform>());
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
