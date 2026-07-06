// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components;

/// <summary>Text-field editor for primitives, strings and anything parseable from text.</summary>
public class PrimitiveMemberEditor : MemberEditor
{
    private readonly SyncRef<TextInput> _input;

    public PrimitiveMemberEditor()
    {
        _input = new SyncRef<TextInput>(this);
    }

    protected override void BuildUI(UIBuilder ui)
    {
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var input = InspectorUI.CreateTextInput(ui);
        input.SetSubmitAction(OnSubmitted);
        _input.Target = input;
        ui.PopStyle();
    }

    protected override void RefreshDisplay()
    {
        var input = _input.Target;
        if (input == null || input.IsDestroyed || input.IsFocused)
            return;
        input.Text.Value = FormatValue(GetMemberValue());
    }

    [SyncMethod]
    public void OnSubmitted(TextInput input, string text)
    {
        var leafType = LeafType;
        if (leafType == null)
            return;
        if (TryParse(text, leafType, out object? parsed))
            SetMemberValue(parsed);
        RefreshDisplay(); // snap the text back to the canonical value either way
    }

    internal static string FormatValue(object? value)
        => value switch
        {
            null => "",
            float f => f.ToString("0.###", CultureInfo.InvariantCulture),
            double d => d.ToString("0.####", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };

    internal static bool TryParse(string text, Type type, out object? value)
    {
        value = null;
        text = text?.Trim() ?? "";
        try
        {
            if (type == typeof(string)) { value = text; return true; }
            if (type == typeof(Uri)) { value = string.IsNullOrEmpty(text) ? null : new Uri(text, UriKind.RelativeOrAbsolute); return true; }
            if (type.IsEnum) { return Enum.TryParse(type, text, ignoreCase: true, out value!); }
            value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
