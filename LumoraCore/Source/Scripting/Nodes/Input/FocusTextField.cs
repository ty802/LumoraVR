// Copyright (c) 2026 LUMORAVR LTD. All rights reserved.
// Licensed under the LumoraVR Source Available License. See LICENSE in the project root.

using Lumora.Script;

namespace Lumora.Core.Scripting;

// Gives this machine's keys to the text field on the slot, the way clicking it does, and says whether it
// did. Only in answer to something the person here did, such as their own press or grab, while this world
// is in front of them, on a field they could type into themselves. Anywhere else it does nothing.
[Node("Input/Text Focus/Focus Field", Capability.Input)]
public sealed class FocusTextField : ActionNode
{
    [return: Port("focused")]
    public static bool Run(ScriptContext ctx, Slot? slot)
    {
        if (ScriptTextFocus.FieldOn(ctx, slot) is not { } field || !ScriptTextFocus.ByThisPerson(ctx))
            return false;
        if (field.ReadOnly.Value || !field.CanInteract
            || ctx.World.DataModelPermissions?.AllowsDomain(ctx.World.LocalUser, DataModelPermissionDomain.Touch) == false)
            return false;
        field.Focus();
        return field.IsFocused;
    }
}
