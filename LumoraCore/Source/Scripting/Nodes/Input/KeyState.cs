// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using System;
using Lumora.Core.Input;
using Lumora.Script;

namespace Lumora.Core.Scripting;

// A key on this machine's keyboard, by name ("E", "Space", "LeftShift"): held, and whether it went down
// or up this frame. Nothing while a text field has the keys.
[Node("Input/Key", Capability.Input)]
public sealed class KeyState : ReadNode
{
    [return: Port("held")]
    public static bool Run(ScriptContext ctx, string? key, out bool pressed, out bool released)
    {
        pressed = false;
        released = false;
        if (ScriptInput.Of(ctx) is not { } input || !ScriptInput.KeysOpen(input) || string.IsNullOrEmpty(key)
            || !Enum.TryParse<Key>(key, ignoreCase: true, out var which))
            return false;
        pressed = input.Keyboard.IsKeyJustPressed(which);
        released = input.Keyboard.IsKeyJustReleased(which);
        return input.Keyboard.IsKeyPressed(which);
    }
}
