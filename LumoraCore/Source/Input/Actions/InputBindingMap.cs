// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace Lumora.Core.Input.Actions;

// Every action the local user has, the bindings behind them, and the once-per-frame pass that turns
// hardware into action values.
//
// One of these belongs to the local user, owned by the InputInterface. It is a plain runtime object,
// not a datamodel component: bindings are a property of the person sitting at the machine, not of
// the world, and nothing about them should replicate or end up in a save.
//
// The pass runs BEFORE input receivers so anything reading an action inside an update sees this
// frame's hardware, not last frame's. -xlinka
public sealed class InputBindingMap
{
    // Bumped when the shipped defaults change shape enough that stored overrides are stale.
    public const int MapVersion = 1;

    // Interaction sits ABOVE the menu: the dash is aimed at with the same laser the world is, so
    // blocking interaction while the menu is up would leave the menu unclickable.
    private const int PriorityLocomotion = 0;
    private const int PriorityMenu = 100;
    private const int PriorityEditing = 130;
    private const int PriorityCamera = 140;
    private const int PriorityInteraction = 150;

    private readonly List<InputActionSet> _sets = new();
    private readonly Dictionary<string, InputAction> _actionsByPath = new(StringComparer.Ordinal);

    private readonly KeyboardSource _keyboard;
    private readonly MouseSource _mouse;
    private readonly GamepadSource _gamepad;
    private readonly VRControllerSource _vrLeft;
    private readonly VRControllerSource _vrRight;

    public LocomotionActions Locomotion { get; }
    public InteractionActions Left { get; }
    public InteractionActions Right { get; }
    public MenuActions Menu { get; }
    public CameraActions Camera { get; }
    public EditingActions Editing { get; }

    public IReadOnlyList<InputActionSet> Sets => _sets;

    // Raised while a focused text field is eating keystrokes.
    public bool TextFocusHeld
    {
        get => _keyboard.TextFocusHeld;
        set => _keyboard.TextFocusHeld = value;
    }

    // For the controls screen's sanity readout.
    public ControlRef LastActivatedControl { get; private set; }

    // False in headless or before init.
    public bool HasEvaluated { get; private set; }

    public InputBindingMap(InputInterface input)
    {
        _keyboard = new KeyboardSource(input);
        _mouse = new MouseSource(input);
        _gamepad = new GamepadSource(input);
        _vrLeft = new VRControllerSource(input, Chirality.Left);
        _vrRight = new VRControllerSource(input, Chirality.Right);

        Locomotion = new LocomotionActions(CreateSet("Locomotion", "Locomotion", PriorityLocomotion, Chirality.None, blocksLower: false));
        Menu = new MenuActions(CreateSet("Menu", "Menu", PriorityMenu, Chirality.None, blocksLower: true));
        Editing = new EditingActions(CreateSet("Editing", "Editing", PriorityEditing, Chirality.None, blocksLower: false));
        Camera = new CameraActions(CreateSet("Camera", "Camera", PriorityCamera, Chirality.None, blocksLower: false));
        Left = new InteractionActions(CreateSet("Interaction.Left", "Left Hand", PriorityInteraction, Chirality.Left, blocksLower: false));
        Right = new InteractionActions(CreateSet("Interaction.Right", "Right Hand", PriorityInteraction, Chirality.Right, blocksLower: false));

        IndexActions();
        InputBindingDefaults.Apply(this);
    }

    private InputActionSet CreateSet(string name, string label, int priority, Chirality side, bool blocksLower)
    {
        var set = new InputActionSet(name, label, priority, side, blocksLower);
        _sets.Add(set);
        return set;
    }

    private void IndexActions()
    {
        _actionsByPath.Clear();
        foreach (var set in _sets)
        {
            foreach (var action in set.Actions)
                _actionsByPath[action.Path] = action;
        }
    }

    // Right is the fallback, which is where desktop input lands.
    public InteractionActions Interaction(Chirality side) => side == Chirality.Left ? Left : Right;

    public InputAction? Find(string path)
        => path != null && _actionsByPath.TryGetValue(path, out var action) ? action : null;

    public IEnumerable<InputAction> AllActions()
    {
        foreach (var set in _sets)
        {
            foreach (var action in set.Actions)
                yield return action;
        }
    }

