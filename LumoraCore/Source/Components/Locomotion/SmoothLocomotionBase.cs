// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;

namespace Lumora.Core.Components;

// Base for continuous-input movement modules. Holds the shared input bindings
// (movement axis, turn axis, jump) and a composable TurnSubmodule. Concrete
// subclasses define how the resolved movement direction is applied to the
// character (physics walk, noclip fly, etc). - xlinka
public abstract class SmoothLocomotionBase : LocomotionModule
{
    public readonly TurnSubmodule Turn = new TurnSubmodule();

    // Settings forwarded by the input helper. Override on subclasses if a mode
    // wants different defaults (e.g. noclip = larger deadzone).
    public float MovementDeadzone { get; set; } = 0.1f;
    public bool ExclusiveAxisMode { get; set; } = false;

    protected InputInterface InputInterface { get; private set; } = null!;
    protected IKeyboardDriver KeyboardDriver { get; private set; } = null!;

    protected override void OnActivated()
    {
        InputInterface = Engine.Current?.InputInterface!;
        KeyboardDriver = InputInterface?.GetKeyboardDriver()!;
        Turn.Activate(Owner);
    }

    protected override void OnDeactivated()
    {
        Turn.Deactivate();
        InputInterface = null!;
        KeyboardDriver = null!;
    }
}
