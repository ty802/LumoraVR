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
    }

    private static void Fill(RectTransform rect)
    {
        rect.AnchorMin.Value = float2.Zero;
        rect.AnchorMax.Value = float2.One;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
    }
}