    // Null when that hardware is not present.
    public IInputSource? Resolve(in ControlRef control) => control.Device switch
    {
        InputDeviceKind.Keyboard => _keyboard,
        InputDeviceKind.Mouse => _mouse,
        InputDeviceKind.Gamepad => _gamepad,
        InputDeviceKind.VRController => control.Side == Chirality.Left ? _vrLeft : _vrRight,
        _ => null
    };

    public bool IsDeviceAvailable(InputDeviceKind device) => device switch
    {
        InputDeviceKind.Keyboard => _keyboard.IsAvailable,
        InputDeviceKind.Mouse => _mouse.IsAvailable,
        InputDeviceKind.Gamepad => _gamepad.IsAvailable,
        InputDeviceKind.VRController => _vrLeft.IsAvailable || _vrRight.IsAvailable,
        _ => false
    };

    // GATING

    // Called by the InputInterface just before evaluation, with the current suppression state.
    public void SetGate(InputActionSet set, bool open)
    {
        if (set != null)
            set.GateOpen = open;
    }

    // EVALUATION

    // Sets that are gated off get released rather than skipped, so a button held when the gate
    // closed reports its release instead of staying stuck down forever.
    public void Evaluate(float deltaTime)
    {
        HasEvaluated = true;

        // A set that asserts itself shuts out everything below it. That is the whole of "the menu
        // blocks locomotion" - no flags passed down, no module checking whether a panel is up.
        int blockBelow = int.MinValue;
        foreach (var set in _sets)
        {
            if (set.BlocksLower && set.Enabled && set.Asserting && set.Priority > blockBelow)
                blockBelow = set.Priority;
        }

        bool capturing = UpdateCapture(deltaTime);

        foreach (var set in _sets)
        {
            bool live = set.IsLive && set.Priority >= blockBelow && !capturing;
            foreach (var action in set.Actions)
            {
                if (!live || !action.Enabled)
                {
                    action.Release(deltaTime);
                    continue;
                }
                EvaluateAction(action, deltaTime);
            }
        }

        TrackLastActivated();
    }

    private void EvaluateAction(InputAction action, float deltaTime)
    {
        action.Begin();
        foreach (var binding in action.Bindings)
        {
            var source = Resolve(binding.Control);
            if (source == null || !source.IsAvailable)
                continue;
            if (!ModifiersHeld(binding))
                continue;
            action.Accumulate(binding, source);
        }
        action.Finish(deltaTime);
    }

    private bool ModifiersHeld(InputBinding binding)
    {
        var modifiers = binding.Modifiers;
        for (int i = 0; i < modifiers.Count; i++)
        {
            var source = Resolve(modifiers[i]);
            if (source == null || !source.IsAvailable || !source.ReadDigital(modifiers[i]))
                return false;
        }
        return true;
    }

    private void TrackLastActivated()
    {
        if (_mouse.TryCaptureControl(out var mouseControl)) { LastActivatedControl = mouseControl; return; }
        if (_gamepad.TryCaptureControl(out var padControl)) { LastActivatedControl = padControl; return; }
        if (_vrRight.TryCaptureControl(out var rightControl)) { LastActivatedControl = rightControl; return; }
        if (_vrLeft.TryCaptureControl(out var leftControl)) { LastActivatedControl = leftControl; return; }
        if (_keyboard.TryCaptureControl(out var keyControl)) LastActivatedControl = keyControl;
    }

    // REBIND LISTENING

    public enum CaptureState
    {
        Idle,
        Listening,
        Captured,
        Cancelled
    }

    public CaptureState CaptureStatus { get; private set; } = CaptureState.Idle;
    public InputAction? CaptureTarget { get; private set; }

    // It is a list because the controls screen presents keyboard and mouse as one column - they are
    // one desk - so a rebind there has to accept whichever of the two the user reaches for.
    public IReadOnlyList<InputDeviceKind> CaptureDevices { get; private set; } = Array.Empty<InputDeviceKind>();

    // Actions in the same set that already use the control just captured.
    public IReadOnlyList<InputAction> CaptureConflicts { get; private set; } = Array.Empty<InputAction>();

    // Raised when a capture finishes, so the screen can repaint and persist.
    public event Action<InputAction?>? BindingsChanged;

