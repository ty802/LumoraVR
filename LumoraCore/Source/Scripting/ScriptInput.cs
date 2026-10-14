// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Core.Input;

namespace Lumora.Core.Scripting;

// The input of whoever is at this machine, for a Local script only: nothing while this world is not the
// one in front of them, and no keys while they are typing into a text field. -xlinka
internal static class ScriptInput
{
    public static InputInterface? Of(ScriptContext ctx)
        => ctx.OffHost && ctx.World.Focus == World.WorldFocus.Focused ? Engine.Current?.InputInterface : null;

    public static bool KeysOpen(InputInterface input) => input.Keyboard != null && input.Actions?.TextFocusHeld != true;
}
