// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Math;

namespace Lumora.Core.GodotUI;

/// <summary>
/// References a specific Control node within a GodotSceneCanvas.
/// Allows reading/writing properties of controls in loaded scenes.
/// </summary>
[ComponentCategory("GodotUI")]
public class GodotControlRef : ImplementableComponent
{
    /// <summary>
    /// Node path to the control (e.g., "Panel/VBox/Label").
    /// </summary>
    public readonly Sync<string> NodePath;

    /// <summary>
    /// Reference to the canvas this control belongs to.
    /// </summary>
    public readonly SyncRef<GodotSceneCanvas> Canvas;

    /// <summary>
    /// Whether the referenced control was found.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Event fired when a button is pressed (if the control is a Button).
    /// </summary>
    public event Action? OnButtonPressed;

    /// <summary>
    /// Event fired when text changes (if the control is a LineEdit/TextEdit).
    /// </summary>
    public event Action<string>? OnTextChanged;

    /// <summary>
    /// Event fired when a value changes (if the control is a Slider/SpinBox).
    /// </summary>
    public event Action<float>? OnValueChanged;

    /// <summary>
    /// Event fired when toggle state changes (if the control is a CheckBox/CheckButton).
    /// </summary>
    public event Action<bool>? OnToggled;

    public override void OnAwake()
    {
        base.OnAwake();

        NodePath.OnChanged += _ => NotifyChanged();
        Canvas.OnChanged += _ => NotifyChanged();
    }

    public override void OnInit()
    {
        base.OnInit();
        NodePath.Value = "";
    }

    // Methods called by hook to trigger events
    public void TriggerButtonPressed() => OnButtonPressed?.Invoke();
    public void TriggerTextChanged(string text) => OnTextChanged?.Invoke(text);
    public void TriggerValueChanged(float value) => OnValueChanged?.Invoke(value);
    public void TriggerToggled(bool pressed) => OnToggled?.Invoke(pressed);
}

/// <summary>
/// Binds a Label's text to a sync field.
/// </summary>
[ComponentCategory("GodotUI")]
public class GodotLabelBinding : ImplementableComponent
{
    /// <summary>
    /// Reference to the control (should be a Label).
    /// </summary>
    public readonly SyncRef<GodotControlRef> ControlRef;

    /// <summary>
    /// The text to display.
    /// </summary>
    public readonly Sync<string> Text;

    public override void OnAwake()
    {
        base.OnAwake();

        Text.OnChanged += _ => NotifyChanged();
        ControlRef.OnChanged += _ => NotifyChanged();
    }

    public override void OnInit()
    {
        base.OnInit();
        Text.Value = "";
    }
}

/// <summary>
/// Binds a ProgressBar/Slider value.
/// </summary>
[ComponentCategory("GodotUI")]
public class GodotValueBinding : ImplementableComponent
{
    /// <summary>
    /// Reference to the control (should be a Range-based control).
    /// </summary>
    public readonly SyncRef<GodotControlRef> ControlRef;

    /// <summary>
    /// The current value.
    /// </summary>
    public readonly Sync<float> Value;

    /// <summary>
    /// Minimum value.
    /// </summary>
    public readonly Sync<float> MinValue;

    /// <summary>
    /// Maximum value.
    /// </summary>
    public readonly Sync<float> MaxValue;

    public override void OnAwake()
    {
        base.OnAwake();

        Value.OnChanged += _ => NotifyChanged();
        MinValue.OnChanged += _ => NotifyChanged();
        MaxValue.OnChanged += _ => NotifyChanged();
        ControlRef.OnChanged += _ => NotifyChanged();
    }

    public override void OnInit()
    {
        base.OnInit();
        Value.Value = 0f;
        MinValue.Value = 0f;
        MaxValue.Value = 100f;
    }
}
