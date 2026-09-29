// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Whether this machine is typing into the text field on the slot.
[Node("Input/Text Focus/Has Focus", Capability.Input)]
public sealed class HasTextFocus : ReadNode
{
    [return: Port("focused")]
    public static bool Run(ScriptContext ctx, Slot? slot) => ScriptTextFocus.FieldOn(ctx, slot) is { IsFocused: true };
}
