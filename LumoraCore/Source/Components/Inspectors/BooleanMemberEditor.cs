// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components;

/// <summary>Checkbox editor for bool members (and bool leaves inside structs).</summary>
public class BooleanMemberEditor : MemberEditor
{
    private readonly SyncRef<Checkbox> _checkbox;

    public BooleanMemberEditor()
    {
        _checkbox = new SyncRef<Checkbox>(this);
    }

    protected override void BuildUI(UIBuilder ui)
    {
        ui.PushStyle();
        ui.MinWidth(InspectorUI.RowHeight);
        var checkbox = ui.Checkbox(GetMemberValue() is true, OnToggled);
        ui.PopStyle();
        _checkbox.Target = checkbox;
    }

    protected override void RefreshDisplay()
    {
        var checkbox = _checkbox.Target;
        if (checkbox == null || checkbox.IsDestroyed)
            return;
        bool value = GetMemberValue() is true;
        if (checkbox.IsChecked.Value != value)
            checkbox.IsChecked.Value = value;
    }

    [SyncMethod]
    public void OnToggled(Checkbox checkbox, bool value) => SetMemberValue(value);
}
