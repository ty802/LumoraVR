// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components;

/// <summary>Slider editor for numeric members carrying [Range], with a live value readout.</summary>
public class SliderMemberEditor : MemberEditor
{
    public readonly Sync<float> Min;
    public readonly Sync<float> Max;
    public readonly Sync<bool> WholeNumbers;

    private readonly SyncRef<Slider> _slider;
    private readonly SyncRef<Text> _valueText;

    public SliderMemberEditor()
    {
        Min = new Sync<float>(this, 0f);
        Max = new Sync<float>(this, 1f);
        WholeNumbers = new Sync<bool>(this, false);
        _slider = new SyncRef<Slider>(this);
        _valueText = new SyncRef<Text>(this);
    }

    protected override void BuildUI(UIBuilder ui)
    {
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        _slider.Target = ui.Slider(ReadFloat(), Min.Value, Max.Value, OnSlid);
        ui.PopStyle();

        ui.PushStyle();
        ui.MinWidth(64f);
        ui.PreferredWidth(64f);
        ui.FlexibleWidth(0f);
        _valueText.Target = ui.Text("", InspectorUI.FontSize, InspectorUI.TextColor);
        InspectorUI.FillParent(_valueText.Target.RectTransform!);
        _valueText.Target.VerticalAlignment.Value = TextVerticalAlignment.Middle;
        ui.PopStyle();
    }

    protected override void RefreshDisplay()
    {
        float value = ReadFloat();
        var slider = _slider.Target;
        if (slider != null && !slider.IsDestroyed && MathF.Abs(slider.Value.Value - value) > 1e-5f)
            slider.Value.Value = value;
        var text = _valueText.Target;
        if (text != null && !text.IsDestroyed)
            text.Content.Value = value.ToString(WholeNumbers.Value ? "0" : "0.###", CultureInfo.InvariantCulture);
    }

    private float ReadFloat()
    {
        var value = GetMemberValue();
        try { return value == null ? 0f : Convert.ToSingle(value, CultureInfo.InvariantCulture); }
        catch { return 0f; }
    }

    [SyncMethod]
    public void OnSlid(Slider slider, float value)
    {
        var leafType = LeafType;
        if (leafType == null)
            return;
        if (WholeNumbers.Value)
            value = MathF.Round(value);
        try { SetMemberValue(Convert.ChangeType(value, leafType, CultureInfo.InvariantCulture)); }
        catch { }
    }
}
