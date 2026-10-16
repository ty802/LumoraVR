// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components;

// < name > cycler over a number that picks one of a fixed set ("Off=0|Front=1|Back=2"): shows the name
// of the current value and steps through the set in its order.
[ComponentCategory("Utility/Inspectors")]
public class FloatChoiceMemberEditor : MemberEditor
{
    public readonly Sync<string> Options;

    private readonly SyncRef<Text> _valueText;

    public FloatChoiceMemberEditor()
    {
        Options = new Sync<string>(this, "");
        _valueText = new SyncRef<Text>(this);
    }

    protected override void BuildUI(UIBuilder ui)
    {
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
        float current = ReadFloat();
        var (names, values) = Parse();
        int index = IndexOf(values, current);
        text.Content.Value = index >= 0 ? names[index] : current.ToString("0.###", CultureInfo.InvariantCulture);
    }

    [SyncMethod]
    public void OnShiftDown(Button button, UIInteractionContext context) => Shift(-1);

    [SyncMethod]
    public void OnShiftUp(Button button, UIInteractionContext context) => Shift(1);

    private void Shift(int direction)
    {
        var leafType = LeafType;
        var (_, values) = Parse();
        if (leafType == null || values.Length == 0)
            return;
        int index = IndexOf(values, ReadFloat());
        index = index < 0 ? 0 : ((index + direction) % values.Length + values.Length) % values.Length;
        try { SetMemberValue(Convert.ChangeType(values[index], leafType, CultureInfo.InvariantCulture)); }
        catch { }
    }

    private static int IndexOf(float[] values, float value)
    {
        for (int i = 0; i < values.Length; i++)
        {
            if (MathF.Abs(values[i] - value) < 1e-4f)
                return i;
        }
        return -1;
    }

    private float ReadFloat()
    {
        var value = GetMemberValue();
        try { return value == null ? 0f : Convert.ToSingle(value, CultureInfo.InvariantCulture); }
        catch { return 0f; }
    }

    private (string[] Names, float[] Values) Parse()
    {
        var entries = (Options.Value ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        var names = new string[entries.Length];
        var values = new float[entries.Length];
        int count = 0;
        foreach (var entry in entries)
        {
            int split = entry.LastIndexOf('=');
            if (split <= 0 || !float.TryParse(entry.AsSpan(split + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                continue;
            names[count] = entry[..split];
            values[count] = v;
            count++;
        }
        return (names[..count], values[..count]);
    }
}
