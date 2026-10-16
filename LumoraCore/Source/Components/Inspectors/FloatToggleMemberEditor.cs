// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components;

// Checkbox over a number used as a switch (a shader's 0/1 float): any nonzero value reads on, a press
// writes 1 or 0.
[ComponentCategory("Utility/Inspectors")]
public class FloatToggleMemberEditor : MemberEditor
{
    private readonly SyncRef<Checkbox> _checkbox;

    public FloatToggleMemberEditor()
    {
        _checkbox = new SyncRef<Checkbox>(this);
    }

    protected override void BuildUI(UIBuilder ui)
    {
        ui.PushStyle();
        ui.MinWidth(InspectorUI.RowHeight);
        ui.PreferredWidth(InspectorUI.RowHeight);
        ui.FlexibleWidth(0f);
        _checkbox.Target = ui.Checkbox(IsOn(), OnToggled);
        ui.PopStyle();
    }

    protected override void RefreshDisplay()
    {
        var checkbox = _checkbox.Target;
        if (checkbox == null || checkbox.IsDestroyed)
            return;
        bool value = IsOn();
        if (checkbox.IsChecked.Value != value)
            checkbox.IsChecked.Value = value;
    }

    private bool IsOn()
    {
        var value = GetMemberValue();
        try { return value != null && Convert.ToSingle(value, CultureInfo.InvariantCulture) != 0f; }
        catch { return false; }
    }

    [SyncMethod]
    public void OnToggled(Checkbox checkbox, bool value)
    {
        var leafType = LeafType;
        if (leafType == null)
            return;
        try { SetMemberValue(Convert.ChangeType(value ? 1f : 0f, leafType, CultureInfo.InvariantCulture)); }
        catch { }
    }
}
