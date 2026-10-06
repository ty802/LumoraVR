// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Helio.UI;

namespace Lumora.Core.Components.Utility;

// A number typed into the TextInput on this slot. What is typed is read into ParsedValue, clamped and rounded,
// and when typing ends the text is rewritten in Format; when ParsedValue changes from elsewhere (a slider
// copied into it) the text follows. Wire ParsedValue to whatever the field edits.
//
// Typing happens on the typist's machine, so the parse does too. The text follows the value on the authority,
// and only when the text no longer reads as the value: what someone is typing already reads as what it set,
// so it is never written over under them. Nothing runs per frame. -xlinka
[ComponentCategory("Utility/Text")]
public sealed class TextInputFloatParser : Component
{
    public readonly Sync<float> ParsedValue;

    // Read the text on every change while typing, not only when typing ends.
    public readonly Sync<bool> ParseContinuously;

    // Rewrite the text when ParsedValue changes from elsewhere.
    public readonly Sync<bool> UpdateTextFromValue;

    public readonly Sync<float> Min;
    public readonly Sync<float> Max;

    // Seven or more keeps every digit.
    public readonly Sync<int> DecimalPlaces;

    // A standard numeric format: N, F2, P0. Empty writes the shortest exact form.
    public readonly Sync<string> Format;

    private TextInput? _input;
    private Action<TextInput, string>? _changed;
    private Action<TextInput, string>? _ended;
    private float _lastValue = float.NaN;

    public TextInputFloatParser()
    {
        ParsedValue = new Sync<float>(this, 0f);
        ParseContinuously = new Sync<bool>(this, true);
        UpdateTextFromValue = new Sync<bool>(this, true);
        Min = new Sync<float>(this, -2147483648f);
        Max = new Sync<float>(this, 2147483648f);
        DecimalPlaces = new Sync<int>(this, int.MaxValue);
        Format = new Sync<string>(this, string.Empty);
    }

    public override void OnStart()
    {
        base.OnStart();
        Hook();
        Follow();
    }

    public override void OnChanges()
    {
        base.OnChanges();
        Hook();
        Follow();
    }

    public override void OnDestroy()
    {
        Unhook();
        base.OnDestroy();
    }

    private void Hook()
    {
        var input = Slot?.GetComponent<TextInput>();
        if (ReferenceEquals(input, _input) && (input == null || !input.IsDestroyed))
            return;
        Unhook();
        if (input == null || input.IsDestroyed)
            return;
        _input = input;
        _changed = (_, text) =>
        {
            if (ParseContinuously.Value && TryParse(text, out float value))
                Take(value);
        };
        _ended = (field, text) =>
        {
            if (TryParse(text, out float value))
                Take(value);
            string shown = Show(ParsedValue.Value);
            if (!field.IsDestroyed && !string.Equals(field.Text.Value, shown, StringComparison.Ordinal))
                field.Text.Value = shown;
        };
        input.EditingChanged += _changed;
        input.FocusLost += _ended;
    }

    private void Unhook()
    {
        if (_input != null)
        {
            if (_changed != null)
                _input.EditingChanged -= _changed;
            if (_ended != null)
                _input.FocusLost -= _ended;
        }
        _input = null;
        _changed = null;
        _ended = null;
    }

    private void Take(float value)
    {
        if (ParsedValue.Value != value && !ParsedValue.IsBlockedByDrive)
            ParsedValue.Value = value;
    }

    // The text follows the value, where it no longer reads as it.
    private void Follow()
    {
        float value = ParsedValue.Value;
        if (value.Equals(_lastValue))
            return;
        _lastValue = value;
        var input = _input;
        if (input == null || input.IsDestroyed || !UpdateTextFromValue.Value || input.IsFocused || World?.IsAuthority != true)
            return;
        if (TryParse(input.Text.Value ?? string.Empty, out float shown) && shown.Equals(value))
            return;
        input.Text.Value = Show(value);
    }

    private string Show(float value)
    {
        string format = Format.Value ?? string.Empty;
        try
        {
            return value.ToString(format.Length > 0 ? format : "R", CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }

    private bool TryParse(string text, out float value)
    {
        value = 0f;
        string format = Format.Value ?? string.Empty;
        bool percent = format.Length > 0 && (format[0] == 'P' || format[0] == 'p');
        string trimmed = text.Replace("%", string.Empty, StringComparison.Ordinal).Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double parsed)
            || !double.IsFinite(parsed))
            return false;
        if (percent)
            parsed /= 100.0;
        int places = DecimalPlaces.Value;
        if (places >= 0 && places < 7)
            parsed = System.Math.Round(parsed, places, MidpointRounding.AwayFromZero);
        float min = Min.Value, max = Max.Value;
        value = (float)System.Math.Clamp(parsed, System.Math.Min(min, max), System.Math.Max(min, max));
        return true;
    }
}
