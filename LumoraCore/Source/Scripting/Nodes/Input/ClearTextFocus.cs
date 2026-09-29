// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Takes the keys back from whichever field in this world this machine is typing into, keeping what was
// typed, and says whether there was one. A field in another world, the dash's included, is left alone.
[Node("Input/Text Focus/Clear Focus", Capability.Input)]
public sealed class ClearTextFocus : ActionNode
{
    [return: Port("cleared")]
    public static bool Run(ScriptContext ctx)
    {
        if (ScriptTextFocus.Focused(ctx) is not { } field)
            return false;
        field.Unfocus();
        return true;
    }
}
