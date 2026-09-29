// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Takes the keys back from the text field on the slot when this machine is typing into it, keeping what was
// typed, the way clicking away does, and says whether it had them.
[Node("Input/Text Focus/Unfocus Field", Capability.Input)]
public sealed class UnfocusTextField : ActionNode
{
    [return: Port("unfocused")]
    public static bool Run(ScriptContext ctx, Slot? slot)
    {
        if (ScriptTextFocus.FieldOn(ctx, slot) is not { IsFocused: true } field)
            return false;
        field.Unfocus();
        return true;
    }
}
