// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Globalization;
using Helio.UI;
using Lumora.Core;
using Lumora.Core.Math;

namespace Lumora.Core.Components;

/// <summary>
/// Euler-angle editor for floatQ members: yaw/pitch/roll text fields in DEGREES (floatQ.Euler takes
/// radians; the conversion lives here so nobody types 90 and gets 90 radians).
/// </summary>
public class QuaternionMemberEditor : MemberEditor
{
    private readonly SyncRef<TextInput> _yaw;
    private readonly SyncRef<TextInput> _pitch;
    private readonly SyncRef<TextInput> _roll;

    public QuaternionMemberEditor()
    {
        _yaw = new SyncRef<TextInput>(this);
        _pitch = new SyncRef<TextInput>(this);
        _roll = new SyncRef<TextInput>(this);
    }

    protected override void BuildUI(UIBuilder ui)
    {
        ui.PushStyle();
        ui.FlexibleWidth(1f);
        _yaw.Target = InspectorUI.CreateTextInput(ui, "Yaw");
        _pitch.Target = InspectorUI.CreateTextInput(ui, "Pitch");
        _roll.Target = InspectorUI.CreateTextInput(ui, "Roll");
        ui.PopStyle();

        _yaw.Target.SetSubmitAction(OnSubmitted);
        _pitch.Target.SetSubmitAction(OnSubmitted);
        _roll.Target.SetSubmitAction(OnSubmitted);
    }

    protected override void RefreshDisplay()
    {
        if (GetMemberValue() is not floatQ rotation)
            return;
        var euler = rotation.ToEuler(); // radians, (yaw, pitch, roll)
        WriteField(_yaw.Target, euler.x);
        WriteField(_pitch.Target, euler.y);
        WriteField(_roll.Target, euler.z);
    }

    private static void WriteField(TextInput? input, float radians)
    {
        if (input == null || input.IsDestroyed || input.IsFocused)
            return;
        input.Text.Value = (radians * (180f / MathF.PI)).ToString("0.##", CultureInfo.InvariantCulture);
    }

    [SyncMethod]
    public void OnSubmitted(TextInput input, string text)
    {
        if (TryRead(_yaw.Target, out float yaw) && TryRead(_pitch.Target, out float pitch) && TryRead(_roll.Target, out float roll))
        {
            const float DegToRad = MathF.PI / 180f;
            SetMemberValue(floatQ.Euler(yaw * DegToRad, pitch * DegToRad, roll * DegToRad));
        }
        RefreshDisplay();
    }

    private static bool TryRead(TextInput? input, out float degrees)
    {
        degrees = 0f;
        return input != null && !input.IsDestroyed
            && float.TryParse(input.Text.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out degrees);
    }
}
