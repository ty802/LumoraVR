// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Helio.UI;
using Lumora.Core;

namespace Lumora.Core.Components;

// string members write LIVE on every keystroke; everything else commits on enter so partial
// numeric input ("-", "1e") never writes. undo records one edit per typing session: value
// captured on focus, recorded on focus loss.
public class PrimitiveMemberEditor : MemberEditor
{
    private readonly SyncRef<TextInput> _input;

    // Typing-session undo capture. Local: the session belongs to whoever holds keyboard focus.
    private bool _sessionHooked;
    private bool _sessionActive;
    private object? _sessionBefore;

    public PrimitiveMemberEditor()
    {
        _input = new SyncRef<TextInput>(this);
    }

    private bool IsLiveString => LeafType == typeof(string);

    protected override void BuildUI(UIBuilder ui)
    {
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        var input = InspectorUI.CreateTextInput(ui);
        if (IsLiveString)
            input.SetChangeAction(OnTyped);
        else
            input.SetSubmitAction(OnSubmitted);
        _input.Target = input;
        // Field-state color on the input backing: a driven field reads magenta so it's obvious why
        // typing into it does nothing (writes are refused while driven).
        BindStateTint(input.Slot?.GetComponent<Image>()?.Tint, InspectorUI.RowColor);
        ui.PopStyle();
        HookSession();
    }

    public override void OnStart()
    {
        base.OnStart();
        // Session events are plain local events, so hook them on every peer (the builder only ran
        // on the authority).
        HookSession();
    }

    public override void OnDestroy()
    {
        var input = _input.Target;
        if (_sessionHooked && input != null)
        {
            input.EditingStarted -= OnEditingStarted;
            input.FocusLost -= OnFocusLost;
        }
        base.OnDestroy();
    }

    private void HookSession()
    {
        if (_sessionHooked)
            return;
        var input = _input.Target;
        if (input == null)
            return;
        _sessionHooked = true;
        input.EditingStarted += OnEditingStarted;
        input.FocusLost += OnFocusLost;
    }

    private void OnEditingStarted(TextInput input)
    {
        if (!IsLiveString)
            return;
        _sessionBefore = Field?.BoxedValue;
        _sessionActive = true;
    }

    private void OnFocusLost(TextInput input, string text)
    {
        if (!_sessionActive)
            return;
        _sessionActive = false;
        var field = Field;
        if (field != null && !Equals(_sessionBefore, field.BoxedValue))
            InspectorUndo.RecordEdit(this, field, _sessionBefore, field.BoxedValue);
        _sessionBefore = null;
        RefreshDisplay();
    }

    protected override void RefreshDisplay()
    {
        var input = _input.Target;
        if (input == null || input.IsDestroyed || input.IsFocused)
            return;
        input.Text.Value = FormatValue(GetMemberValue());
    }

    // live per-keystroke write for string members; undo is handled by the session hooks
    [SyncMethod]
    public void OnTyped(TextInput input, string text)
    {
        if (!IsLiveString)
            return;
        SetMemberValueSilent(text);
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
