// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

namespace Lumora.Core.Input.Actions;

// Typed handles onto one set's actions. Call sites go through these, so an action rename is a
// compile error rather than a string that silently stops matching anything.
public abstract class InputActionGroup
{
    public InputActionSet Set { get; }

    protected InputActionGroup(InputActionSet set)
    {
        Set = set;
    }

    protected DigitalAction Digital(string name, string label, string description = "")
    {
        var action = Set.Add(new DigitalAction(name, label));
        action.Description = description;
        return action;
    }

    protected AnalogAction Analog(string name, string label, string description = "")
    {
        var action = Set.Add(new AnalogAction(name, label));
        action.Description = description;
        return action;
    }

    protected Analog2DAction Analog2D(string name, string label, string description = "")
    {
        var action = Set.Add(new Analog2DAction(name, label));
        action.Description = description;
        return action;
    }
}

// Moving the body: walk, fly, turn, jump, and the blink module's aim/backstep.
public sealed class LocomotionActions : InputActionGroup
{
    public readonly Analog2DAction Move;
    public readonly AnalogAction Turn;
    public readonly AnalogAction Fly;
    public readonly DigitalAction Jump;
    public readonly DigitalAction Sprint;
    public readonly DigitalAction Crouch;
    public readonly DigitalAction ToggleMouseCapture;
    public readonly AnalogAction ScaleUser;
    public readonly Analog2DAction LeftStick;
    public readonly Analog2DAction RightStick;
    public readonly DigitalAction BlinkAim;
    public readonly DigitalAction BlinkBackstep;

    public LocomotionActions(InputActionSet set) : base(set)
    {
        Move = Analog2D("Move", "Move", "Walk and strafe.");
        Turn = Analog("Turn", "Turn", "Snap or smooth turn. Desktop turns with the mouse instead.");
        Fly = Analog("Fly", "Fly Up / Down", "Vertical axis for noclip flight.");
        Jump = Digital("Jump", "Jump");
        Sprint = Digital("Sprint", "Sprint");
        Crouch = Digital("Crouch", "Crouch");
        ToggleMouseCapture = Digital("ToggleMouseCapture", "Release Mouse", "Frees the cursor from mouse look.");
        ScaleUser = Analog("ScaleUser", "Resize Self", "Grow or shrink yourself.");

        // The two sticks verbatim, before any of the composites above touch them. Blink reads these
        // because it wants each hand's raw tilt with its own hysteresis, not a merged movement axis.
        LeftStick = Analog2D("LeftStick", "Left Stick (raw)", "Raw left-hand VR stick, used by aim gestures.");
        RightStick = Analog2D("RightStick", "Right Stick (raw)", "Raw right-hand VR stick, used by aim gestures.");

        BlinkAim = Digital("BlinkAim", "Blink Aim", "Hold to aim the blink arc (desktop and pad).");
        BlinkBackstep = Digital("BlinkBackstep", "Blink Backstep", "Short hop backwards.");
    }
}

// One hand's tools: click, grab, tool secondary, context menu, and held-object handling.
public sealed class InteractionActions : InputActionGroup
{
    public Chirality Side => Set.Side;

    public readonly DigitalAction Primary;
    public readonly DigitalAction Grab;
    public readonly DigitalAction Secondary;
    public readonly DigitalAction ContextMenu;
    public readonly DigitalAction HoldFreeze;
    public readonly DigitalAction HoldModifier;
    public readonly AnalogAction HoldScroll;
    public readonly Analog2DAction HoldLook;
    public readonly Analog2DAction HoldAxis;
    public readonly Analog2DAction Pointer;
    public readonly Analog2DAction Stick;

    public InteractionActions(InputActionSet set) : base(set)
    {
        Primary = Digital("Primary", "Use / Click");
        Grab = Digital("Grab", "Grab");
        Secondary = Digital("Secondary", "Tool Secondary");
        ContextMenu = Digital("ContextMenu", "Context Menu");
        HoldFreeze = Digital("HoldFreeze", "Manipulate Held", "Hold to aim a carried object instead of looking.");
        HoldModifier = Digital("HoldModifier", "Scale Modifier", "Held with distance input, resizes instead of moving.");
        HoldScroll = Analog("HoldScroll", "Held Distance", "Push a carried object nearer or further.");
        HoldLook = Analog2D("HoldLook", "Held Rotation", "Pointer motion applied to a carried object.");
        HoldAxis = Analog2D("HoldAxis", "Held Axis", "Stick axis for distance (Y) and twist (X).");
        Pointer = Analog2D("Pointer", "Pointer Scroll", "Scrolls whatever the laser is pointing at.");
        Stick = Analog2D("Stick", "Menu Flick", "Deflect to pick a context menu wedge, release to take it.");

        // Distance/rotation on the same hand read the same wheel and the same stick on purpose - what
        // they mean depends on the modifier - so a radial cut is wrong here.
        HoldAxis.PerAxisDeadzone = true;
        HoldAxis.Deadzone = 0.15f;
        Pointer.PerAxisDeadzone = true;
        Pointer.Deadzone = 0.20f;
        HoldLook.ClampToUnit = false;
    }
}

// The dashboard. Live above locomotion, so opening it stops the body dead.
public sealed class MenuActions : InputActionGroup
{
    public readonly DigitalAction ToggleDashboard;

    public MenuActions(InputActionSet set) : base(set)
    {
        ToggleDashboard = Digital("ToggleDashboard", "Dashboard", "Open or close the dashboard.");
    }
}

// Desktop camera modes and the free-cam flight controls.
public sealed class CameraActions : InputActionGroup
{
    public readonly DigitalAction ThirdPerson;
    public readonly DigitalAction FreeCam;
    public readonly Analog2DAction FlyMove;
    public readonly AnalogAction FlyVertical;
    public readonly DigitalAction FlyFast;
    public readonly AnalogAction OrbitZoom;

    public CameraActions(InputActionSet set) : base(set)
    {
        ThirdPerson = Digital("ThirdPerson", "Third Person", "Toggle the orbit camera.");
        FreeCam = Digital("FreeCam", "Free Camera", "Toggle detached flying camera.");
        FlyMove = Analog2D("FlyMove", "Camera Move", "Free-cam horizontal flight.");
        FlyVertical = Analog("FlyVertical", "Camera Up / Down", "Free-cam vertical flight.");
        FlyFast = Digital("FlyFast", "Camera Boost", "Hold for fast free-cam flight.");
        OrbitZoom = Analog("OrbitZoom", "Orbit Zoom", "Third-person camera distance.");
    }
}

// World editing shortcuts that are not a tool press.
public sealed class EditingActions : InputActionGroup
{
    public readonly DigitalAction Paste;

    public EditingActions(InputActionSet set) : base(set)
    {
        Paste = Digital("Paste", "Paste", "Import whatever is on the clipboard into the world.");
    }
}