    // Listening gates every set off, which is right while somebody is genuinely mid-rebind and very
    // wrong if the screen that started it went away without cancelling. Nothing should be able to
    // leave a client unable to move; if no control arrives in this long, the listener gives up on
    // its own. -xlinka
    private const float CaptureTimeout = 8f;
    private float _captureAge;

    // Every set is gated off while listening, so the key you press to rebind does not also fire
    // whatever it is currently bound to.
    public void BeginCapture(InputAction action, params InputDeviceKind[] devices)
    {
        if (action == null || !action.Rebindable || devices == null || devices.Length == 0)
            return;
        CaptureTarget = action;
        CaptureDevices = devices;
        CaptureStatus = CaptureState.Listening;
        CaptureConflicts = Array.Empty<InputAction>();
        _captureAge = 0f;
    }

    public void CancelCapture()
    {
        CaptureTarget = null;
        CaptureDevices = Array.Empty<InputDeviceKind>();
        CaptureStatus = CaptureState.Cancelled;
    }

    public void ClearCapture()
    {
        CaptureTarget = null;
        CaptureDevices = Array.Empty<InputDeviceKind>();
        CaptureStatus = CaptureState.Idle;
        CaptureConflicts = Array.Empty<InputAction>();
    }

    private bool UpdateCapture(float deltaTime)
    {
        if (CaptureStatus != CaptureState.Listening || CaptureTarget == null)
            return false;

        _captureAge += deltaTime;
        if (_captureAge > CaptureTimeout)
        {
            CancelCapture();
            return true;
        }

        // Escape always means "leave it alone", on every device family, and is therefore never
        // bindable. Read the raw keyboard rather than an action so a text gate cannot swallow it.
        if (_keyboard.CaptureEscape())
        {
            CancelCapture();
            return true;
        }

        ControlRef control = default;
        bool captured = false;
        foreach (var device in CaptureDevices)
        {
            if (TryCaptureFrom(device, out control))
            {
                captured = true;
                break;
            }
        }
        if (!captured)
            return true;

        var action = CaptureTarget;
        foreach (var device in CaptureDevices)
            action.ClearBindings(device);
        var binding = new InputBinding(control, BindingAxis.Auto, DefaultTargetFor(action, control));
        action.Bind(binding);
        action.IsOverridden = true;

        CaptureConflicts = action.Set?.FindConflicts(action, control) ?? (IReadOnlyList<InputAction>)Array.Empty<InputAction>();
        CaptureStatus = CaptureState.Captured;
        CaptureTarget = null;
        BindingsChanged?.Invoke(action);
        return true;
    }

    // A digital control bound onto a two-axis action has to say which half it drives, or it would
    // land on Y by default and a rebound "strafe left" would walk you forwards.
    private static BindingAxis DefaultTargetFor(InputAction action, in ControlRef control)
    {
        if (action.Kind != InputActionKind.Analog2D)
            return BindingAxis.Auto;
        return InputControlCatalog.IsVectorControl(control) ? BindingAxis.Auto : BindingAxis.Y;
    }

    private bool TryCaptureFrom(InputDeviceKind device, out ControlRef control)
    {
        control = default;
        switch (device)
        {
            case InputDeviceKind.Keyboard:
                return _keyboard.TryCaptureControl(out control);
            case InputDeviceKind.Mouse:
                return _mouse.TryCaptureControl(out control);
            case InputDeviceKind.Gamepad:
                return _gamepad.TryCaptureControl(out control);
            case InputDeviceKind.VRController:
                if (_vrRight.TryCaptureControl(out control))
                    return true;
                return _vrLeft.TryCaptureControl(out control);
            default:
                return false;
        }
    }

    // EDITING

    public void ClearBindings(InputAction action, params InputDeviceKind[] devices)
    {
        if (action == null || devices == null)
            return;
        foreach (var device in devices)
            action.ClearBindings(device);
        action.IsOverridden = true;
        BindingsChanged?.Invoke(action);
    }

    public void ResetToDefaults(InputAction action)
    {
        if (action == null)
            return;
        action.ClearBindings();
        InputBindingDefaults.ApplyTo(this, action);
        action.IsOverridden = false;
        BindingsChanged?.Invoke(action);
    }

    public void ResetAllToDefaults()
    {
        foreach (var action in AllActions())
        {
            action.ClearBindings();
            action.IsOverridden = false;
        }
        InputBindingDefaults.Apply(this);
        BindingsChanged?.Invoke(null);
    }
}
