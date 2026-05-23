// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class DashboardScreen : UIComponent
{
    public readonly Sync<string> Label;
    public readonly Sync<color> ActiveColor;

    private bool _built;
    private Slot? _contentSlot;

    public Slot? ContentSlot
    {
        get
        {
            EnsureContent();
            return _contentSlot;
        }
    }

    public DashboardScreen()
    {
        Label = new Sync<string>(this, string.Empty);
        ActiveColor = new Sync<color>(this, new color(0.28f, 0.72f, 1f, 1f));
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureContent();
    }

    public void ShowScreen()
    {
        Slot.ActiveSelf.Value = true;
        OnShow();
    }

    public void HideScreen()
    {
        Slot.ActiveSelf.Value = false;
        OnHide();
    }

    protected virtual void OnShow()
    {
    }

    protected virtual void OnHide()
    {
    }

    protected virtual void OnContentReady(Slot contentSlot)
    {
    }

    protected virtual void BuildContent(UIBuilder builder)
    {
    }

    private void EnsureContent()
    {
        if (_built) return;
        _built = true;

        var rect = RectTransform ?? Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
        Fill(rect);

        _contentSlot = Slot.AddSlot("Content");
        Fill(_contentSlot.AttachComponent<RectTransform>());
        OnContentReady(_contentSlot);
        BuildContent(new UIBuilder(_contentSlot));
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
