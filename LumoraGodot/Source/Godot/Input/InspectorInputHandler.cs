// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Godot;
using Lumora.Core;
using LumoraEngine = Lumora.Core.Engine;

namespace Lumora.Source.Input;

#nullable enable

public partial class InspectorInputHandler : Node3D
{
    private World? _world;
    private LumoraEngine? _engine;

    public LumoraEngine? Engine
    {
        get => _engine;
        set => _engine = value;
    }

    public World? World
    {
        get => _world ?? _engine?.WorldManager?.FocusedWorld;
        set => _world = value;
    }

    public override void _Ready()
    {
        EnsureInputActionsExist();
    }

    private static void EnsureInputActionsExist()
    {
        EnsureKeyAction("Inspect", Key.I, false);
        EnsureKeyAction("InspectWorld", Key.I, true);
        EnsureKeyAction("ToggleInspector", Key.Tab, false);
    }

    private static void EnsureKeyAction(string action, Key key, bool shift)
    {
        if (InputMap.HasAction(action))
        {
            return;
        }

        InputMap.AddAction(action);
        var keyEvent = new InputEventKey
        {
            PhysicalKeycode = key,
            ShiftPressed = shift
        };
        InputMap.ActionAddEvent(action, keyEvent);
    }
}
