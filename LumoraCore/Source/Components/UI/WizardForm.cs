// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class WizardForm : UIComponent
{
    public readonly Sync<float> SlideDuration;
    public readonly Sync<float2> CanvasSize;

    private readonly List<Action<UIBuilder>> _path = new();
    private Slot? _swapSlot;
    private SwapPanel? _swapPanel;

    public int Depth => _path.Count;

    public SwapPanel? SwapPanel
    {
        get
        {
            EnsureSwapPanel();
            return _swapPanel;
        }
    }

    protected virtual float DefaultSlideDuration => 0.25f;
    protected virtual float2 DefaultCanvasSize => new float2(400f, 800f);
    public virtual float CanvasScale => 0.5f / CanvasSize.Value.y;

    public WizardForm()
    {
        SlideDuration = new Sync<float>(this, 0.25f);
        CanvasSize = new Sync<float2>(this, new float2(400f, 800f));
    }

    public override void OnInit()
    {
        base.OnInit();
        SlideDuration.Value = DefaultSlideDuration;
        CanvasSize.Value = DefaultCanvasSize;
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureSwapPanel();
    }

    public void OpenRoot(Action<UIBuilder> build)
    {
        _path.Clear();
        _path.Add(build);
        EnsureSwapPanel();
        _swapPanel!.Show(build);
    }

    public void Open(Action<UIBuilder> build)
    {
        _path.Add(build);
        EnsureSwapPanel();
        _swapPanel!.Show(build, SwapDirection.Forward);
    }

    public bool Return()
    {
        if (_path.Count <= 1) return false;

        _path.RemoveAt(_path.Count - 1);
        EnsureSwapPanel();
        _swapPanel!.Show(_path[_path.Count - 1], SwapDirection.Back);
        return true;
    }

    public void Refresh()
    {
        if (_path.Count == 0) return;
        EnsureSwapPanel();
        _swapPanel!.Show(_path[_path.Count - 1]);
    }

    private void EnsureSwapPanel()
    {
        if (_swapPanel != null) return;

        _swapSlot = Slot.AddSlot("WizardPages");
        Fill(_swapSlot.AttachComponent<RectTransform>());
        _swapPanel = _swapSlot.AttachComponent<SwapPanel>();
        _swapPanel.Duration.Value = SlideDuration.Value;
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
