// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components.UI;

public class UserspaceDashboard : UIComponent
{
    public readonly Sync<bool> IsOpen;
    public readonly Sync<float2> Size;

    private Dashboard? _dashboard;

    public Dashboard? Dashboard => _dashboard;

    public UserspaceDashboard()
    {
        IsOpen = new Sync<bool>(this, true);
        Size = new Sync<float2>(this, new float2(900f, 600f));
    }

    public override void OnStart()
    {
        base.OnStart();
        EnsureDashboard();
    }

    public override void OnCommonUpdate()
    {
        base.OnCommonUpdate();
        EnsureDashboard();
        Slot.ActiveSelf.Value = IsOpen.Value;
    }

    public void Open() => IsOpen.Value = true;
    public void Close() => IsOpen.Value = false;
    public void Toggle() => IsOpen.Value = !IsOpen.Value;

    private void EnsureDashboard()
    {
        if (_dashboard != null) return;

        var rect = RectTransform ?? Slot.GetComponent<RectTransform>() ?? Slot.AttachComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.5f, 0.5f);
        rect.AnchorMax.Value = new float2(0.5f, 0.5f);
        rect.OffsetMin.Value = new float2(Size.Value.x * -0.5f, Size.Value.y * -0.5f);
        rect.OffsetMax.Value = new float2(Size.Value.x * 0.5f, Size.Value.y * 0.5f);

        _dashboard = Slot.GetComponent<Dashboard>() ?? Slot.AttachComponent<Dashboard>();
    }
}
