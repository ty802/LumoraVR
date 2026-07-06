// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components;

/// <summary>&lt; name &gt; cycler for enum members.</summary>
public class EnumMemberEditor : MemberEditor
{
    private readonly SyncRef<Text> _valueText;

    public EnumMemberEditor()
    {
        _valueText = new SyncRef<Text>(this);
    }

    protected override void BuildUI(UIBuilder ui)
    {
        // Children go straight into the editor area's HorizontalLayout - a wrapper row here
        // double-nests and garbles the row.
        ui.PushStyle();
        ui.MinWidth(30f);
        ui.PreferredWidth(30f);
        ui.FlexibleWidth(0f);
        ui.Button("<", OnShiftDown);
        ui.PopStyle();

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        _valueText.Target = ui.Text("", InspectorUI.FontSize, InspectorUI.TextColor);
        InspectorUI.FillParent(_valueText.Target.RectTransform!);
        _valueText.Target.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
        _valueText.Target.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();

        ui.PushStyle();
        ui.MinWidth(30f);
        ui.PreferredWidth(30f);
        ui.FlexibleWidth(0f);
        ui.Button(">", OnShiftUp);
        ui.PopStyle();
    }

    protected override void RefreshDisplay()
    {
        var text = _valueText.Target;
        if (text == null || text.IsDestroyed)
            return;
        text.Content.Value = GetMemberValue()?.ToString() ?? "";
    }

    [SyncMethod]
    public void OnShiftDown(Button button, UIInteractionContext context) => Shift(-1);

    [SyncMethod]
    public void OnShiftUp(Button button, UIInteractionContext context) => Shift(1);

    private void Shift(int direction)
    {
        var type = LeafType;
        var current = GetMemberValue();
        if (type == null || !type.IsEnum || current == null)
            return;

        var values = Enum.GetValues(type);
        int index = Array.IndexOf(values, current);
        if (index < 0)
            index = 0;
        index = ((index + direction) % values.Length + values.Length) % values.Length;
        SetMemberValue(values.GetValue(index));
    }
}
