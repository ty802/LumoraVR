// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Reflection;
using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components;

// One value a method row's button passes to its method. Kept as invariant text, so one synced string carries
// any simple parameter type, and parsed against the parameter when the button is pressed.
//
// A typed value writes on every keystroke. The button does not take focus from the field, so a value that
// was typed and never confirmed with Enter is still the one the press uses, instead of the stale one the
// field showed before. Enums and bools cycle through their choices. Changing a value here records no undo:
// nothing in the world changes until the button runs the method. -xlinka
[ComponentCategory("Utility/Inspectors")]
public class InspectorMethodArgument : Component
{
    public readonly Sync<string> Value;

    // '|'-separated names a stepped argument cycles through; empty for a typed one.
    public readonly Sync<string> Choices;

    private readonly SyncRef<TextInput> _input;
    private readonly SyncRef<Text> _label;

    public InspectorMethodArgument()
    {
        Value = new Sync<string>(this, "");
        Choices = new Sync<string>(this, "");
        _input = new SyncRef<TextInput>(this);
        _label = new SyncRef<Text>(this);
        Value.OnChanged += _ => Refresh();
    }

    public static bool IsSupported(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (type.IsByRef || parameter.IsOut)
            return false;
        return type.IsEnum || type == typeof(string) || type == typeof(bool)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
            || type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte);
    }

    public static bool AreSupported(ParameterInfo[] parameters)
    {
        foreach (var parameter in parameters)
        {
            if (!IsSupported(parameter))
                return false;
        }
        return true;
    }

    public static bool TryParse(string? text, Type type, out object? value)
        => PrimitiveMemberEditor.TryParse(text ?? "", type, out value);

    // The editor for one parameter, built into the builder's current container and seeded with the
    // parameter's declared default.
    public void Setup(ParameterInfo parameter, UIBuilder ui)
    {
        var type = parameter.ParameterType;
        Value.Value = PrimitiveMemberEditor.FormatValue(DefaultOf(parameter));

        if (type.IsEnum || type == typeof(bool))
        {
            Choices.Value = type == typeof(bool) ? "False|True" : string.Join("|", Enum.GetNames(type));

            ui.PushStyle();
            ui.MinWidth(30f);
            ui.PreferredWidth(30f);
            ui.FlexibleWidth(0f);
            ui.Button("<", OnPrevious);
            ui.PopStyle();

            ui.PushStyle();
            ui.FlexibleWidth(1f);
            var label = ui.Text(Value.Value, InspectorUI.FontSize, InspectorUI.TextColor);
            InspectorUI.FillParent(label.RectTransform!);
            label.HorizontalAlignment.Value = TextHorizontalAlignment.Center;
            label.VerticalAlignment.Value = TextVerticalAlignment.Middle;
            _label.Target = label;
            ui.PopStyle();

            ui.PushStyle();
            ui.MinWidth(30f);
            ui.PreferredWidth(30f);
            ui.FlexibleWidth(0f);
            ui.Button(">", OnNext);
            ui.PopStyle();
            return;
        }

        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var input = InspectorUI.CreateTextInput(ui, alignment:
            type == typeof(string) ? TextHorizontalAlignment.Left : TextHorizontalAlignment.Center);
        input.Text.Value = Value.Value;
        input.SetChangeAction(OnTyped);
        input.SetSubmitAction(OnSubmitted);
        _input.Target = input;
        ui.PopStyle();
    }

    [SyncMethod]
    public void OnTyped(TextInput input, string text) => Value.Value = text ?? "";

    // Enter settles the text on what was stored, so what the field shows is what the button sends.
    [SyncMethod]
    public void OnSubmitted(TextInput input, string text)
    {
        Value.Value = text ?? "";
        Refresh();
    }

    [SyncMethod]
    public void OnPrevious(Button button, UIInteractionContext context) => Step(-1);

    [SyncMethod]
    public void OnNext(Button button, UIInteractionContext context) => Step(1);

    private void Step(int direction)
    {
        var choices = (Choices.Value ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (choices.Length == 0)
            return;
        int index = Array.FindIndex(choices, c => string.Equals(c, Value.Value, StringComparison.OrdinalIgnoreCase));
        index = index < 0 ? 0 : ((index + direction) % choices.Length + choices.Length) % choices.Length;
        Value.Value = choices[index];
    }

    private void Refresh()
    {
        var label = _label.Target;
        if (label != null && !label.IsDestroyed)
            label.Content.Value = Value.Value ?? "";

        var input = _input.Target;
        if (input != null && !input.IsDestroyed && !input.IsFocused && input.Text.Value != Value.Value)
            input.Text.Value = Value.Value ?? "";
    }

    private static object? DefaultOf(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (parameter.HasDefaultValue && parameter.DefaultValue != null && parameter.DefaultValue != DBNull.Value)
        {
            var value = parameter.DefaultValue;
            return type.IsEnum && value.GetType() != type ? Enum.ToObject(type, value) : value;
        }
        if (type == typeof(string))
            return "";
        return type.IsEnum ? Enum.GetValues(type).GetValue(0) : Activator.CreateInstance(type);
    }
}
